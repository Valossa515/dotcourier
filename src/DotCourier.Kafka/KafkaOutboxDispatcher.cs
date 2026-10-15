using System.Text;
using Confluent.Kafka;
using DotCourier.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier.Kafka;

/// <summary>
/// <see cref="IOutboxDispatcher"/> that publishes outbox messages to Kafka instead of the in-process
/// notification handlers, so the events leave the application.
/// </summary>
/// <remarks>
/// <para>The stored JSON is forwarded <b>as is</b>: no deserialize/serialize round trip, and consumers do
/// not need the producer's event types.</para>
/// <para><b>The send is awaited on purpose.</b> The outbox marks a message delivered when this returns, so
/// it waits for the broker's acknowledgement; returning once the send is merely queued would let a crash
/// lose the event, which is precisely what the outbox prevents. Failures throw, so the message stays
/// pending and is retried.</para>
/// <para>Record: key = outbox message id (spreads load over partitions, but gives <b>no ordering per
/// aggregate</b>; register your own dispatcher if you need that); value = the stored JSON; headers
/// <see cref="MessageIdHeader"/> (for consumer-side deduplication: delivery is at least once) and
/// <see cref="EventTypeHeader"/>.</para>
/// </remarks>
public sealed partial class KafkaOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>Header with the outbox message id. Delivery is at least once: consumers deduplicate on it.</summary>
    public const string MessageIdHeader = "courier-message-id";

    /// <summary>Header with the event's type name, for routing without parsing the payload.</summary>
    public const string EventTypeHeader = "courier-event-type";

    private readonly CourierKafkaProducer _producer;
    private readonly IOutboxTopicResolver _topics;
    private readonly CourierKafkaOptions _options;
    private readonly ILogger _logger;

    internal KafkaOutboxDispatcher(
        CourierKafkaProducer producer,
        IOutboxTopicResolver topics,
        CourierKafkaOptions options,
        ILogger<KafkaOutboxDispatcher>? logger = null)
    {
        _producer = producer;
        _topics = topics;
        _options = options;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var topic = _topics.Resolve(message);
        var id = message.Id.ToString();
        var record = new Message<string, string>
        {
            Key = id,
            Value = message.Payload,
            Headers = new Headers
            {
                { MessageIdHeader, Encoding.UTF8.GetBytes(id) },
                { EventTypeHeader, Encoding.UTF8.GetBytes(message.EventType) },
            },
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.SendTimeout);
        DeliveryResult<string, string> result;
        try
        {
            result = await _producer.Producer.ProduceAsync(topic, record, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OutboxPublishException(
                $"Outbox message {id} was not acknowledged by Kafka (topic '{topic}') within {_options.SendTimeout.TotalMilliseconds:0} ms.");
        }
        catch (KafkaException ex)
        {
            // Covers ProduceException (delivery failed) and failures raised before sending (e.g. no broker).
            throw new OutboxPublishException($"Outbox message {id} could not be published to topic '{topic}': {ex.Error.Reason}", ex);
        }

        if (result.Status != PersistenceStatus.Persisted)
        {
            throw new OutboxPublishException($"Outbox message {id} was not persisted by Kafka (topic '{topic}', status {result.Status}).");
        }

        LogPublished(id, message.EventType, topic, result.Partition.Value, result.Offset.Value);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Courier] Outbox message {MessageId} ({EventType}) published to {Topic} [{Partition}] @{Offset}")]
    private partial void LogPublished(string messageId, string eventType, string topic, int partition, long offset);
}
