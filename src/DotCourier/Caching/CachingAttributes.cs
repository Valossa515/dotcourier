namespace DotCourier;

/// <summary>
/// Caches the successful result of the annotated request (typically a query) when the
/// <c>DotCourier.Caching</c> package is enabled with <c>cfg.AddCaching()</c>. The key is built
/// from the request's content, so equal requests share an entry. Error responses, exceptions
/// and <c>null</c> results are never cached.
/// </summary>
/// <remarks>Lives in the core package so request contracts can be annotated without a caching dependency.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class CacheableAttribute : Attribute
{
    /// <summary>Caches for the configured default duration.</summary>
    public CacheableAttribute()
    {
    }

    /// <summary>Caches for <paramref name="seconds"/> seconds.</summary>
    public CacheableAttribute(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        Seconds = seconds;
    }

    /// <summary>Cache duration in seconds, or <c>null</c> for the configured default.</summary>
    public int? Seconds { get; }
}

/// <summary>
/// Makes the annotated request (typically a command) idempotent when <c>DotCourier.Caching</c> is
/// enabled: the first successful result is recorded, and repeated or concurrent equal requests get
/// that result without running the handler again. Failures are not recorded, so a retry runs again.
/// The key is the request's content, or <see cref="IIdempotencyKey.IdempotencyKey"/> when implemented.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>Records results for the configured default duration (1 hour unless changed).</summary>
    public IdempotentAttribute()
    {
    }

    /// <summary>Records results for <paramref name="seconds"/> seconds.</summary>
    public IdempotentAttribute(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        Seconds = seconds;
    }

    /// <summary>How long a result is remembered, in seconds, or <c>null</c> for the configured default.</summary>
    public int? Seconds { get; }
}

/// <summary>
/// When the annotated request (typically a command) succeeds, every cached result of the listed
/// request types is evicted, e.g. <c>[InvalidatesCache(typeof(GetProduct), typeof(ListProducts))]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class InvalidatesCacheAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="requestTypes">The cached request types to evict.</param>
    public InvalidatesCacheAttribute(params Type[] requestTypes)
    {
        ArgumentNullException.ThrowIfNull(requestTypes);
        RequestTypes = requestTypes;
    }

    /// <summary>The cached request types to evict.</summary>
    public IReadOnlyList<Type> RequestTypes { get; }
}

/// <summary>Lets a <see cref="CacheableAttribute"/> request choose its own cache key instead of its serialized content.</summary>
public interface ICacheKey
{
    /// <summary>The key, unique per distinct result (the request type is added automatically).</summary>
    string CacheKey { get; }
}

/// <summary>
/// Lets an <see cref="IdempotentAttribute"/> request supply its idempotency key, e.g. the value of
/// an HTTP <c>Idempotency-Key</c> header, instead of its serialized content.
/// </summary>
public interface IIdempotencyKey
{
    /// <summary>The idempotency key (the request type is added automatically).</summary>
    string IdempotencyKey { get; }
}
