using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotCourier.Kafka;
using DotCourier.Outbox;
using DotCourier.Tests.EntityFrameworkCore;
using DotCourier.Tests.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.Kafka;

namespace DotCourier.Tests.Kafka;

public sealed class KafkaTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FakeProducer _producer = new();

    public void Dispose() => _database.Dispose();

    private (ServiceProvider Services, CallLog Log) Build(
        Action<CourierKafkaOptions>? kafka = null,
        Action<IServiceCollection>? services = null,
        bool kafkaFirst = false,
        bool realProducer = false) =>
        TestHost.Build(
            cfg =>
            {
                void AddKafka() => cfg.AddKafkaDelivery(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    kafka?.Invoke(o);
                });

                if (kafkaFirst)
                {
                    AddKafka();
                }

                cfg.AddEntityFrameworkTransactions<ShopDb>().AddOutbox<ShopDb>(o => o.MaxAttempts = 3);
                if (!kafkaFirst)
                {
                    AddKafka();
                }
            },
            registerServices: s =>
            {
                _database.Register(s);
                services?.Invoke(s);
            },
            configureAfter: realProducer ? null : s => s.Replace(ServiceDescriptor.Singleton(new CourierKafkaProducer(() => _producer))));

    private static async Task Ship(ServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICourier>().Send(new ShipItem(name));
    }

    private static Task<int> ProcessOnce(ServiceProvider services) =>
        services.GetRequiredService<OutboxProcessor<ShopDb>>().ProcessOnceAsync(CancellationToken.None);

    private static string Header(Message<string, string> message, string name) =>
        Encoding.UTF8.GetString(message.Headers.GetLastBytes(name));

    [Fact]
    public async Task Messages_go_to_Kafka_as_stored_instead_of_the_in_process_handlers()
    {
        var (services, log) = Build();
        await Ship(services, "pen");

        await ProcessOnce(services);

        var (topic, message) = Assert.Single(_producer.Sent);
        var row = Assert.Single(_database.OutboxRows());
        Assert.Equal("courier.ItemShipped", topic);
        Assert.Equal(row.Id.ToString(), message.Key);
        Assert.Equal("""{"name":"pen"}""", message.Value); // the stored JSON, untouched
        Assert.Equal(row.Id.ToString(), Header(message, KafkaOutboxDispatcher.MessageIdHeader));
        Assert.Equal("DotCourier.Tests.Outbox.ItemShipped, DotCourier.Tests", Header(message, KafkaOutboxDispatcher.EventTypeHeader));
        Assert.Equal(OutboxStatus.Processed, row.Status);
        Assert.Empty(log.Entries); // redirected, not duplicated: no handler ran
    }

    [Fact]
    public async Task The_registration_order_does_not_matter()
    {
        var (services, log) = Build(kafkaFirst: true);
        await Ship(services, "pen");

        await ProcessOnce(services);

        Assert.Single(_producer.Sent);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Generic_events_get_readable_topic_names()
    {
        var (services, _) = Build(o => o.TopicPrefix = "shop.");
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOutbox>().PublishAsync(new Envelope<ItemShipped>(new ItemShipped("cup"), 1));
        }

        await ProcessOnce(services);

        Assert.Equal("shop.Envelope-ItemShipped", Assert.Single(_producer.Sent).Topic);
    }

    [Fact]
    public async Task A_fixed_topic_receives_every_event()
    {
        var (services, _) = Build(o => o.Topic = "store-events");
        await Ship(services, "pen");

        await ProcessOnce(services);

        Assert.Equal("store-events", Assert.Single(_producer.Sent).Topic);
    }

    [Fact]
    public async Task A_custom_topic_resolver_replaces_the_default()
    {
        var (services, _) = Build(services: s => s.AddSingleton<IOutboxTopicResolver, TenantTopics>());
        await Ship(services, "pen");

        await ProcessOnce(services);

        Assert.Equal("tenant-a.events", Assert.Single(_producer.Sent).Topic);
    }

    [Fact]
    public async Task A_rejected_send_leaves_the_message_pending_with_the_reason()
    {
        _producer.Respond = (_, _, _) => throw new ProduceException<string, string>(new Error(ErrorCode.Local_MsgTimedOut, "broker unreachable"), null!);
        var (services, _) = Build();
        await Ship(services, "pen");

        await ProcessOnce(services);

        var row = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("OutboxPublishException", row.LastError, StringComparison.Ordinal);
        Assert.Contains("broker unreachable", row.LastError, StringComparison.Ordinal);

        _producer.Respond = null; // the broker is back
        await ProcessOnce(services);
        Assert.Equal(OutboxStatus.Processed, Assert.Single(_database.OutboxRows()).Status);
    }

    [Fact]
    public async Task An_unacknowledged_send_fails_after_SendTimeout()
    {
        _producer.Respond = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };
        var (services, _) = Build(o => o.SendTimeout = TimeSpan.FromMilliseconds(100));
        await Ship(services, "pen");

        await ProcessOnce(services);

        var row = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Contains("not acknowledged by Kafka", row.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_send_that_is_not_persisted_is_not_counted_as_delivered()
    {
        _producer.Respond = (topic, message, _) =>
        {
            var result = FakeProducer.Persisted(topic, message);
            result.Status = PersistenceStatus.PossiblyPersisted;
            return Task.FromResult(result);
        };
        var (services, _) = Build();
        await Ship(services, "pen");

        await ProcessOnce(services);

        var row = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Contains("PossiblyPersisted", row.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void The_producer_is_durable_by_default_and_configurable()
    {
        var options = new CourierKafkaOptions
        {
            BootstrapServers = "broker:9092",
            SendTimeout = TimeSpan.FromSeconds(7),
            ConfigureProducer = c => c.CompressionType = CompressionType.Zstd,
        };

        var config = options.BuildProducerConfig();

        Assert.Equal("broker:9092", config.BootstrapServers);
        Assert.Equal(Acks.All, config.Acks);
        Assert.True(config.EnableIdempotence);
        Assert.Equal(7000, config.MessageTimeoutMs);
        Assert.Equal(CompressionType.Zstd, config.CompressionType);
    }

    [Fact]
    public void Topic_names_survive_types_that_cannot_be_loaded()
    {
        var resolver = new EventTypeTopicResolver(new CourierKafkaOptions());

        Assert.Equal("courier.OrderPlaced", resolver.Resolve(OutboxMessage.Create("Other.Service.Events.OrderPlaced, Other.Service", "{}", DateTime.UtcNow)));
        Assert.Equal("courier.Wrapper", resolver.Resolve(OutboxMessage.Create("Other.Wrapper`1[[Other.X, Other]], Other", "{}", DateTime.UtcNow)));
        Assert.Equal("courier.Inner", resolver.Resolve(OutboxMessage.Create("Other.Outer+Inner, Other", "{}", DateTime.UtcNow)));
    }

    [Fact]
    public void The_producer_is_flushed_and_disposed_only_if_it_was_used()
    {
        var unused = new FakeProducer();
        new CourierKafkaProducer(() => unused).Dispose();
        Assert.False(unused.Disposed);

        var used = new FakeProducer();
        var holder = new CourierKafkaProducer(() => used);
        _ = holder.Producer;
        holder.Dispose();
        Assert.True(used.Flushed);
        Assert.True(used.Disposed);
    }

    [Fact]
    public void Options_are_validated_at_startup()
    {
        Assert.Throws<ArgumentException>(() => Build(o => o.BootstrapServers = " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.SendTimeout = TimeSpan.Zero));
    }

    [DockerFact]
    public async Task End_to_end_with_a_real_Kafka_broker()
    {
        await using var kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.1").Build();
        await kafka.StartAsync();
        var bootstrap = kafka.GetBootstrapAddress();
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build())
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = "courier.ItemShipped", NumPartitions = 1, ReplicationFactor = 1 }]);
        }

        var (services, _) = Build(o => o.BootstrapServers = bootstrap, realProducer: true);
        await Ship(services, "pen");
        Assert.Equal(1, await ProcessOnce(services));
        var row = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Processed, row.Status);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = "dotcourier-tests",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe("courier.ItemShipped");
        var record = consumer.Consume(TimeSpan.FromSeconds(30));

        Assert.NotNull(record);
        Assert.Equal(row.Id.ToString(), record.Message.Key);
        Assert.Equal("""{"name":"pen"}""", record.Message.Value);
        Assert.Equal(row.Id.ToString(), Header(record.Message, KafkaOutboxDispatcher.MessageIdHeader));
        await services.DisposeAsync(); // flushes and disposes the producer
    }

    private sealed class TenantTopics : IOutboxTopicResolver
    {
        public string Resolve(OutboxMessage message) => "tenant-a.events";
    }
}
