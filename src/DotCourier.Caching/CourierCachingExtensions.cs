using DotCourier.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCourier;

/// <summary>Adds caching, idempotency and cache invalidation to a DotCourier pipeline.</summary>
public static class CourierCachingExtensions
{
    /// <summary>
    /// Enables <see cref="CacheableAttribute"/>, <see cref="IdempotentAttribute"/> and
    /// <see cref="InvalidatesCacheAttribute"/>, and registers <see cref="ICourierCache"/>.
    /// Storage is Microsoft <c>HybridCache</c> (registered with defaults unless you call
    /// <c>AddHybridCache</c> yourself): in-memory per instance, plus any registered
    /// <c>IDistributedCache</c> (e.g. <c>AddStackExchangeRedisCache</c>) as a shared second level.
    /// Cached results must be serializable with System.Text.Json.
    /// </summary>
    public static CourierConfiguration AddCaching(this CourierConfiguration configuration, Action<CourierCachingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CourierCachingOptions();
        configure?.Invoke(options);
        options.Validate();

        return configuration
            .ConfigureServices(services =>
            {
                services.TryAddSingleton(options);
                services.TryAddSingleton<CacheKeys>();
                services.TryAddSingleton<ICourierCache, CourierCache>();
                var hybrid = services.AddHybridCache();
                if (options.ResultSerializerOptions is { } serializer)
                {
                    hybrid.AddSerializerFactory(new CourierHybridSerializerFactory(serializer));
                }
            })
            .AddOpenBehavior(typeof(IdempotencyBehavior<,>), BehaviorOrder.Idempotency)
            .AddOpenBehavior(typeof(CacheInvalidationBehavior<,>), BehaviorOrder.CacheInvalidation)
            .AddOpenBehavior(typeof(CachingBehavior<,>), BehaviorOrder.Caching)
            .AddBehaviorTypes(CachingBehaviorTypes.Instance);
    }
}
