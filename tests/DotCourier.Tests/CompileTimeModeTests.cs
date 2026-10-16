using DotCourier.Generated;
using DotCourier.Tests.Generics;
using DotCourier.Tests.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Tests;

/// <summary>A behavior the generated code cannot close (constraint beyond notnull).</summary>
public sealed class ClassOnlyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next(cancellationToken);
}

/// <summary>A value-type response of its own (TelemetryProbe is counted by the telemetry test, which runs in parallel).</summary>
public sealed record Sum(int A, int B) : IRequest<int>;

public sealed class SumHandler : IRequestHandler<Sum, int>
{
    public Task<int> Handle(Sum request, CancellationToken cancellationToken) => Task.FromResult(request.A + request.B);
}

/// <summary>A closed behavior that records where it runs, to pin its position among open ones.</summary>
public sealed class PingMarkerBehavior(CallLog log) : IPipelineBehavior<Ping, string>
{
    public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken)
    {
        log.Add("marker>");
        var response = await next(cancellationToken);
        log.Add("<marker");
        return response;
    }
}

/// <summary>
/// Compile-time mode is what Native AOT runs: behaviors closed per request by BehaviorTypes, exception handlers
/// through typed invokers, nothing built with MakeGenericType. These tests run the same scenarios in both modes
/// (the published AOT app is exercised separately, in CI).
/// </summary>
public class CompileTimeModeTests
{
    private static (ServiceProvider Services, CallLog Log) Build(bool compileTime, Action<CourierConfiguration>? configure = null) =>
        TestHost.Build(cfg =>
        {
            cfg.RequireCompileTimeRegistrations = compileTime;
            configure?.Invoke(cfg);
        });

    private static async Task<string[]> Run(bool compileTime, Action<CourierConfiguration>? configure, Func<ICourier, Task> scenario)
    {
        var (services, log) = Build(compileTime, configure);
        await scenario(services.Courier());
        return log.Entries;
    }

    private static async Task AssertSameInBothModes(Func<ICourier, Task> scenario, Action<CourierConfiguration>? configure = null)
    {
        var reflection = await Run(compileTime: false, configure, scenario);
        var compileTime = await Run(compileTime: true, configure, scenario);

        Assert.Equal(reflection, compileTime);
    }

    [Fact]
    public void It_is_off_where_runtime_code_generation_is_available()
    {
        Assert.False(new CourierConfiguration().RequireCompileTimeRegistrations);
    }

    [Fact]
    public Task Open_closed_and_built_in_behaviors_run_in_the_same_order() =>
        AssertSameInBothModes(
            async courier => Assert.Equal("pong: hi (decorated)", await courier.Send(new Ping("hi"))),
            cfg => cfg
                .AddOpenBehavior(typeof(OuterBehavior<,>), order: -5)
                .AddOpenBehavior(typeof(InnerBehavior<,>), order: 5)
                .AddBehavior<PingOnlyBehavior>());

    [Fact]
    public Task A_closed_behavior_keeps_its_place_between_open_ones() =>
        AssertSameInBothModes(
            async courier => await courier.Send(new Ping("hi")),
            cfg => cfg
                .AddOpenBehavior(typeof(OuterBehavior<,>), order: -5)
                .AddBehavior<PingMarkerBehavior>(order: 0)
                .AddOpenBehavior(typeof(InnerBehavior<,>), order: 5));

    [Fact]
    public Task Value_type_responses_get_closed_behaviors_too() =>
        AssertSameInBothModes(
            async courier =>
            {
                Assert.Equal(42, await courier.Send(new Sum(40, 2)));
                var id = await courier.Send(new Register("Ana", "ana@example.com", 30));
                Assert.NotEqual(Guid.Empty, id);
            },
            cfg => cfg.AddOpenBehavior(typeof(OuterBehavior<,>)));

    [Fact]
    public Task DataAnnotations_and_validators_reject_the_same_requests() =>
        AssertSameInBothModes(async courier =>
        {
            var response = await courier.Dispatch(new Register(null, "not-an-email", 3));
            Assert.Equal(400, response.StatusCode);
            Assert.Equal(["Name", "Email", "Age"], response.GetErrorDetails<IReadOnlyList<ValidationError>>()!.Select(e => e.Field));
            Assert.Equal(400, (await courier.Dispatch(new Register("admin", "a@b.c", 30))).StatusCode);
            Assert.True((await courier.Dispatch(new Register("Ana", "a@b.c", 30))).IsSuccess);
        });

    [Fact]
    public Task Exception_handlers_supply_the_response() =>
        AssertSameInBothModes(async courier =>
        {
            Assert.Equal("fallback after broken", await courier.Send(new Fragile()));
            var failure = await courier.Dispatch(new Boom(AsCourierException: true));
            Assert.Equal(409, failure.StatusCode);
        });

    [Fact]
    public Task Notifications_reach_their_handlers_in_the_same_order() =>
        AssertSameInBothModes(async courier =>
        {
            await courier.Publish(new ItemCreated("fail"));
            await courier.Publish(new NobodyListens());
        });

    [Fact]
    public Task Streams_get_their_behaviors() =>
        AssertSameInBothModes(
            async courier =>
            {
                await foreach (var item in courier.CreateStream(new CountTo(2)))
                {
                    Assert.True(item > 0);
                }

                await Assert.ThrowsAsync<ValidationException>(async () =>
                {
                    await foreach (var _ in courier.CreateStream(new CountTo(0)))
                    {
                    }
                });
            },
            cfg => cfg.AddOpenStreamBehavior(typeof(TracingStreamBehavior<,>)));

    [Fact]
    public async Task Timeouts_still_apply()
    {
        var (services, _) = Build(compileTime: true);

        Assert.Equal(504, (await services.Courier().Dispatch(new Slow(5_000))).StatusCode);
    }

    [Fact]
    public async Task Open_generic_request_handlers_are_reported_as_missing()
    {
        var (reflection, _) = Build(compileTime: false);
        var (compileTime, _) = Build(compileTime: true);

        Assert.True((await reflection.Courier().Dispatch(new Create<string>("pen"))).IsSuccess);
        var missing = await compileTime.Courier().Dispatch(new Create<string>("pen"));
        Assert.False(missing.IsSuccess);
        Assert.Contains("Create<String>", missing.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_behavior_that_cannot_be_closed_fails_at_startup_with_the_reason()
    {
        Build(compileTime: false, cfg => cfg.AddOpenBehavior(typeof(ClassOnlyBehavior<,>)));

        var error = Assert.Throws<InvalidOperationException>(() => Build(compileTime: true, cfg => cfg.AddOpenBehavior(typeof(ClassOnlyBehavior<,>))));
        Assert.Contains("ClassOnlyBehavior", error.Message, StringComparison.Ordinal);
        Assert.Contains("AddBehaviorTypes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_without_generated_registrations_fails_at_startup()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() => services.AddCourier(cfg =>
        {
            cfg.RequireCompileTimeRegistrations = true;
            cfg.RegisterServicesFromAssemblyContaining<ICourier>(); // DotCourier itself: no generator
        }));

        Assert.Contains("DotCourier.SourceGenerator", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Behaviors_are_registered_closed_with_no_open_behavior_left()
    {
        var services = new ServiceCollection();
        services.AddLogging().AddSingleton(new CallLog()).AddCourier(cfg =>
        {
            cfg.RequireCompileTimeRegistrations = true;
            cfg.RegisterServicesFromAssemblyContaining<CallLog>();
            cfg.AddOpenBehavior(typeof(OuterBehavior<,>));
        });

        // Open handlers and validators stay open (the container closes them for reference types); behaviors do not.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPipelineBehavior<,>) || d.ServiceType == typeof(IStreamPipelineBehavior<,>));
        Assert.Contains(services, d => d.ServiceType == typeof(IPipelineBehavior<Ping, string>) && d.ImplementationType == typeof(OuterBehavior<Ping, string>));
        Assert.Contains(services, d => d.ServiceType == typeof(IPipelineBehavior<Sum, int>) && d.ImplementationType == typeof(Behaviors.LoggingBehavior<Sum, int>));
        Assert.Contains(services, d => d.ServiceType == typeof(IStreamPipelineBehavior<CountTo, int>) && d.ImplementationType == typeof(Behaviors.StreamValidationBehavior<CountTo, int>));
        Assert.Contains(services, d => d.ServiceType == typeof(IExceptionHandlerInvoker<Fragile, string>));
    }
}
