using System.Text.Json;
using DotCourier.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Outbox;

/// <summary>Options for <c>cfg.AddOutbox&lt;TDbContext&gt;(...)</c>.</summary>
public sealed class CourierOutboxOptions
{
    /// <summary>
    /// How often the processor looks for pending messages. Messages saved through <see cref="IOutbox"/>
    /// wake it up right after the commit, so this is only the safety net (e.g. after a restart).
    /// Default 5 s.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Most messages delivered per cycle. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Failed attempts before a message is marked <see cref="OutboxStatus.Failed"/>. Default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// How long a message may stay <see cref="OutboxStatus.Processing"/> before it is considered
    /// abandoned (the instance delivering it crashed) and delivered again. Default 1 min.
    /// </summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Run the background processor in this application. Set <c>false</c> on instances that only write
    /// to the outbox. Default <c>true</c>.
    /// </summary>
    public bool RunProcessor { get; set; } = true;

    /// <summary>JSON options for the stored notifications. Default: <see cref="JsonSerializerDefaults.Web"/>.</summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);

    internal void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "Must be positive.");
        }

        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "Must be at least 1.");
        }

        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "Must be at least 1.");
        }

        if (ProcessingTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ProcessingTimeout), ProcessingTimeout, "Must be positive.");
        }

        ArgumentNullException.ThrowIfNull(JsonSerializerOptions);
    }
}

/// <summary>
/// Captures notifications into the outbox, in the same database transaction as your data. They are
/// delivered later, at least once, by the background processor (to the notification handlers by
/// default). Handlers must therefore tolerate receiving a notification twice.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Adds <paramref name="notification"/> to the outbox. It is written by your next
    /// <c>SaveChangesAsync</c>, atomically with the other changes: use this inside a unit of work
    /// that saves afterwards.
    /// </summary>
    void Add(INotification notification);

    /// <summary>
    /// Adds <paramref name="notification"/> and saves the context, together with any other pending
    /// changes. Inside a DotCourier command it is part of the command's transaction: rolled back with it.
    /// </summary>
    Task PublishAsync(INotification notification, CancellationToken cancellationToken = default);
}

internal sealed class Outbox<TDbContext> : IOutbox
    where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly OutboxSerializer _serializer;
    private readonly OutboxSignal _signal;
    private readonly ICourierTransaction? _transaction;
    private readonly TimeProvider _time;
    private bool _watchingSaves;

    public Outbox(TDbContext db, OutboxSerializer serializer, OutboxSignal signal, IServiceProvider services)
    {
        _db = db;
        _serializer = serializer;
        _signal = signal;
        _transaction = services.GetService<ICourierTransaction>();
        _time = services.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    public void Add(INotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var message = OutboxMessage.Create(OutboxSerializer.TypeName(notification.GetType()), _serializer.Serialize(notification), _time.GetUtcNow().UtcDateTime);
        _db.Set<OutboxMessage>().Add(message);
        WakeProcessorWhenStored();
    }

    public async Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        Add(notification);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inside a command, the message becomes visible at the commit; otherwise at the next save. The
    /// processor is woken then, instead of waiting for its next poll.
    /// </summary>
    private void WakeProcessorWhenStored()
    {
        if (_transaction is { IsActive: true })
        {
            _ = _transaction.OnCommittedAsync(_ =>
            {
                _signal.Notify();
                return Task.CompletedTask;
            });
            return;
        }

        if (!_watchingSaves)
        {
            _watchingSaves = true;
            _db.SavedChanges += (_, _) => _signal.Notify();
        }
    }
}

/// <summary>Wakes the processor early when new messages are committed.</summary>
internal sealed class OutboxSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled: the next cycle picks up every pending message anyway.
        }
    }

    /// <summary>Waits for a signal or <paramref name="timeout"/>, whichever comes first.</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _semaphore.WaitAsync(timeout, cancellationToken);
}
