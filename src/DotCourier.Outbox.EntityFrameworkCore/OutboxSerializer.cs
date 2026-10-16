using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DotCourier.Generated;

namespace DotCourier.Outbox;

/// <summary>Turns notifications into (type name, JSON) and back.</summary>
internal sealed class OutboxSerializer(CourierOutboxOptions options)
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();
    private static readonly ConcurrentDictionary<string, Type?> Types = new();

    /// <remarks>
    /// Through the options' type metadata, never reflection-only overloads: under Native AOT the options'
    /// <c>TypeInfoResolver</c> (a source-generated context) must know the notification types.
    /// </remarks>
    public string Serialize(object notification) =>
        Json.Serialize(notification, notification.GetType(), options.JsonSerializerOptions);

    public static string TypeName(Type type) => Names.GetOrAdd(type, Build);

    /// <summary>Restores a stored notification. Only <see cref="INotification"/> types are accepted.</summary>
    public object Deserialize(OutboxMessage message)
    {
        var type = Types.GetOrAdd(message.EventType, Resolve);
        if (type is null)
        {
            throw new OutboxException($"Outbox message {message.Id}: type '{message.EventType}' was not found.");
        }

        // The type name comes from the database: never instantiate anything that is not a notification.
        if (!typeof(INotification).IsAssignableFrom(type))
        {
            throw new OutboxException($"Outbox message {message.Id}: '{message.EventType}' is not an {nameof(INotification)}.");
        }

        return Json.Deserialize(message.Payload, type, options.JsonSerializerOptions)
            ?? throw new OutboxException($"Outbox message {message.Id}: the payload is empty.");
    }

    /// <summary>Notification types registered at compile time first; any other name by reflection.</summary>
    private static Type? Resolve(string name)
    {
        foreach (var type in GeneratedCourierRegistry.NotificationTypes)
        {
            if (TypeName(type) == name)
            {
                return type;
            }
        }

        return FromName(name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "Types registered at compile time are found first; any other name may have been trimmed away, and then the message fails as an unknown type.")]
    private static Type? FromName(string name) => Type.GetType(name, throwOnError: false);

    /// <summary>
    /// <c>Namespace.Type, Assembly</c>, recursively for generic arguments: like
    /// <see cref="Type.AssemblyQualifiedName"/> but without version, culture and key, so messages
    /// written before an upgrade still resolve.
    /// </summary>
    private static string Build(Type type)
    {
        if (!type.IsGenericType)
        {
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        var definition = type.GetGenericTypeDefinition();
        var arguments = string.Join(",", type.GetGenericArguments().Select(a => $"[{Build(a)}]"));
        return $"{definition.FullName}[{arguments}], {definition.Assembly.GetName().Name}";
    }
}

/// <summary>An outbox message could not be stored or restored.</summary>
public sealed class OutboxException(string message) : Exception(message);
