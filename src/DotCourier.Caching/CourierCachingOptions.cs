using System.Text.Json;

namespace DotCourier.Caching;

/// <summary>Options for <c>cfg.AddCaching(...)</c>.</summary>
public sealed class CourierCachingOptions
{
    /// <summary>Duration for <see cref="CacheableAttribute"/> requests that do not set one. Default 5 minutes.</summary>
    public TimeSpan DefaultCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Duration for <see cref="IdempotentAttribute"/> requests that do not set one. Default 1 hour.</summary>
    public TimeSpan DefaultIdempotencyDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Upper bound for how long an instance keeps an entry in its own memory. With several instances
    /// sharing a distributed cache, an invalidation clears the shared level, but other instances'
    /// in-memory copies live until this expires, so it bounds how stale they can be.
    /// Default <c>null</c>: same as the entry's duration.
    /// </summary>
    public TimeSpan? LocalCacheDuration { get; set; }

    /// <summary>
    /// Cache every <see cref="IQuery{TResponse}"/>, not only those marked <see cref="CacheableAttribute"/>
    /// (Spring Courier's behavior). Default <c>false</c>: caching everything silently serves stale data.
    /// </summary>
    public bool CacheAllQueries { get; set; }

    /// <summary>Prefix of every key and tag, to share one cache server between applications. Default <c>"dotcourier"</c>.</summary>
    public string KeyPrefix { get; set; } = "dotcourier";

    /// <summary>
    /// With an <see cref="IIdempotencyLock"/>: how long the lock is held at most. It must outlast the slowest
    /// idempotent handler, or a duplicate on another instance could start while the first still runs.
    /// Default 1 minute.
    /// </summary>
    public TimeSpan IdempotencyLockDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// With an <see cref="IIdempotencyLock"/>: how long a duplicate waits for the instance running the request
    /// before giving up with <see cref="IdempotencyInProgressException"/> (409). Default 10 seconds.
    /// </summary>
    public TimeSpan IdempotencyLockWait { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Serializer options for the stored results (and, unless <see cref="KeySerializerOptions"/> is set, for
    /// the requests turned into keys). Default <c>null</c>: HybridCache's own serializer, which uses
    /// reflection. <b>Required under Native AOT</b>: options whose <c>TypeInfoResolver</c> is a source-generated
    /// context listing your cached results and requests.
    /// </summary>
    public JsonSerializerOptions? ResultSerializerOptions { get; set; }

    /// <summary>Serializer options used to turn a request into its key (not to store results). Default: the defaults of <see cref="JsonSerializer"/>.</summary>
    public JsonSerializerOptions? KeySerializerOptions { get; set; }

    internal void Validate()
    {
        if (DefaultCacheDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultCacheDuration), "Must be positive.");
        }

        if (DefaultIdempotencyDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultIdempotencyDuration), "Must be positive.");
        }

        if (LocalCacheDuration is { } local && local <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(LocalCacheDuration), "Must be positive.");
        }

        if (IdempotencyLockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(IdempotencyLockDuration), "Must be positive.");
        }

        if (IdempotencyLockWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(IdempotencyLockWait), "Must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(KeyPrefix))
        {
            throw new ArgumentException("Must not be empty.", nameof(KeyPrefix));
        }
    }
}
