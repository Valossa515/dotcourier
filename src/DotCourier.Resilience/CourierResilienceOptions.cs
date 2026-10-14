using Polly;

namespace DotCourier.Resilience;

/// <summary>Retry settings. Off by default: retrying a non-idempotent command can apply it twice.</summary>
public sealed class RetrySettings
{
    /// <summary>Whether requests are retried. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Total attempts, including the first one. Default 3; 1 or less disables retrying.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry. Default 200 ms.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How the delay grows between retries. Default exponential.</summary>
    public DelayBackoffType BackoffType { get; set; } = DelayBackoffType.Exponential;

    /// <summary>Adds randomness to delays so instances do not retry in lockstep. Default <c>true</c>.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>Upper bound for any single delay. Default 30 s.</summary>
    public TimeSpan? MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    internal RetrySettings Clone() => (RetrySettings)MemberwiseClone();
}

/// <summary>
/// Circuit breaker settings. On by default: it only acts once a request type is already
/// failing, so it cannot throttle a healthy system.
/// </summary>
public sealed class CircuitBreakerSettings
{
    /// <summary>Whether the circuit breaker is active. Default <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Failure ratio (0–1] within <see cref="SamplingDuration"/> that opens the circuit. Default 0.5.</summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>Minimum requests within <see cref="SamplingDuration"/> before the ratio is evaluated (at least 2). Default 10.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>Window over which failures are counted (at least 500 ms). Default 30 s.</summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before letting a trial request through (at least 500 ms). Default 30 s.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    internal CircuitBreakerSettings Clone() => (CircuitBreakerSettings)MemberwiseClone();
}

/// <summary>Fixed-window rate limiter settings. Off by default: it imposes a hard throughput ceiling.</summary>
public sealed class RateLimiterSettings
{
    /// <summary>Whether the rate limiter is active. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Requests allowed per <see cref="Window"/>. Default 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Window length. Default 1 s.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Requests that may wait for the next window instead of being rejected. Default 0.</summary>
    public int QueueLimit { get; set; }

    internal RateLimiterSettings Clone() => (RateLimiterSettings)MemberwiseClone();
}

/// <summary>Bulkhead (concurrency limiter) settings. Off by default: it imposes a hard concurrency ceiling.</summary>
public sealed class BulkheadSettings
{
    /// <summary>Whether the bulkhead is active. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Executions allowed at the same time. Default 10.</summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>Executions that may wait for a free slot instead of being rejected. Default 0.</summary>
    public int QueueLimit { get; set; }

    internal BulkheadSettings Clone() => (BulkheadSettings)MemberwiseClone();
}

/// <summary>The four strategies' settings, either as defaults or for one request type.</summary>
public class ResilienceSettings
{
    /// <summary>Retry settings.</summary>
    public RetrySettings Retry { get; private init; } = new();

    /// <summary>Circuit breaker settings.</summary>
    public CircuitBreakerSettings CircuitBreaker { get; private init; } = new();

    /// <summary>Rate limiter settings.</summary>
    public RateLimiterSettings RateLimiter { get; private init; } = new();

    /// <summary>Bulkhead settings.</summary>
    public BulkheadSettings Bulkhead { get; private init; } = new();

    internal ResilienceSettings Clone() => new()
    {
        Retry = Retry.Clone(),
        CircuitBreaker = CircuitBreaker.Clone(),
        RateLimiter = RateLimiter.Clone(),
        Bulkhead = Bulkhead.Clone(),
    };
}

/// <summary>
/// Options for <c>cfg.AddResilience(...)</c>. The properties inherited from
/// <see cref="ResilienceSettings"/> are the defaults for every request type;
/// <see cref="ForRequest{TRequest}"/> adjusts them for one type. Each request type gets its
/// own circuit, rate-limit budget and bulkhead.
/// </summary>
public sealed class CourierResilienceOptions : ResilienceSettings
{
    private readonly Dictionary<Type, Action<ResilienceSettings>> _overrides = [];

    /// <summary>
    /// Decides which exceptions count as failures for retry and the circuit breaker. By default:
    /// everything except cancellation and <see cref="CourierException"/>s with a status below 500
    /// (validation errors, not found, conflicts), which would fail the same way again.
    /// </summary>
    public Func<Exception, bool> ShouldHandle { get; set; } = IsTransient;

    /// <summary>
    /// Adjusts the settings of <typeparamref name="TRequest"/>, starting from a copy of the defaults.
    /// Calling it again for the same type adds to the previous adjustments.
    /// </summary>
    public CourierResilienceOptions ForRequest<TRequest>(Action<ResilienceSettings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _overrides[typeof(TRequest)] = _overrides.TryGetValue(typeof(TRequest), out var existing)
            ? existing + configure
            : configure;
        return this;
    }

    /// <summary>The default transient-failure predicate used by <see cref="ShouldHandle"/>.</summary>
    public static bool IsTransient(Exception exception) => exception switch
    {
        OperationCanceledException => false,
        CourierException courier => courier.StatusCode >= 500,
        _ => true,
    };

    internal IEnumerable<ResilienceSettings> AllSettings() =>
        _overrides.Keys.Select(Resolve).Prepend(this);

    internal ResilienceSettings Resolve(Type requestType)
    {
        var settings = Clone();
        if (_overrides.TryGetValue(requestType, out var configure))
        {
            configure(settings);
        }

        return settings;
    }
}
