using System.Collections.Concurrent;
using Confluent.Kafka;

namespace DotCourier.Tests.Kafka;

/// <summary>Runs only where Docker is available and allowed (CI sets <c>DOTCOURIER_DOCKER_TESTS=true</c>).</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DOTCOURIER_DOCKER_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Needs Docker: set DOTCOURIER_DOCKER_TESTS=true to run it.";
        }
    }
}

/// <summary>
/// Stands in for a Kafka producer. <see cref="Respond"/> decides each send's outcome; by default the
/// message is persisted.
/// </summary>
internal sealed class FakeProducer : IProducer<string, string>
{
    public ConcurrentQueue<(string Topic, Message<string, string> Message)> Sent { get; } = new();

    public Func<string, Message<string, string>, CancellationToken, Task<DeliveryResult<string, string>>>? Respond { get; set; }

    public bool Flushed { get; private set; }

    public bool Disposed { get; private set; }

    public Handle Handle => throw new NotSupportedException();

    public string Name => "fake";

    public Task<DeliveryResult<string, string>> ProduceAsync(string topic, Message<string, string> message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((topic, message));
        return Respond?.Invoke(topic, message, cancellationToken) ?? Task.FromResult(Persisted(topic, message));
    }

    public static DeliveryResult<string, string> Persisted(string topic, Message<string, string> message) => new()
    {
        Topic = topic,
        Message = message,
        Partition = new Partition(0),
        Offset = new Offset(42),
        Status = PersistenceStatus.Persisted,
    };

    public Task<DeliveryResult<string, string>> ProduceAsync(TopicPartition topicPartition, Message<string, string> message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Produce(string topic, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null) =>
        throw new NotSupportedException();

    public void Produce(TopicPartition topicPartition, Message<string, string> message, Action<DeliveryReport<string, string>>? deliveryHandler = null) =>
        throw new NotSupportedException();

    public int Poll(TimeSpan timeout) => 0;

    public int Flush(TimeSpan timeout)
    {
        Flushed = true;
        return 0;
    }

    public void Flush(CancellationToken cancellationToken = default) => Flushed = true;

    public void InitTransactions(TimeSpan timeout) => throw new NotSupportedException();

    public void BeginTransaction() => throw new NotSupportedException();

    public void CommitTransaction(TimeSpan timeout) => throw new NotSupportedException();

    public void CommitTransaction() => throw new NotSupportedException();

    public void AbortTransaction(TimeSpan timeout) => throw new NotSupportedException();

    public void AbortTransaction() => throw new NotSupportedException();

    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) =>
        throw new NotSupportedException();

    public int AddBrokers(string brokers) => 0;

    public void SetSaslCredentials(string username, string password) => throw new NotSupportedException();

    public void Dispose() => Disposed = true;
}
