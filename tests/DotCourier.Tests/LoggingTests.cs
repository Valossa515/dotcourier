using DotCourier.Tests.Resilience;
using Microsoft.Extensions.Logging;

namespace DotCourier.Tests;

public class LoggingTests
{
    [Fact]
    public async Task Unexpected_exception_is_logged_once_with_its_stack_trace()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(logs: logs);

        await services.Courier().Dispatch(new Boom(false));

        var error = Assert.Single(logs.Entries, e => e.Level >= LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains("[Courier] <-- Boom threw InvalidOperationException", error.Message);
    }

    [Fact]
    public async Task Dispatch_still_logs_unexpected_exceptions_when_the_logging_behavior_is_off()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(cfg => cfg.EnableLogging = false, logs);

        await services.Courier().Dispatch(new Boom(false));

        var error = Assert.Single(logs.Entries, e => e.Level >= LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task Expected_business_errors_are_not_logged_as_errors()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(logs: logs);

        await services.Courier().Dispatch(new Boom(true));

        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("status=409"));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_logged_as_an_error()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(cfg => cfg.EnableTimeouts = false, logs);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.Courier().Dispatch(new Slow(5_000), cts.Token));

        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
        Assert.Contains(logs.Entries, e => e.Message.Contains("cancelled by the caller"));
    }

    [Fact]
    public async Task Retry_log_counts_attempts_including_the_first_call()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(cfg => cfg.AddResilience(o =>
        {
            o.Retry.Enabled = true;
            o.Retry.MaxAttempts = 4;
            o.Retry.Delay = TimeSpan.Zero;
        }), logs);

        await services.Courier().Send(new Flaky("f", 2));

        var retries = logs.Entries.Where(e => e.Message.Contains("failed with")).Select(e => e.Message).ToArray();
        Assert.Equal(2, retries.Length);
        Assert.Contains("attempt 2/4", retries[0]);
        Assert.Contains("attempt 3/4", retries[1]);
    }
}
