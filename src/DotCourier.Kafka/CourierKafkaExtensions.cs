using Confluent.Kafka;
using DotCourier.Kafka;
using DotCourier.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier;

/// <summary>Sends the transactional outbox to Kafka.</summary>
public static class CourierKafkaExtensions
{
    /// <summary>
    /// Delivers outbox messages to Kafka (<see cref="KafkaOutboxDispatcher"/>) <b>instead of</b> the
    /// in-process notification handlers: enabling it redirects delivery, it does not add to it. Requires
    /// <c>AddOutbox&lt;TDbContext&gt;()</c>. Each event type gets its own topic (<c>courier.OrderPlaced</c>);
    /// register an <see cref="IOutboxTopicResolver"/> to route differently.
    /// </summary>
    public static CourierConfiguration AddKafkaDelivery(this CourierConfiguration configuration, Action<CourierKafkaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CourierKafkaOptions();
        configure(options);
        options.Validate();

        return configuration.ConfigureServices(services =>
        {
            services.TryAddSingleton(options);
            services.TryAddSingleton<IOutboxTopicResolver, EventTypeTopicResolver>();
            services.TryAddSingleton(sp =>
            {
                var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("DotCourier.Kafka") ?? NullLogger.Instance;
                return new CourierKafkaProducer(
                    options,
                    message => logger.Log(ToLogLevel(message.Level), "[Kafka] {Facility}: {Message}", message.Facility, message.Message),
                    error => logger.Log(error.IsFatal ? LogLevel.Critical : LogLevel.Warning, "[Kafka] {Code}: {Reason}", error.Code, error.Reason));
            });

            // Replaces the outbox's in-process dispatcher whatever the order of AddOutbox and AddKafkaDelivery.
            services.RemoveAll<IOutboxDispatcher>();
            services.AddSingleton<IOutboxDispatcher>(sp => new KafkaOutboxDispatcher(
                sp.GetRequiredService<CourierKafkaProducer>(),
                sp.GetRequiredService<IOutboxTopicResolver>(),
                options,
                sp.GetService<ILogger<KafkaOutboxDispatcher>>()));
        });
    }

    private static LogLevel ToLogLevel(SyslogLevel level) => level switch
    {
        SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical => LogLevel.Critical,
        SyslogLevel.Error => LogLevel.Error,
        SyslogLevel.Warning => LogLevel.Warning,
        SyslogLevel.Notice or SyslogLevel.Info => LogLevel.Information,
        _ => LogLevel.Debug,
    };
}
