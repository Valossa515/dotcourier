using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using DotCourier.Behaviors;

namespace DotCourier.Caching;

/// <summary>
/// Builds keys and tags. A key is <c>{prefix}:{cache|idem}:{Namespace.RequestName}:{discriminator}</c>,
/// where the discriminator is the explicit key (<see cref="ICacheKey"/>/<see cref="IIdempotencyKey"/>)
/// or the SHA-256 of the request serialized as JSON. Serializing (rather than using
/// <c>ToString()</c>) keeps requests with collections apart: a record's <c>ToString()</c> prints a
/// list's type, not its items, so two different requests would collide.
/// </summary>
internal sealed class CacheKeys(CourierCachingOptions options)
{
    private static readonly ConcurrentDictionary<Type, string> TypeIds = new();

    public string CacheTag => options.KeyPrefix + ":cache";

    public string IdempotencyTag => options.KeyPrefix + ":idem";

    /// <summary>Tag for cache entries of <paramref name="requestType"/>; an open generic definition matches every closed type.</summary>
    public string CacheTypeTag(Type requestType) => $"{options.KeyPrefix}:cache-type:{TypeId(requestType)}";

    public string[] CacheTags(Type requestType)
    {
        return requestType.IsConstructedGenericType
            ? [CacheTag, CacheTypeTag(requestType), CacheTypeTag(requestType.GetGenericTypeDefinition())]
            : [CacheTag, CacheTypeTag(requestType)];
    }

    public string[] IdempotencyTags => [IdempotencyTag];

    /// <summary>The cache key, or <c>null</c> when the request cannot be serialized into one.</summary>
    public string? ForCache(object request) => Build("cache", request, (request as ICacheKey)?.CacheKey);

    /// <summary>The idempotency key, or <c>null</c> when the request cannot be serialized into one.</summary>
    public string? ForIdempotency(object request) => Build("idem", request, (request as IIdempotencyKey)?.IdempotencyKey);

    private string? Build(string kind, object request, string? explicitKey)
    {
        var type = request.GetType();
        var discriminator = explicitKey;
        if (discriminator is null)
        {
            try
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(request, type, options.KeySerializerOptions ?? JsonSerializerOptions.Default);
                discriminator = Convert.ToHexString(SHA256.HashData(json));
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        return $"{options.KeyPrefix}:{kind}:{TypeId(type)}:{discriminator}";
    }

    private static string TypeId(Type type) =>
        TypeIds.GetOrAdd(type, static t => t.Namespace is { Length: > 0 } ns
            ? ns + "." + CourierDiagnostics.GetRequestName(t)
            : CourierDiagnostics.GetRequestName(t));
}
