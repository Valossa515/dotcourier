namespace DotCourier.Caching;

/// <summary>
/// A lock shared by every instance of the application, so an <see cref="IdempotentAttribute"/> request
/// runs once even when duplicates arrive at the same time on different instances. Without one,
/// duplicates are only coalesced within each instance. <c>DotCourier.Caching.Redis</c> provides a Redis
/// implementation (<c>cfg.AddRedisIdempotencyLock(...)</c>).
/// </summary>
public interface IIdempotencyLock
{
    /// <summary>
    /// Takes the lock named <paramref name="key"/> for at most <paramref name="duration"/> (it expires by
    /// itself if the holder crashes). Returns a handle that releases it when disposed, or <c>null</c> when
    /// someone else holds it; the caller retries.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>
/// Another instance is still running the same <see cref="IdempotentAttribute"/> request and did not finish
/// within <see cref="CourierCachingOptions.IdempotencyLockWait"/>. Maps to 409: the client may retry later
/// and will then get the recorded result.
/// </summary>
public sealed class IdempotencyInProgressException : CourierException
{
    /// <summary>Creates the exception.</summary>
    public IdempotencyInProgressException(Type requestType, CourierMessages? messages = null)
        : base((messages ?? CourierMessages.Default).IdempotencyInProgress.Render(requestType), 409)
    {
        RequestType = requestType;
        Code = (messages ?? CourierMessages.Default).IdempotencyInProgress.Code;
    }

    /// <summary>The request type that is still in progress.</summary>
    public Type RequestType { get; }
}
