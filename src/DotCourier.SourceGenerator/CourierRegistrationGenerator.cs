using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotCourier.SourceGenerator;

/// <summary>
/// Generates the DotCourier registrations of the assembly being compiled: every request, stream and
/// notification handler, processor, exception handler and validator it declares, registered through
/// calls closed over the concrete types. <c>AddCourier</c> then uses them instead of scanning the
/// assembly with reflection, with the same rules as the scan.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CourierRegistrationGenerator : IIncrementalGenerator
{
    internal static readonly DiagnosticDescriptor InaccessibleType = new(
        id: "DOTCOURIER001",
        title: "Handler not reachable from generated code",
        messageFormat: "'{0}' is not accessible from generated code, so DotCourier scans this assembly with reflection instead. Make it internal or public to keep the compile-time registrations.",
        category: "DotCourier",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateHandler = new(
        id: "DOTCOURIER002",
        title: "Request handled twice",
        messageFormat: "'{0}' and '{1}' both handle '{2}'; only '{0}' is used",
        category: "DotCourier",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private const string RegistryMetadataName = "DotCourier.Generated.GeneratedCourierRegistry";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly SymbolDisplayFormat FullyQualifiedWithoutGenerics =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var usesCourier = context.CompilationProvider
            .Select(static (compilation, _) => compilation.GetTypeByMetadataName(RegistryMetadataName) is not null);

        var types = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (syntax, cancellationToken) => Describe(syntax, cancellationToken))
            .Where(static type => type is not null)
            .Collect();

        // The order the reflection scan sees (metadata order) decides the order notification handlers
        // run in and which of two handlers for a request wins, so the generated calls follow it.
        var typeOrder = context.CompilationProvider
            .Select(static (compilation, _) => new EquatableArray<string>(MetadataOrder(compilation).ToImmutableArray()));

        context.RegisterSourceOutput(types.Combine(usesCourier).Combine(typeOrder), static (output, input) =>
        {
            if (input.Left.Right)
            {
                Emit(output, input.Left.Left!, input.Right);
            }
        });
    }

    /// <summary>Types with a base list: a handler, behavior or notification always names an interface or a base class.</summary>
    private static bool IsCandidate(SyntaxNode node) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax
        && ((TypeDeclarationSyntax)node).BaseList is not null;

    internal static HandlerType? Describe(GeneratorSyntaxContext syntax, CancellationToken cancellationToken)
    {
        if (syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, cancellationToken) is not INamedTypeSymbol type
            || type.IsAbstract
            || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return null;
        }

        // Same rule as the reflection scan: types nested in generic types are skipped.
        for (var container = type.ContainingType; container is not null; container = container.ContainingType)
        {
            if (container.IsGenericType)
            {
                return null;
            }
        }

        var compilation = syntax.SemanticModel.Compilation;
        var open = type.TypeParameters.Length > 0;
        var registrations = ImmutableArray.CreateBuilder<Registration>();
        var reachable = compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
        var accessible = reachable;
        var handlerName = type.ToDisplayString(FullyQualified);

        // Extras for compile-time mode; skipped silently when generated code cannot reach the type,
        // since the reflection scan does not register them either.
        if (reachable && !open && type.AllInterfaces.Any(i => IsCourier(i, "INotification")))
        {
            registrations.Add(new Registration(RegistrationKind.Notification, new EquatableArray<string>(ImmutableArray.Create(handlerName)), string.Empty));
        }

        if (reachable && open && ClosedBehavior(type) is { } behavior)
        {
            registrations.Add(behavior);
        }

        if (type.TypeKind != TypeKind.Class)
        {
            return registrations.Count == 0 ? null : new HandlerType(NameOf(type), open, true, new EquatableArray<Registration>(registrations.ToImmutable()), LocationInfo.From(type.Locations.FirstOrDefault()));
        }

        var handlerKinds = 0;
        foreach (var @interface in type.AllInterfaces)
        {
            var kind = KindOf(@interface);
            if (kind is null)
            {
                continue;
            }

            handlerKinds++;
            if (open)
            {
                continue; // the runtime applies the scan's rules for open generics: only the type is needed
            }

            accessible &= @interface.TypeArguments.All(argument => compilation.IsSymbolAccessibleWithin(argument, compilation.Assembly));
            var arguments = @interface.TypeArguments.Select(argument => argument.ToDisplayString(FullyQualified)).ToList();
            var serviceKey = @interface.ToDisplayString(FullyQualified);
            if (kind == RegistrationKind.Service)
            {
                arguments = new List<string> { serviceKey };
            }

            arguments.Add(handlerName);
            registrations.Add(new Registration(kind.Value, new EquatableArray<string>(arguments.ToImmutableArray()), serviceKey));
        }

        if (open && handlerKinds > 0)
        {
            registrations.Add(new Registration(RegistrationKind.Service, default, "open"));
        }

        if (registrations.Count == 0)
        {
            return null;
        }

        return new HandlerType(
            NameOf(type),
            open,
            accessible || handlerKinds == 0,
            new EquatableArray<Registration>(registrations.ToImmutable()),
            LocationInfo.From(type.Locations.FirstOrDefault()));
    }

    private static bool IsCourier(INamedTypeSymbol @interface, string metadataName) =>
        @interface.OriginalDefinition.MetadataName == metadataName
        && @interface.OriginalDefinition.ContainingNamespace?.ToDisplayString() == "DotCourier";

    /// <summary>
    /// For an open behavior whose two type parameters are exactly the behavior interface's (in any order) and
    /// carry no constraint but <c>notnull</c>: <c>typeof(B&lt;,&gt;)</c> and <c>B&lt;TRequest, TResponse&gt;</c>, which
    /// the generated <c>BehaviorTypes</c> can name inside its generic method.
    /// </summary>
    private static Registration? ClosedBehavior(INamedTypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            var kind = IsCourier(@interface, "IPipelineBehavior`2") ? RegistrationKind.RequestBehavior
                : IsCourier(@interface, "IStreamPipelineBehavior`2") ? RegistrationKind.StreamBehavior
                : (RegistrationKind?)null;
            if (kind is null
                || type.TypeParameters.Length != 2
                || @interface.TypeArguments[0] is not ITypeParameterSymbol request
                || @interface.TypeArguments[1] is not ITypeParameterSymbol response
                || SymbolEqualityComparer.Default.Equals(request, response)
                || !type.TypeParameters.Contains(request, SymbolEqualityComparer.Default)
                || !type.TypeParameters.Contains(response, SymbolEqualityComparer.Default)
                || type.TypeParameters.Any(HasConstraintBeyondNotNull))
            {
                continue;
            }

            var arguments = type.TypeParameters.Select(p => SymbolEqualityComparer.Default.Equals(p, request) ? "TRequest" : "TResponse");
            var name = type.ToDisplayString(FullyQualifiedWithoutGenerics);
            return new Registration(
                kind.Value,
                new EquatableArray<string>(ImmutableArray.Create(name + "<,>", name + "<" + string.Join(", ", arguments) + ">")),
                string.Empty);
        }

        return null;
    }

    private static bool HasConstraintBeyondNotNull(ITypeParameterSymbol parameter) =>
        parameter.HasReferenceTypeConstraint
        || parameter.HasValueTypeConstraint
        || parameter.HasUnmanagedTypeConstraint
        || parameter.HasConstructorConstraint
        || parameter.ConstraintTypes.Length > 0;

    /// <summary>How generated code names the type: <c>global::Ns.Handler</c>, or <c>global::Ns.Handler&lt;,&gt;</c> when open.</summary>
    /// <remarks>
    /// Arity from the type's own parameters: <c>IsGenericType</c> is also true for a type nested in a
    /// generic one, which has none of its own.
    /// </remarks>
    private static string NameOf(INamedTypeSymbol type) => type.TypeParameters.Length > 0
        ? type.ToDisplayString(FullyQualifiedWithoutGenerics) + "<" + new string(',', type.TypeParameters.Length - 1) + ">"
        : type.ToDisplayString(FullyQualified);

    /// <summary>
    /// The order the compiler writes types to metadata, which is the order <c>Assembly.DefinedTypes</c>
    /// (the reflection scan) returns them: namespaces are walked with a stack, so each namespace's types
    /// come in declaration order and its child namespaces in reverse; nested types come after every
    /// top-level type, breadth first.
    /// </summary>
    internal static List<string> MetadataOrder(Compilation compilation)
    {
        var order = new List<string>();
        var nested = new Queue<INamedTypeSymbol>();
        var namespaces = new Stack<INamespaceSymbol>();
        namespaces.Push(compilation.Assembly.GlobalNamespace);
        while (namespaces.Count > 0)
        {
            foreach (var member in namespaces.Pop().GetMembers())
            {
                if (member is INamespaceSymbol child)
                {
                    namespaces.Push(child);
                }
                else if (member is INamedTypeSymbol type)
                {
                    order.Add(NameOf(type));
                    EnqueueNested(type, nested);
                }
            }
        }

        while (nested.Count > 0)
        {
            var type = nested.Dequeue();
            order.Add(NameOf(type));
            EnqueueNested(type, nested);
        }

        return order;
    }

    private static void EnqueueNested(INamedTypeSymbol type, Queue<INamedTypeSymbol> nested)
    {
        foreach (var member in type.GetTypeMembers())
        {
            nested.Enqueue(member);
        }
    }

    private static RegistrationKind? KindOf(INamedTypeSymbol @interface)
    {
        var definition = @interface.OriginalDefinition;
        if (definition.ContainingNamespace?.ToDisplayString() != "DotCourier")
        {
            return null;
        }

        return definition.MetadataName switch
        {
            "IRequestHandler`2" => RegistrationKind.RequestHandler,
            "IStreamRequestHandler`2" => RegistrationKind.StreamHandler,
            "INotificationHandler`1" => RegistrationKind.NotificationHandler,
            "IRequestExceptionHandler`3" => RegistrationKind.ExceptionHandler,
            "IRequestPreProcessor`1" or "IRequestPostProcessor`2" or "IRequestValidator`1" => RegistrationKind.Service,
            _ => null,
        };
    }

    private static void Emit(SourceProductionContext output, ImmutableArray<HandlerType?> found, EquatableArray<string> typeOrder)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in typeOrder)
        {
            if (!positions.ContainsKey(name))
            {
                positions[name] = positions.Count;
            }
        }

        // A partial class with several base lists is seen once per declaration: keep one.
        var types = found
            .Where(type => type is not null)
            .Select(type => type!)
            .GroupBy(type => type.Name)
            .Select(group => group.First())
            .OrderBy(type => positions.TryGetValue(type.Name, out var position) ? position : int.MaxValue)
            .ToList();

        var body = new StringBuilder();
        var requestBehaviors = new List<Registration>();
        var streamBehaviors = new List<Registration>();
        var inaccessible = types.Where(type => !type.IsAccessible).ToList();
        if (inaccessible.Count > 0)
        {
            foreach (var type in inaccessible)
            {
                output.ReportDiagnostic(Diagnostic.Create(InaccessibleType, type.Location?.ToLocation(), type.Name.Replace("global::", string.Empty)));
            }

            body.AppendLine("            context.ScanAssembly();");
        }
        else
        {
            ReportDuplicates(output, types);
            foreach (var type in types)
            {
                foreach (var registration in type.Registrations)
                {
                    switch (registration.Kind)
                    {
                        case RegistrationKind.RequestBehavior:
                            requestBehaviors.Add(registration);
                            break;
                        case RegistrationKind.StreamBehavior:
                            streamBehaviors.Add(registration);
                            break;
                        case RegistrationKind.Service when type.IsOpenGeneric:
                            body.AppendLine($"            context.OpenGeneric(typeof({type.Name}));");
                            break;
                        default:
                            body.AppendLine($"            context.{registration.Kind}<{string.Join(", ", registration.TypeArguments)}>();");
                            break;
                    }
                }
            }

            if (requestBehaviors.Count + streamBehaviors.Count > 0)
            {
                body.AppendLine("            context.BehaviorTypes(new GeneratedBehaviorTypes());");
            }
        }

        output.AddSource("DotCourier.Registrations.g.cs", $$"""
            // <auto-generated/>
            // Generated by DotCourier.SourceGenerator: the handlers of this assembly, registered without reflection.
            #nullable disable

            namespace DotCourier.Generated
            {
                [global::System.CodeDom.Compiler.GeneratedCode("DotCourier.SourceGenerator", "{{typeof(CourierRegistrationGenerator).Assembly.GetName().Version}}")]
                [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                internal static class CourierGeneratedRegistrations
                {
                    [global::System.Runtime.CompilerServices.ModuleInitializer]
                    internal static void Initialize() =>
                        global::DotCourier.Generated.GeneratedCourierRegistry.Register(typeof(CourierGeneratedRegistrations).Assembly, Register);

                    private static void Register(global::DotCourier.Generated.GeneratedRegistrationContext context)
                    {
            {{body.ToString().TrimEnd()}}
                    }
            {{BehaviorTypesClass(requestBehaviors, streamBehaviors)}}
                }
            }

            """);
    }

    /// <summary>
    /// Closes this assembly's open behaviors for compile-time mode (Native AOT), where the container cannot
    /// close open generics: <c>typeof(B&lt;TRequest, TResponse&gt;)</c> inside a generic method needs no runtime code.
    /// </summary>
    private static string BehaviorTypesClass(List<Registration> request, List<Registration> stream)
    {
        if (request.Count + stream.Count == 0)
        {
            return string.Empty;
        }

        static string Method(string name, List<Registration> behaviors)
        {
            var text = new StringBuilder();
            text.AppendLine("            [return: global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]");
            text.AppendLine($"            public override global::System.Type {name}<[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::DotCourier.Generated.CompileTimeTypes.Request)] TRequest, TResponse>(global::System.Type openBehavior)");
            text.AppendLine("            {");
            foreach (var behavior in behaviors)
            {
                text.AppendLine($"                if (openBehavior == typeof({behavior.TypeArguments.Items[0]})) return typeof({behavior.TypeArguments.Items[1]});");
            }

            text.AppendLine("                return null;");
            text.Append("            }");
            return text.ToString();
        }

        var methods = new List<string>();
        if (request.Count > 0)
        {
            methods.Add(Method("CloseRequestBehavior", request));
        }

        if (stream.Count > 0)
        {
            methods.Add(Method("CloseStreamBehavior", stream));
        }

        if (request.Count == 0)
        {
            methods.Insert(0, "            public override global::System.Type CloseRequestBehavior<[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::DotCourier.Generated.CompileTimeTypes.Request)] TRequest, TResponse>(global::System.Type openBehavior) => null;");
        }

        return $$"""

                    private sealed class GeneratedBehaviorTypes : global::DotCourier.Generated.BehaviorTypes
                    {
            {{string.Join("\n\n", methods)}}
                    }
            """;
    }

    /// <summary>Like the scan, the first handler of a request wins; say so when that hides another one.</summary>
    private static void ReportDuplicates(SourceProductionContext output, List<HandlerType> types)
    {
        var owners = new Dictionary<string, HandlerType>(StringComparer.Ordinal);
        foreach (var type in types.Where(type => !type.IsOpenGeneric))
        {
            foreach (var registration in type.Registrations.Where(r => r.Kind is RegistrationKind.RequestHandler or RegistrationKind.StreamHandler))
            {
                if (owners.TryGetValue(registration.ServiceKey, out var first))
                {
                    output.ReportDiagnostic(Diagnostic.Create(
                        DuplicateHandler,
                        type.Location?.ToLocation(),
                        first.Name.Replace("global::", string.Empty),
                        type.Name.Replace("global::", string.Empty),
                        registration.TypeArguments.Items[0].Replace("global::", string.Empty)));
                }
                else
                {
                    owners[registration.ServiceKey] = type;
                }
            }
        }
    }
}
