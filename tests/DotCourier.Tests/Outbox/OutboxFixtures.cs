using DotCourier.Outbox;
using DotCourier.Tests.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Tests.Outbox;

public sealed record ItemShipped(string Name) : INotification;

/// <summary>Fails for names starting with "poison".</summary>
public sealed class ItemShippedHandler(CallLog log) : INotificationHandler<ItemShipped>
{
    public Task Handle(ItemShipped notification, CancellationToken ct)
    {
        if (notification.Name.StartsWith("poison", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("cannot ship " + notification.Name);
        }

        log.Add("shipped " + notification.Name);
        return Task.CompletedTask;
    }
}

/// <summary>A closed generic notification, like a domain-event wrapper.</summary>
public sealed record Envelope<TEvent>(TEvent Event, int Version) : INotification;

public sealed class EnvelopeHandler(CallLog log) : INotificationHandler<Envelope<ItemShipped>>
{
    public Task Handle(Envelope<ItemShipped> notification, CancellationToken ct)
    {
        log.Add($"envelope {notification.Event.Name} v{notification.Version}");
        return Task.CompletedTask;
    }
}

/// <summary>Saves an item and captures <see cref="ItemShipped"/> in the outbox, then optionally fails.</summary>
public sealed record ShipItem(string Name, bool FailAfterSave = false) : ICommand<int>;

public sealed class ShipItemHandler(IServiceProvider services) : ICommandHandler<ShipItem, int>
{
    public async Task<int> Handle(ShipItem command, CancellationToken ct)
    {
        var db = services.Shop();
        db.Items.Add(new Item { Name = command.Name });
        services.GetRequiredService<IOutbox>().Add(new ItemShipped(command.Name));
        var saved = await db.SaveChangesAsync(ct);
        return command.FailAfterSave ? throw new InvalidOperationException("after save") : saved;
    }
}

public sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Records what it was asked to deliver instead of publishing.</summary>
public sealed class RecordingDispatcher(CallLog log) : IOutboxDispatcher
{
    public Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        log.Add($"dispatched {message.EventType} {message.Payload}");
        return Task.CompletedTask;
    }
}
