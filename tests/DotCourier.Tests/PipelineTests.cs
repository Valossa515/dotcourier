using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotCourier.Behaviors;

namespace DotCourier.Tests;

public class PipelineTests
{
    [Fact]
    public async Task Behaviors_run_by_order_not_by_registration()
    {
        var (services, log) = TestHost.Build(cfg => cfg
            .AddOpenBehavior(typeof(InnerBehavior<,>), order: 10)
            .AddOpenBehavior(typeof(OuterBehavior<,>), order: 5));

        await services.Courier().Send(new CreateItem("a"));

        Assert.Equal(["outer>", "inner>", "created a", "<inner", "<outer"], log.Entries);
    }

    [Fact]
    public async Task Behaviors_with_equal_order_keep_registration_order()
    {
        var (services, log) = TestHost.Build(cfg => cfg
            .AddOpenBehavior(typeof(InnerBehavior<,>))
            .AddOpenBehavior(typeof(OuterBehavior<,>)));

        await services.Courier().Send(new CreateItem("a"));

        Assert.Equal(["inner>", "outer>", "created a", "<outer", "<inner"], log.Entries);
    }

    [Fact]
    public async Task Closed_behaviors_apply_only_to_their_request()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddBehavior<PingOnlyBehavior>());

        Assert.Equal("pong: x (decorated)", await services.Courier().Send(new Ping("x")));
        Assert.Equal("item 1", (await services.Courier().Dispatch(new GetItem(1))).Data);
    }

    [Fact]
    public async Task Validation_reports_every_failure_including_record_parameter_attributes()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddOpenBehavior(typeof(OuterBehavior<,>)));

        var response = await services.Courier().Dispatch(new Register(null, "not-an-email", 12));

        Assert.False(response.IsSuccess);
        Assert.Equal(400, response.StatusCode);
        Assert.True(response.IsValidationFailure);
        var errors = response.GetErrorDetails<IReadOnlyList<ValidationError>>()!;
        Assert.Equal(["Age", "Email", "Name"], errors.Select(e => e.Field).Order());
        // User behaviors sit inside validation, so an invalid request never reaches them.
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Validation_runs_custom_validators()
    {
        var (services, _) = TestHost.Build();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => services.Courier().Send(new Register("admin", "a@b.com", 30)));

        Assert.Equal("Name is reserved", Assert.Single(ex.Errors).Message);
    }

    [Fact]
    public async Task Validation_passes_valid_requests()
    {
        var (services, log) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Register("Ana", "ana@example.com", 30));

        Assert.True(response.IsSuccess);
        Assert.Equal(["registered"], log.Entries);
    }

    [Fact]
    public async Task Validation_can_skip_DataAnnotations()
    {
        var (services, _) = TestHost.Build(cfg => cfg.UseDataAnnotations = false);

        var response = await services.Courier().Dispatch(new Register(null, "nope", 1));

        Assert.True(response.IsSuccess);
    }

    [Fact]
    public async Task Timeout_cancels_the_handler_and_returns_504()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Slow(5_000));

        Assert.Equal(504, response.StatusCode);
        Assert.Contains("timed out", response.Error);
    }

    [Fact]
    public async Task Timeout_returns_even_when_the_handler_ignores_the_token()
    {
        var (services, _) = TestHost.Build();
        var watch = Stopwatch.StartNew();

        var response = await services.Courier().Dispatch(new Stubborn());

        Assert.Equal(504, response.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(1_500), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Timeout_does_not_affect_fast_handlers()
    {
        var (services, _) = TestHost.Build();

        Assert.Equal("done", await services.Courier().Send(new Relaxed()));
    }

    [Fact]
    public async Task Telemetry_emits_activities_and_metrics()
    {
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CourierDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.DisplayName == "courier " + nameof(TelemetryProbe))
                {
                    lock (activities)
                    {
                        activities.Add(activity);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        var outcomes = new List<string>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == CourierDiagnostics.SourceName && instrument.Name == CourierDiagnostics.RequestsMetric)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var tagArray = tags.ToArray();
            if (tagArray.Any(t => t.Key == "courier.request.type" && (string?)t.Value == nameof(TelemetryProbe)))
            {
                lock (outcomes)
                {
                    outcomes.Add((string)tagArray.Single(t => t.Key == "courier.outcome").Value!);
                }
            }
        });
        meterListener.Start();

        var (services, _) = TestHost.Build();
        await services.Courier().Dispatch(new TelemetryProbe(false));
        await services.Courier().Dispatch(new TelemetryProbe(true));

        Assert.Equal(2, activities.Count);
        Assert.Contains(activities, a => a.Status == ActivityStatusCode.Error);
        Assert.Equal(["error", "success"], outcomes.Order());
    }
}
