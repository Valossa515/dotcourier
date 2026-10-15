using DotCourier.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DotCourier;

/// <summary>Adds the transactional outbox to DotCourier.</summary>
public static class CourierOutboxExtensions
{
    /// <summary>
    /// Registers <see cref="IOutbox"/>, which stores notifications in <typeparamref name="TDbContext"/>
    /// (map the table with <c>modelBuilder.AddCourierOutbox()</c>), and a background processor that
    /// delivers them, at least once, to the notification handlers (or to your
    /// <see cref="IOutboxDispatcher"/>). Combine with <c>AddEntityFrameworkTransactions</c> so a
    /// command's messages are committed or rolled back with its data.
    /// </summary>
    public static CourierConfiguration AddOutbox<TDbContext>(this CourierConfiguration configuration, Action<CourierOutboxOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CourierOutboxOptions();
        configure?.Invoke(options);
        options.Validate();

        return configuration.ConfigureServices(services =>
        {
            services.TryAddSingleton(options);
            services.TryAddSingleton<OutboxSerializer>();
            services.TryAddSingleton<OutboxSignal>();
            services.TryAddScoped<IOutbox, Outbox<TDbContext>>();
            services.TryAddScoped<IOutboxDispatcher, CourierOutboxDispatcher>();
            services.TryAddSingleton<OutboxProcessor<TDbContext>>();
            if (options.RunProcessor)
            {
                services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxProcessor<TDbContext>>());
            }
        });
    }
}
