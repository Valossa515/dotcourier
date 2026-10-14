using DotCourier.Resilience;
using Polly;

namespace DotCourier.Tests.Resilience;

public class ResilienceTests
{
    private static void FastRetry(CourierResilienceOptions o, int attempts = 3)
    {
        o.Retry.Enabled = true;
        o.Retry.MaxAttempts = attempts;
        o.Retry.Delay = TimeSpan.Zero;
        o.Retry.UseJitter = false;
    }

    [Fact]
    public async Task Retry_recovers_from_transient_failures()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddResilience(o => FastRetry(o)));

        var result = await services.Courier().Send(new Flaky("f", 2));

        Assert.Equal("ok after 3", result);
        Assert.Equal(3, log.Entries.Length);
    }

    [Fact]
    public async Task Retry_is_off_by_default()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddResilience());

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Courier().Send(new Flaky("f", 1)));

        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task Retry_skips_client_errors_that_would_fail_again()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddResilience(o => FastRetry(o)));

        var response = await services.Courier().Dispatch(new MissingThing("m"));

        Assert.Equal(404, response.StatusCode);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task ForRequest_overrides_the_defaults_for_one_type()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddResilience(o =>
        {
            FastRetry(o);
            o.ForRequest<AlwaysFails>(r => r.Retry.Enabled = false);
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Courier().Send(new AlwaysFails("a")));
        await services.Courier().Send(new Flaky("f", 1));

        Assert.Equal(["a", "f", "f"], log.Entries);
    }

    [Fact]
    public async Task Circuit_breaker_sits_outside_retry_and_counts_one_failure_per_request()
    {
        var (services, log) = TestHost.Build(cfg => cfg.AddResilience(o =>
        {
            FastRetry(o);
            o.CircuitBreaker.MinimumThroughput = 2;
            o.CircuitBreaker.FailureRatio = 0.5;
        }));
        var courier = services.Courier();

        // 1st request: 3 attempts, but the breaker (outside retry) records a single failure,
        // which is below MinimumThroughput, so the circuit stays closed.
        await Assert.ThrowsAsync<InvalidOperationException>(() => courier.Send(new AlwaysFails("a")));
        Assert.Equal(3, log.Entries.Length);

        // 2nd request: 3 more attempts; now 2 failed requests out of 2 open the circuit.
        await Assert.ThrowsAsync<InvalidOperationException>(() => courier.Send(new AlwaysFails("a")));
        Assert.Equal(6, log.Entries.Length);

        // 3rd request: rejected once, without touching the handler and without being retried.
        var response = await courier.Dispatch(new AlwaysFails("a"));
        Assert.Equal(6, log.Entries.Length);
        Assert.Equal(503, response.StatusCode);
        Assert.Equal("Service temporarily unavailable: the circuit is open for this operation", response.Error);
    }

    [Fact]
    public async Task Circuits_are_isolated_per_request_type()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddResilience(o => o.CircuitBreaker.MinimumThroughput = 2));
        var courier = services.Courier();

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => courier.Send(new AlwaysFails("a")));
        }

        await Assert.ThrowsAsync<CircuitOpenException>(() => courier.Send(new AlwaysFails("a")));
        Assert.Equal("ok after 1", await courier.Send(new Flaky("f", 0)));
    }

    [Fact]
    public async Task Rate_limiter_returns_429_and_invalid_requests_do_not_consume_quota()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddResilience(o =>
        {
            o.RateLimiter.Enabled = true;
            o.RateLimiter.PermitLimit = 2;
            o.RateLimiter.Window = TimeSpan.FromMinutes(1);
        }));
        var courier = services.Courier();

        Assert.True((await courier.Dispatch(new Limited("a"))).IsSuccess);
        Assert.Equal(400, (await courier.Dispatch(new Limited(null))).StatusCode); // validation runs first
        Assert.True((await courier.Dispatch(new Limited("b"))).IsSuccess);

        var rejected = await courier.Dispatch(new Limited("c"));

        Assert.Equal(429, rejected.StatusCode);
        Assert.Equal("Too many requests: the rate limit for this operation is exhausted", rejected.Error);
    }

    [Fact]
    public async Task Bulkhead_rejects_excess_concurrency_with_503()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddResilience(o =>
        {
            o.Bulkhead.Enabled = true;
            o.Bulkhead.MaxConcurrency = 1;
        }));
        var courier = services.Courier();

        var first = courier.Dispatch(new Crowded("a", 500));
        await Task.Delay(100);
        var second = await courier.Dispatch(new Crowded("b", 0));

        Assert.Equal(503, second.StatusCode);
        Assert.Equal("Service busy: too many concurrent executions of this operation", second.Error);
        Assert.True((await first).IsSuccess);
    }

    [Fact]
    public async Task Bulkhead_sits_inside_retry_so_backoff_does_not_hold_a_slot()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddResilience(o =>
        {
            o.Retry.Enabled = true;
            o.Retry.MaxAttempts = 2;
            o.Retry.Delay = TimeSpan.FromMilliseconds(600);
            o.Retry.BackoffType = DelayBackoffType.Constant;
            o.Retry.UseJitter = false;
            o.Bulkhead.Enabled = true;
            o.Bulkhead.MaxConcurrency = 1;
        }));
        var courier = services.Courier();

        // "slow-fail" fails its first attempt immediately and then waits 600 ms to retry.
        var retrying = courier.Dispatch(new Crowded("slow-fail", 0));
        await Task.Delay(150);

        // During that backoff the single bulkhead slot must be free.
        var other = await courier.Dispatch(new Crowded("other", 0));

        Assert.True(other.IsSuccess, other.Error);
        Assert.True((await retrying).IsSuccess);
    }

    [Fact]
    public void Invalid_settings_fail_at_startup()
    {
        Assert.ThrowsAny<Exception>(() => TestHost.Build(cfg => cfg.AddResilience(o => o.CircuitBreaker.BreakDuration = TimeSpan.FromMilliseconds(10))));
        Assert.ThrowsAny<Exception>(() => TestHost.Build(cfg => cfg.AddResilience(o =>
            o.ForRequest<Limited>(r => { r.RateLimiter.Enabled = true; r.RateLimiter.PermitLimit = 0; }))));
    }

    [Fact]
    public void Default_transient_predicate_excludes_client_errors_and_cancellation()
    {
        Assert.True(CourierResilienceOptions.IsTransient(new TimeoutException()));
        Assert.True(CourierResilienceOptions.IsTransient(new CourierException("boom", 502)));
        Assert.False(CourierResilienceOptions.IsTransient(new CourierException("nope", 409)));
        Assert.False(CourierResilienceOptions.IsTransient(new ValidationException([])));
        Assert.False(CourierResilienceOptions.IsTransient(new OperationCanceledException()));
    }
}
