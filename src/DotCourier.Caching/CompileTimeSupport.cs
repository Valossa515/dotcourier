using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotCourier.Generated;
using Microsoft.Extensions.Caching.Hybrid;

namespace DotCourier.Caching;

/// <summary>
/// HybridCache serializers from <see cref="CourierCachingOptions.ResultSerializerOptions"/>: they ask the options
/// for each type's metadata (a source-generated context under Native AOT) instead of reflecting over it. Types
/// the options do not know are left to the next factory, so the rest of the application's cache is unaffected.
/// </summary>
internal sealed class CourierHybridSerializerFactory(JsonSerializerOptions options) : IHybridCacheSerializerFactory
{
    public bool TryCreateSerializer<T>([NotNullWhen(true)] out IHybridCacheSerializer<T>? serializer)
    {
        // Options without a resolver add nothing over HybridCache's own (reflection-based) serializer.
        if (options.TypeInfoResolver is not null && options.TryGetTypeInfo(typeof(T), out var info) && info is JsonTypeInfo<T> typed)
        {
            serializer = new Serializer<T>(typed);
            return true;
        }

        serializer = null;
        return false;
    }

    private sealed class Serializer<T>(JsonTypeInfo<T> typeInfo) : IHybridCacheSerializer<T>
    {
        public T Deserialize(ReadOnlySequence<byte> source)
        {
            var reader = new Utf8JsonReader(source);
            return JsonSerializer.Deserialize(ref reader, typeInfo)!;
        }

        public void Serialize(T value, IBufferWriter<byte> target)
        {
            using var writer = new Utf8JsonWriter(target);
            JsonSerializer.Serialize(writer, value, typeInfo);
        }
    }
}

/// <summary>Closes the caching behaviors for compile-time mode (Native AOT).</summary>
internal sealed class CachingBehaviorTypes : BehaviorTypes
{
    public static readonly CachingBehaviorTypes Instance = new();

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type? CloseRequestBehavior<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>(Type openBehavior)
    {
        if (openBehavior == typeof(IdempotencyBehavior<,>))
        {
            return typeof(IdempotencyBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(CacheInvalidationBehavior<,>))
        {
            return typeof(CacheInvalidationBehavior<TRequest, TResponse>);
        }

        return openBehavior == typeof(CachingBehavior<,>) ? typeof(CachingBehavior<TRequest, TResponse>) : null;
    }
}
