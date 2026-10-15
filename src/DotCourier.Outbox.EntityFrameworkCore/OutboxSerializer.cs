using System.Collections.Concurrent;
using System.Text.Json;

namespace DotCourier.Outbox;

/// <summary>Turns notifications into (type name, JSON) and back.</summary>
internal sealed class OutboxSerializer(CourierOutboxOptions options)
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();
    private static readonly ConcurrentDictionary<string, Type?> Types = new();

    public string Serialize(object notification) =>
        JsonSerializer.Serialize(notification, notification.GetType(), options.JsonSerializerOptions);

    public static string TypeName(Type type) => Names.GetOrAdd(type, Build);

    /// <summary>Restores a stored notification. Only <see cref="INotification"/> types are accepted.</summary>
    public object Deserialize(OutboxMessage message)
    {
        var type = Types.GetOrAdd(message.EventType, name => Type.GetType(name, throwOnError: false));
        if (type is null)
        {
            throw new OutboxException($"Outbox message {message.Id}: type '{message.EventType}' was not found.");
        }

        // The type name comes from the database: never instantiate anything that is not a notification.
        if (!typeof(INotification).IsAssignableFrom(type))
        {
            throw new OutboxException($"Outbox message {message.Id}: '{message.EventType}' is not an {nameof(INotification)}.");
        }

        return JsonSerializer.Deserialize(message.Payload, type, options.JsonSerializerOptions)
            ?? throw new OutboxException($"Outbox message {message.Id}: the payload is empty.");
    }

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
