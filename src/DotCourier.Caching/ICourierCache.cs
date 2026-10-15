namespace DotCourier;

/// <summary>
/// Evicts DotCourier cache entries from code (for cases <see cref="InvalidatesCacheAttribute"/> does
/// not cover, e.g. data changed by another system). Registered by <c>cfg.AddCaching()</c>.
/// </summary>
public interface ICourierCache
{
    /// <summary>Evicts every cached result of <typeparamref name="TRequest"/> (an open generic definition evicts all its closed types).</summary>
    ValueTask InvalidateAsync<TRequest>(CancellationToken cancellationToken = default);

    /// <summary>Evicts every cached result of <paramref name="requestType"/> (an open generic definition evicts all its closed types).</summary>
    ValueTask InvalidateAsync(Type requestType, CancellationToken cancellationToken = default);

    /// <summary>Evicts the cached result of this exact request.</summary>
    ValueTask InvalidateAsync(object request, CancellationToken cancellationToken = default);

    /// <summary>Evicts every cached result (idempotency records are kept).</summary>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets the recorded result of an idempotent request, so the next equal request runs again.</summary>
    ValueTask ForgetIdempotentResultAsync(object request, CancellationToken cancellationToken = default);
}
