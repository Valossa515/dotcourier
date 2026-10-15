using System.Collections.Immutable;
using System.Runtime.Loader;
using DotCourier.Generated;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.SourceGenerator.Tests;

public class GeneratorTests
{
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Append(typeof(ICourier).Assembly.Location)
        .Distinct()
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    // What an SDK project gets from ImplicitUsings.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static readonly CSharpParseOptions Parse = new(LanguageVersion.Latest);

    private static (string? Source, ImmutableArray<Diagnostic> Diagnostics, Compilation Output) Run(string code, bool referenceCourier = true)
    {
        var compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText(code, Parse), CSharpSyntaxTree.ParseText(ImplicitUsings, Parse)],
            referenceCourier ? References : References.Where(r => !r.Display!.EndsWith("DotCourier.dll", StringComparison.Ordinal)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CourierRegistrationGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var source = driver.GetRunResult().GeneratedTrees.SingleOrDefault()?.ToString();
        return (source, diagnostics, output);
    }

    private static void AssertCompiles(Compilation output) =>
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));

    [Fact]
    public void Registers_every_kind_of_handler_with_closed_calls()
    {
        var (source, diagnostics, output) = Run("""
            using DotCourier;
            namespace Shop;
            public sealed record Ping(string Text) : IRequest<string>;
            internal sealed class PingHandler : IRequestHandler<Ping, string>
            {
                public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult(request.Text);
            }
            public sealed record Delete(int Id) : ICommand;
            public sealed class DeleteHandler : ICommandHandler<Delete>
            {
                public Task Handle(Delete request, CancellationToken ct) => Task.CompletedTask;
            }
            public sealed record Feed : IStreamRequest<int>;
            public sealed class FeedHandler : IStreamRequestHandler<Feed, int>
            {
                public async IAsyncEnumerable<int> Handle(Feed request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { yield return 1; await Task.Yield(); }
            }
            public sealed record Shipped : INotification;
            public sealed class ShippedHandler : INotificationHandler<Shipped>
            {
                public Task Handle(Shipped notification, CancellationToken ct) => Task.CompletedTask;
            }
            public sealed class PingValidator : IRequestValidator<Ping>
            {
                public ValueTask<IEnumerable<ValidationError>> ValidateAsync(Ping request, CancellationToken ct) => new([]);
            }
            public sealed class AuditAll<T> : INotificationHandler<T> where T : INotification
            {
                public Task Handle(T notification, CancellationToken ct) => Task.CompletedTask;
            }
            public abstract class NotRegistered : INotificationHandler<Shipped>
            {
                public abstract Task Handle(Shipped notification, CancellationToken ct);
            }
            """);

        Assert.Empty(diagnostics);
        Assert.NotNull(source);
        Assert.Contains("context.RequestHandler<global::Shop.Ping, string, global::Shop.PingHandler>();", source);
        Assert.Contains("context.RequestHandler<global::Shop.Delete, global::DotCourier.Unit, global::Shop.DeleteHandler>();", source);
        Assert.Contains("context.StreamHandler<global::Shop.Feed, int, global::Shop.FeedHandler>();", source);
        Assert.Contains("context.NotificationHandler<global::Shop.Shipped, global::Shop.ShippedHandler>();", source);
        Assert.Contains("context.Service<global::DotCourier.IRequestValidator<global::Shop.Ping>, global::Shop.PingValidator>();", source);
        Assert.Contains("context.OpenGeneric(typeof(global::Shop.AuditAll<>));", source);
        Assert.DoesNotContain("NotRegistered", source);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source);
        AssertCompiles(output);
    }

    [Fact]
    public void A_private_handler_makes_the_assembly_fall_back_to_scanning_with_a_warning()
    {
        var (source, diagnostics, output) = Run("""
            using DotCourier;
            namespace Shop;
            public sealed record Ping : IRequest<string>;
            public static class Outer
            {
                private sealed class HiddenHandler : IRequestHandler<Ping, string>
                {
                    public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult("x");
                }
            }
            """);

        var warning = Assert.Single(diagnostics);
        Assert.Equal("DOTCOURIER001", warning.Id);
        Assert.Contains("Shop.Outer.HiddenHandler", warning.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("context.ScanAssembly();", source);
        Assert.DoesNotContain("context.RequestHandler", source);
        AssertCompiles(output);
    }

    [Fact]
    public void A_private_request_type_also_falls_back()
    {
        var (source, diagnostics, _) = Run("""
            using DotCourier;
            namespace Shop;
            public static class Outer
            {
                private sealed record Secret : IRequest<int>;
                internal sealed class SecretHandler : IRequestHandler<Secret, int>
                {
                    public Task<int> Handle(Secret request, CancellationToken ct) => Task.FromResult(1);
                }
            }
            """);

        Assert.Equal("DOTCOURIER001", Assert.Single(diagnostics).Id);
        Assert.Contains("context.ScanAssembly();", source);
    }

    [Fact]
    public void Two_handlers_for_one_request_are_reported()
    {
        var (_, diagnostics, _) = Run("""
            using DotCourier;
            namespace Shop;
            public sealed record Ping : IRequest<string>;
            public sealed class FirstHandler : IRequestHandler<Ping, string>
            {
                public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult("1");
            }
            public sealed class SecondHandler : IRequestHandler<Ping, string>
            {
                public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult("2");
            }
            """);

        var warning = Assert.Single(diagnostics);
        Assert.Equal("DOTCOURIER002", warning.Id);
        Assert.Equal("'Shop.FirstHandler' and 'Shop.SecondHandler' both handle 'Shop.Ping'; only 'Shop.FirstHandler' is used", warning.GetMessage());
    }

    [Fact]
    public void Partial_classes_are_registered_once_and_nullable_responses_compile()
    {
        var (source, diagnostics, output) = Run("""
            using DotCourier;
            namespace Shop;
            public sealed record Find(int Id) : IRequest<string?>;
            public sealed partial class FindHandler : IRequestHandler<Find, string?>
            {
                public Task<string?> Handle(Find request, CancellationToken ct) => Task.FromResult<string?>(null);
            }
            public sealed partial class FindHandler : System.IDisposable
            {
                public void Dispose() { }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Single(source!.Split('\n'), line => line.Contains("context.RequestHandler<", StringComparison.Ordinal));
        AssertCompiles(output);
    }

    [Fact]
    public void Types_nested_in_generic_types_are_skipped_like_the_scan_does()
    {
        var (source, diagnostics, output) = Run("""
            using DotCourier;
            namespace Shop;
            public sealed record Shipped : INotification;
            public sealed class Container<T>
            {
                public sealed class Inner : INotificationHandler<Shipped>
                {
                    public Task Handle(Shipped notification, CancellationToken ct) => Task.CompletedTask;
                }
            }
            """);

        Assert.Empty(diagnostics); // includes a generator crash (CS8785)
        Assert.NotNull(source);
        Assert.DoesNotContain("Inner", source);
        AssertCompiles(output);
    }

    [Fact]
    public void Nothing_is_generated_without_a_reference_to_DotCourier()
    {
        var (source, diagnostics, _) = Run("public sealed class Plain : System.IDisposable { public void Dispose() { } }", referenceCourier: false);

        Assert.Null(source);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_assembly_without_handlers_still_registers_itself()
    {
        var (source, _, output) = Run("namespace Shop; public sealed class Plain : System.IDisposable { public void Dispose() { } }");

        Assert.NotNull(source);
        Assert.Contains("GeneratedCourierRegistry.Register(", source);
        AssertCompiles(output);
    }

    [Fact]
    public void The_output_is_deterministic()
    {
        const string code = """
            using DotCourier;
            namespace Shop;
            public sealed record Shipped : INotification;
            public sealed class B : INotificationHandler<Shipped> { public Task Handle(Shipped n, CancellationToken ct) => Task.CompletedTask; }
            public sealed class A : INotificationHandler<Shipped> { public Task Handle(Shipped n, CancellationToken ct) => Task.CompletedTask; }
            """;

        var first = Run(code).Source;

        Assert.Equal(first, Run(code).Source);
        Assert.True(first!.IndexOf("global::Shop.B>", StringComparison.Ordinal) < first.IndexOf("global::Shop.A>", StringComparison.Ordinal)); // declaration order, not alphabetical
    }

    [Fact]
    public async Task A_compiled_assembly_registers_itself_even_before_any_of_its_code_ran()
    {
        var (_, diagnostics, output) = Run("""
            using DotCourier;
            namespace Shop;
            public sealed record Ping(string Text) : IRequest<string>;
            public sealed class PingHandler : IRequestHandler<Ping, string>
            {
                public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult("pong: " + request.Text);
            }
            """);
        Assert.Empty(diagnostics);
        using var image = new MemoryStream();
        var emit = output.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        image.Position = 0;

        // Loaded but never executed: its module initializer has not run when AddCourier sees it.
        var assembly = new AssemblyLoadContext("generated-sample").LoadFromStream(image);
        var services = new ServiceCollection().AddCourier(cfg => cfg.RegisterServicesFromAssembly(assembly)).BuildServiceProvider();

        Assert.NotNull(GeneratedCourierRegistry.Find(assembly));
        var ping = Activator.CreateInstance(assembly.GetType("Shop.Ping")!, "hi")!;
        Assert.Equal("pong: hi", await services.GetRequiredService<ICourier>().Send(ping));
    }

    [Fact]
    public void Emits_what_compile_time_mode_needs()
    {
        var (source, diagnostics, output) = Run("""
            using DotCourier;
            namespace Shop;
            public sealed record Ping : IRequest<string>;
            public sealed class PingHandler : IRequestHandler<Ping, string>
            {
                public Task<string> Handle(Ping request, CancellationToken ct) => Task.FromResult("x");
            }
            public sealed class PingFallback : IRequestExceptionHandler<Ping, string, InvalidOperationException>
            {
                public Task Handle(Ping request, InvalidOperationException exception, RequestExceptionHandlerState<string> state, CancellationToken ct) => Task.CompletedTask;
            }
            public sealed record Unheard(int Id) : INotification;
            public readonly record struct Tick(int N) : INotification;
            public sealed class Audit<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
            {
                public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct) => next(ct);
            }
            public sealed class Swapped<TResponse, TRequest> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
            {
                public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct) => next(ct);
            }
            public sealed class ClassOnly<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : class
            {
                public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct) => next(ct);
            }
            public sealed class StreamAudit<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse> where TRequest : notnull
            {
                public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken ct) => next();
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("context.ExceptionHandler<global::Shop.Ping, string, global::System.InvalidOperationException, global::Shop.PingFallback>();", source);
        Assert.Contains("context.Notification<global::Shop.Unheard>();", source);
        Assert.Contains("context.Notification<global::Shop.Tick>();", source);
        Assert.Contains("context.BehaviorTypes(new GeneratedBehaviorTypes());", source);
        Assert.Contains("if (openBehavior == typeof(global::Shop.Audit<,>)) return typeof(global::Shop.Audit<TRequest, TResponse>);", source);
        Assert.Contains("if (openBehavior == typeof(global::Shop.Swapped<,>)) return typeof(global::Shop.Swapped<TResponse, TRequest>);", source);
        Assert.Contains("if (openBehavior == typeof(global::Shop.StreamAudit<,>)) return typeof(global::Shop.StreamAudit<TRequest, TResponse>);", source);
        Assert.DoesNotContain("ClassOnly", source); // its constraint cannot be met from the generic closer
        AssertCompiles(output);
    }
}
