using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Hybrid;

namespace DotCourier.Caching;

/// <summary>
/// Runs the pipeline through <see cref="HybridCache.GetOrCreateAsync{TState, T}(string, TState, Func{TState, CancellationToken, ValueTask{T}}, HybridCacheEntryOptions?, IEnumerable{string}?, CancellationToken)"/>.
/// Concurrent calls with the same key share one execution (in-process), and only successful,
/// non-null results are stored: anything else is carried out of the factory in a
/// <see cref="SkipCacheException"/> so every waiter gets that same result without it being cached.
/// </summary>
internal static class CachedExecution
{
    private static readonly Meter Meter = new("DotCourier");

    internal static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("courier.cache.hits", "{request}", "Cached results served by DotCourier");
    internal static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("courier.cache.misses", "{request}", "Cacheable requests that ran the handler");
    internal static readonly Counter<long> IdempotencyHits = Meter.CreateCounter<long>("courier.idempotency.hits", "{request}", "Duplicate idempotent requests answered from the recorded result");
    internal static readonly Counter<long> IdempotencyMisses = Meter.CreateCounter<long>("courier.idempotency.misses", "{request}", "Idempotent requests that ran the handler");

    public static async Task<(TResponse Response, bool Executed)> GetOrRunAsync<TResponse>(
        HybridCache cache,
        string key,
        TimeSpan duration,
        TimeSpan? localDuration,
        string[] tags,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // HybridCache starts the factory on a separate work item WITHOUT the caller's ExecutionContext
        // whenever the caller's token can be cancelled, which is always true for an HTTP request
        // (HttpContext.RequestAborted). The rest of the pipeline and the handler would then lose
        // everything that lives in AsyncLocal: the request culture, IHttpContextAccessor (user,
        // tenant), Activity.Current (traces). Capture it here and run the pipeline inside it.
        var state = (Next: next, Executed: new StrongBox<bool>(), Context: ExecutionContext.Capture());
        var local = localDuration is { } max && max < duration ? max : duration;
        var entryOptions = new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = local };
        try
        {
            var response = await cache.GetOrCreateAsync(
                key,
                state,
                static async (s, token) =>
                {
                    s.Executed.Value = true;
                    var result = await RunInContext(s.Context, s.Next, token).ConfigureAwait(false);
                    if (result is null || result is IResponseStatus { IsSuccess: false })
                    {
                        throw new SkipCacheException(result);
                    }

                    return result;
                },
                entryOptions,
                tags,
                cancellationToken).ConfigureAwait(false);

            return (response, state.Executed.Value);
        }
        catch (SkipCacheException skip)
        {
            return ((TResponse)skip.Result!, state.Executed.Value);
        }
    }

    /// <summary>
    /// Starts <paramref name="next"/> inside <paramref name="context"/>. Its awaits capture that
    /// context, so the whole remaining pipeline and the handler run with the caller's ambient state.
    /// </summary>
    private static Task<TResponse> RunInContext<TResponse>(ExecutionContext? context, RequestHandlerDelegate<TResponse> next, CancellationToken token)
    {
        if (context is null)
        {
            return next(token);
        }

        Task<TResponse>? started = null;
        ExecutionContext.Run(context, _ => started = next(token), null);
        return started!;
    }

    /// <summary>Carries a result that must not be cached out of the cache factory.</summary>
    internal sealed class SkipCacheException(object? result) : Exception("Result not cached")
    {
        public object? Result { get; } = result;
    }
}
