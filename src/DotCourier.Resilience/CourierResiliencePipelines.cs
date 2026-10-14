using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using DotCourier.Behaviors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace DotCourier.Resilience;

internal enum ResilienceStrategy
{
    RateLimiter,
    CircuitBreaker,
    Retry,
    Bulkhead,
}

/// <summary>
/// Builds and caches one Polly pipeline per (request type, strategy). Singleton, so circuits,
/// rate-limit windows and bulkhead slots are shared by every request of a type.
/// </summary>
internal sealed partial class CourierResiliencePipelines
{
    private static readonly Meter Meter = new("DotCourier");
    private static readonly Counter<long> Rejections =
        Meter.CreateCounter<long>("courier.resilience.rejected", "{request}", "Requests rejected by a resilience strategy");
    private static readonly Counter<long> RetryAttempts =
        Meter.CreateCounter<long>("courier.retry.attempts", "{attempt}", "Retries performed by DotCourier");

    private readonly ConcurrentDictionary<(Type, ResilienceStrategy), Lazy<ResiliencePipeline?>> _pipelines = new();
    private readonly CourierResilienceOptions _options;
    private readonly ILogger _logger;

    public CourierResiliencePipelines(CourierResilienceOptions options, ILogger<CourierResiliencePipelines>? logger = null)
    {
        _options = options;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>The pipeline for <paramref name="strategy"/>, or <c>null</c> when it is disabled for the request type.</summary>
    public ResiliencePipeline? Get(Type requestType, ResilienceStrategy strategy) =>
        _pipelines.GetOrAdd((requestType, strategy), key =>
            new Lazy<ResiliencePipeline?>(() => Build(key.Item1, key.Item2, _options.Resolve(key.Item1)))).Value;

    public void RecordRejection(Type requestType, ResilienceStrategy strategy)
    {
        var requestName = CourierDiagnostics.GetRequestName(requestType);
        LogRejected(strategy, requestName);
        Rejections.Add(1, new TagList
        {
            { "courier.request.type", requestName },
            { "courier.resilience.strategy", strategy.ToString() },
        });
    }

    /// <summary>Builds every configured pipeline once so invalid settings fail at startup, not on the first request.</summary>
    internal static void Validate(CourierResilienceOptions options)
    {
        var pipelines = new CourierResiliencePipelines(options);
        foreach (var settings in options.AllSettings())
        {
            foreach (var strategy in Enum.GetValues<ResilienceStrategy>())
            {
                pipelines.Build(typeof(CourierResilienceOptions), strategy, settings);
            }
        }
    }

    private ResiliencePipeline? Build(Type requestType, ResilienceStrategy strategy, ResilienceSettings settings)
    {
        var requestName = CourierDiagnostics.GetRequestName(requestType);
        var builder = new ResiliencePipelineBuilder { Name = $"{requestName}.{strategy}" };
        switch (strategy)
        {
            case ResilienceStrategy.Retry when settings.Retry is { Enabled: true, MaxAttempts: > 1 } retry:
                builder.AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = retry.MaxAttempts - 1,
                    Delay = retry.Delay,
                    BackoffType = retry.BackoffType,
                    UseJitter = retry.UseJitter,
                    MaxDelay = retry.MaxDelay,
                    ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is { } ex && _options.ShouldHandle(ex)),
                    OnRetry = args =>
                    {
                        // AttemptNumber is the zero-based index of the retry, so the attempt about to
                        // run (counting the first call) is AttemptNumber + 2.
                        LogRetrying(requestName, args.Outcome.Exception?.GetType().Name, args.AttemptNumber + 2, retry.MaxAttempts,
                            args.RetryDelay.TotalMilliseconds);
                        RetryAttempts.Add(1, new KeyValuePair<string, object?>("courier.request.type", requestName));
                        return default;
                    },
                });
                break;

            case ResilienceStrategy.CircuitBreaker when settings.CircuitBreaker is { Enabled: true } breaker:
                builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = breaker.FailureRatio,
                    MinimumThroughput = breaker.MinimumThroughput,
                    SamplingDuration = breaker.SamplingDuration,
                    BreakDuration = breaker.BreakDuration,
                    ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is { } ex && _options.ShouldHandle(ex)),
                    OnOpened = args =>
                    {
                        LogCircuitOpened(requestName, args.BreakDuration.TotalSeconds);
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        LogCircuitClosed(requestName);
                        return default;
                    },
                });
                break;

            case ResilienceStrategy.RateLimiter when settings.RateLimiter is { Enabled: true } limiter:
                builder.AddRateLimiter(new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limiter.PermitLimit,
                    Window = limiter.Window,
                    QueueLimit = limiter.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));
                break;

            case ResilienceStrategy.Bulkhead when settings.Bulkhead is { Enabled: true } bulkhead:
                builder.AddConcurrencyLimiter(bulkhead.MaxConcurrency, bulkhead.QueueLimit);
                break;

            default:
                return null;
        }

        return builder.Build();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] {RequestType} failed with {ExceptionType}; attempt {NextAttempt}/{MaxAttempts} in {DelayMs:0}ms")]
    private partial void LogRetrying(string requestType, string? exceptionType, int nextAttempt, int maxAttempts, double delayMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] Circuit opened for {RequestType} for {BreakSeconds:0}s")]
    private partial void LogCircuitOpened(string requestType, double breakSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] Circuit closed for {RequestType}")]
    private partial void LogCircuitClosed(string requestType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] {Strategy} rejected {RequestType} without invoking the handler")]
    private partial void LogRejected(ResilienceStrategy strategy, string requestType);
}
