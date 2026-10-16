using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DotCourier.Outbox;

/// <summary>
/// JSON through the options' type metadata when they have a resolver (a source-generated context under
/// Native AOT); options without one keep the reflection-based serializer, as before. Reflection is disabled
/// under Native AOT, so there such options fail with System.Text.Json's own explanation.
/// </summary>
internal static class Json
{
    private const string Justification = "Only for options without a TypeInfoResolver while reflection-based serialization is enabled, which it never is under Native AOT.";

    public static byte[] SerializeToUtf8Bytes(object value, Type type, JsonSerializerOptions options) =>
        UsesReflection(options) ? ReflectionBytes(value, type, options) : JsonSerializer.SerializeToUtf8Bytes(value, options.GetTypeInfo(type));

    public static string Serialize(object value, Type type, JsonSerializerOptions options) =>
        UsesReflection(options) ? ReflectionString(value, type, options) : JsonSerializer.Serialize(value, options.GetTypeInfo(type));

    public static object? Deserialize(string json, Type type, JsonSerializerOptions options) =>
        UsesReflection(options) ? ReflectionObject(json, type, options) : JsonSerializer.Deserialize(json, options.GetTypeInfo(type));

    private static bool UsesReflection(JsonSerializerOptions options) =>
        options.TypeInfoResolver is null && JsonSerializer.IsReflectionEnabledByDefault;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    private static byte[] ReflectionBytes(object value, Type type, JsonSerializerOptions options) => JsonSerializer.SerializeToUtf8Bytes(value, type, options);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    private static string ReflectionString(object value, Type type, JsonSerializerOptions options) => JsonSerializer.Serialize(value, type, options);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    private static object? ReflectionObject(string json, Type type, JsonSerializerOptions options) => JsonSerializer.Deserialize(json, type, options);
}
