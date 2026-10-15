using System.Collections.Concurrent;
using System.Reflection;
using DotCourier.Behaviors;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier.Caching;

/// <summary>Serves <see cref="CacheableAttribute"/> requests (or every query with <c>CacheAllQueries</c>) from the cache.</summary>
internal sealed partial class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly CacheableAttribute? Cacheable = typeof(TRequest).GetCustomAttribute<CacheableAttribute>(inherit: true);
    private static readonly bool IsQuery = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
    private static readonly string RequestName = CourierDiagnostics.GetRequestName(typeof(TRequest));

    private readonly HybridCache _cache;
    private readonly CacheKeys _keys;
    private readonly CourierCachingOptions _options;
    private readonly ILogger _logger;

    public CachingBehavior(HybridCache cache, CacheKeys keys, CourierCachingOptions options, ILogger<CachingBehavior<TRequest, TResponse>>? logger = null)
    {
        _cache = cache;
        _keys = keys;
        _options = options;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (Cacheable is null && !(_options.CacheAllQueries && IsQuery))
        {
            return next(cancellationToken);
        }

        var key = _keys.ForCache(request);
        if (key is null)
        {
            UnsupportedKeys.WarnOnce(_logger, typeof(TRequest), RequestName, "caching");
            return next(cancellationToken);
        }

        var duration = Cacheable?.Seconds is { } seconds ? TimeSpan.FromSeconds(seconds) : _options.DefaultCacheDuration;
        return Run(key, duration, next, cancellationToken);
    }

    private async Task<TResponse> Run(string key, TimeSpan duration, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var (response, executed) = await CachedExecution
            .GetOrRunAsync(_cache, key, duration, _options.LocalCacheDuration, _keys.CacheTags(typeof(TRequest)), next, cancellationToken)
            .ConfigureAwait(false);

        var tag = new KeyValuePair<string, object?>("courier.request.type", RequestName);
        if (executed)
        {
            CachedExecution.CacheMisses.Add(1, tag);
        }
        else
        {
            CachedExecution.CacheHits.Add(1, tag);
            LogHit(RequestName);
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Courier] Cache hit for {RequestType}")]
    private partial void LogHit(string requestType);
}

/// <summary>
/// Makes <see cref="IdempotentAttribute"/> requests run once: the first successful result is recorded
/// and returned to repeated or concurrent equal requests.
/// </summary>
internal sealed partial class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly IdempotentAttribute? Idempotent = typeof(TRequest).GetCustomAttribute<IdempotentAttribute>(inherit: true);
    private static readonly string RequestName = CourierDiagnostics.GetRequestName(typeof(TRequest));

    private readonly HybridCache _cache;
    private readonly CacheKeys _keys;
    private readonly CourierCachingOptions _options;
    private readonly ILogger _logger;

    public IdempotencyBehavior(HybridCache cache, CacheKeys keys, CourierCachingOptions options, ILogger<IdempotencyBehavior<TRequest, TResponse>>? logger = null)
    {
        _cache = cache;
        _keys = keys;
        _options = options;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (Idempotent is null)
        {
            return next(cancellationToken);
        }

        var key = _keys.ForIdempotency(request);
        if (key is null)
        {
            UnsupportedKeys.WarnOnce(_logger, typeof(TRequest), RequestName, "idempotency");
            return next(cancellationToken);
        }

        var duration = Idempotent.Seconds is { } seconds ? TimeSpan.FromSeconds(seconds) : _options.DefaultIdempotencyDuration;
        return Run(key, duration, next, cancellationToken);
    }

    private async Task<TResponse> Run(string key, TimeSpan duration, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var (response, executed) = await CachedExecution
            .GetOrRunAsync(_cache, key, duration, _options.LocalCacheDuration, _keys.IdempotencyTags, next, cancellationToken, awaitSharedWrite: true)
            .ConfigureAwait(false);

        var tag = new KeyValuePair<string, object?>("courier.request.type", RequestName);
        if (executed)
        {
            CachedExecution.IdempotencyMisses.Add(1, tag);
        }
        else
        {
            CachedExecution.IdempotencyHits.Add(1, tag);
            LogDuplicate(RequestName);
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] Duplicate {RequestType} answered with the recorded result")]
    private partial void LogDuplicate(string requestType);
}

/// <summary>Evicts the caches listed in <see cref="InvalidatesCacheAttribute"/> once the request succeeded.</summary>
internal sealed class CacheInvalidationBehavior<TRequest, TResponse>(HybridCache cache, CacheKeys keys) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly Type[] Targets = typeof(TRequest)
        .GetCustomAttributes<InvalidatesCacheAttribute>(inherit: true)
        .SelectMany(a => a.RequestTypes)
        .Distinct()
        .ToArray();

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
        Targets.Length == 0 ? next(cancellationToken) : HandleAndInvalidate(next, cancellationToken);

    private async Task<TResponse> HandleAndInvalidate(RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken).ConfigureAwait(false);
        if (response is IResponseStatus { IsSuccess: false })
        {
            return response;
        }

        foreach (var target in Targets)
        {
            await cache.RemoveByTagAsync(keys.CacheTypeTag(target), cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}

internal static partial class UnsupportedKeys
{
    private static readonly ConcurrentDictionary<Type, bool> Warned = new();

    public static void WarnOnce(ILogger logger, Type requestType, string requestName, string feature)
    {
        if (Warned.TryAdd(requestType, true))
        {
            LogUnsupported(logger, requestName, feature);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] {RequestType} cannot be serialized into a key, so {Feature} is skipped for it. Implement ICacheKey / IIdempotencyKey or make it JSON-serializable.")]
    private static partial void LogUnsupported(ILogger logger, string requestType, string feature);
}

internal sealed class CourierCache(HybridCache cache, CacheKeys keys) : ICourierCache
{
    private readonly CacheKeys _keys = keys;

    public ValueTask InvalidateAsync<TRequest>(CancellationToken cancellationToken = default) =>
        InvalidateAsync(typeof(TRequest), cancellationToken);

    public ValueTask InvalidateAsync(Type requestType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return cache.RemoveByTagAsync(_keys.CacheTypeTag(requestType), cancellationToken);
    }

    public ValueTask InvalidateAsync(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _keys.ForCache(request) is { } key ? cache.RemoveAsync(key, cancellationToken) : ValueTask.CompletedTask;
    }

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default) =>
        cache.RemoveByTagAsync(_keys.CacheTag, cancellationToken);

    public ValueTask ForgetIdempotentResultAsync(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _keys.ForIdempotency(request) is { } key ? cache.RemoveAsync(key, cancellationToken) : ValueTask.CompletedTask;
    }
}
