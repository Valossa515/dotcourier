using DotCourier.Resilience;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCourier;

/// <summary>Adds the resilience behaviors to a DotCourier pipeline.</summary>
public static class CourierResilienceExtensions
{
    /// <summary>
    /// Adds retry, circuit breaker, rate limiter and bulkhead behaviors at their
    /// <see cref="BehaviorOrder"/> positions. Only the circuit breaker is on by default.
    /// Settings are validated here, so a bad value fails at startup.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddCourier(cfg => cfg
    ///     .RegisterServicesFromAssemblyContaining&lt;Program&gt;()
    ///     .AddResilience(o =>
    ///     {
    ///         o.Retry.Enabled = true;
    ///         o.ForRequest&lt;GenerateReport&gt;(r => { r.Bulkhead.Enabled = true; r.Bulkhead.MaxConcurrency = 2; });
    ///     }));
    /// </code>
    /// </example>
    public static CourierConfiguration AddResilience(this CourierConfiguration configuration, Action<CourierResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CourierResilienceOptions();
        configure?.Invoke(options);
        CourierResiliencePipelines.Validate(options);

        return configuration
            .ConfigureServices(services =>
            {
                services.TryAddSingleton(options);
                services.TryAddSingleton<CourierResiliencePipelines>();
            })
            .AddOpenBehavior(typeof(RateLimiterBehavior<,>), BehaviorOrder.RateLimiter)
            .AddOpenBehavior(typeof(CircuitBreakerBehavior<,>), BehaviorOrder.CircuitBreaker)
            .AddOpenBehavior(typeof(RetryBehavior<,>), BehaviorOrder.Retry)
            .AddOpenBehavior(typeof(BulkheadBehavior<,>), BehaviorOrder.Bulkhead);
    }
}
