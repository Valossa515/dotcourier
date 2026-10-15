using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using DotCourier.Outbox;

namespace DotCourier.Kafka;

/// <summary>Options for <c>cfg.AddKafkaDelivery(...)</c>.</summary>
public sealed class CourierKafkaOptions
{
    /// <summary>Kafka brokers, e.g. <c>localhost:9092</c>. Required.</summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>Prepended to the event's type name to form the topic. Default <c>courier.</c>.</summary>
    public string TopicPrefix { get; set; } = "courier.";

    /// <summary>When set, every message goes to this topic and <see cref="TopicPrefix"/> is ignored.</summary>
    public string? Topic { get; set; }

    /// <summary>
    /// How long to wait for the broker's acknowledgement before the attempt counts as failed (and the
    /// message stays pending). It bounds how long one message can hold up an outbox cycle. Default 10 s.
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Adjusts the producer configuration (security, SASL, compression, ...). Applied after the
    /// durable defaults (<c>acks=all</c>, idempotence), so it can override them, which is rarely wise.
    /// </summary>
    public Action<ProducerConfig>? ConfigureProducer { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BootstrapServers))
        {
            throw new ArgumentException("Kafka BootstrapServers is required.", nameof(BootstrapServers));
        }

        if (SendTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(SendTimeout), SendTimeout, "Must be positive.");
        }
    }

    internal ProducerConfig BuildProducerConfig()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = BootstrapServers,
            ClientId = "dotcourier-outbox",

            // The outbox exists so an event is never lost: confirm only once every in-sync replica has it,
            // and let the idempotent producer keep its own retries from writing duplicates.
            Acks = Acks.All,
            EnableIdempotence = true,

            // librdkafka gives up delivering at the same time as we stop waiting.
            MessageTimeoutMs = (int)Math.Min(int.MaxValue, SendTimeout.TotalMilliseconds),
        };
        ConfigureProducer?.Invoke(config);
        return config;
    }
}

/// <summary>Decides the Kafka topic of an outbox message. Register your own to route differently.</summary>
public interface IOutboxTopicResolver
{
    /// <summary>The topic <paramref name="message"/> is published to.</summary>
    string Resolve(OutboxMessage message);
}

/// <summary>
/// Default <see cref="IOutboxTopicResolver"/>: one topic per event type, <c>prefix + type name</c>, e.g.
/// <c>courier.OrderPlaced</c>; generic arguments are appended (<c>courier.DomainEventNotification-OrderPlaced</c>).
/// The namespace is left out so consumers do not have to mirror the producer's code structure.
/// </summary>
internal sealed class EventTypeTopicResolver(CourierKafkaOptions options) : IOutboxTopicResolver
{
    private const int MaxTopicLength = 249;
    private static readonly ConcurrentDictionary<string, string> Names = new();

    public string Resolve(OutboxMessage message)
    {
        if (!string.IsNullOrWhiteSpace(options.Topic))
        {
            return options.Topic;
        }

        var topic = options.TopicPrefix + Names.GetOrAdd(message.EventType, NameOf);
        return topic.Length > MaxTopicLength ? topic[..MaxTopicLength] : topic;
    }

    private static string NameOf(string eventType)
    {
        var type = Type.GetType(eventType, throwOnError: false);
        return Sanitize(type is null ? FallbackName(eventType) : NameOf(type));
    }

    private static string NameOf(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return string.Join("-", type.GetGenericArguments().Select(NameOf).Prepend(name));
    }

    /// <summary>The type is not loadable here (e.g. a relay process): take its simple name from the text.</summary>
    private static string FallbackName(string eventType)
    {
        var end = eventType.IndexOfAny([',', '[']);
        var fullName = end < 0 ? eventType : eventType[..end];
        var name = fullName[(fullName.LastIndexOfAny(['.', '+']) + 1)..];
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        return arity < 0 ? name : name[..arity];
    }

    /// <summary>Kafka topics allow only letters, digits, '.', '_' and '-'.</summary>
    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }

        return builder.ToString();
    }
}

/// <summary>An outbox message could not be handed to Kafka; the outbox keeps it pending and retries.</summary>
public sealed class OutboxPublishException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Owns the producer: created on first use, flushed and disposed with the application.</summary>
internal sealed class CourierKafkaProducer : IDisposable
{
    private readonly Lazy<IProducer<string, string>> _producer;

    public CourierKafkaProducer(CourierKafkaOptions options, Action<LogMessage>? log = null, Action<Error>? error = null)
        : this(() =>
        {
            var builder = new ProducerBuilder<string, string>(options.BuildProducerConfig());
            if (log is not null)
            {
                builder.SetLogHandler((_, message) => log(message));
            }

            if (error is not null)
            {
                builder.SetErrorHandler((_, e) => error(e));
            }

            return builder.Build();
        })
    {
    }

    internal CourierKafkaProducer(Func<IProducer<string, string>> factory)
    {
        _producer = new Lazy<IProducer<string, string>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IProducer<string, string> Producer => _producer.Value;

    public void Dispose()
    {
        if (!_producer.IsValueCreated)
        {
            return;
        }

        // Every delivered message was already awaited; flushing only covers a send cut short by shutdown.
        _producer.Value.Flush(TimeSpan.FromSeconds(5));
        _producer.Value.Dispose();
    }
}
