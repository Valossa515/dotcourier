using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier.Outbox;

/// <summary>
/// Delivers an outbox message. The default publishes it to the in-process notification handlers;
/// register your own (e.g. to send it to a message broker) to change where messages go.
/// </summary>
/// <remarks>
/// Receives the stored message rather than the notification, so a broker-bound dispatcher can
/// forward <see cref="OutboxMessage.Payload"/> as is. Complete only once delivery is durable: the
/// message is marked delivered when this returns. Throw on failure to have it retried.
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>Delivers <paramref name="message"/>; throws to have it retried later.</summary>
    Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Default dispatcher: restores the notification and publishes it in process. Every handler runs, and
/// any failure is thrown (<see cref="NotificationPublishStrategy.SequentialThenThrow"/>) whatever the
/// configured strategy, so the message is retried instead of counted as delivered.
/// </summary>
internal sealed class CourierOutboxDispatcher(OutboxSerializer serializer, IPublisher publisher) : IOutboxDispatcher
{
    public Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var notification = serializer.Deserialize(message);
        return publisher is Courier courier
            ? courier.Publish(notification, NotificationPublishStrategy.SequentialThenThrow, cancellationToken)
            : publisher.Publish(notification, cancellationToken);
    }
}

/// <summary>
/// Background worker that drains the outbox: it returns abandoned messages to pending, then claims
/// pending messages one by one and hands each to the <see cref="IOutboxDispatcher"/>. Delivery is
/// at least once: a crash between delivering and recording it delivers the message again.
/// </summary>
internal sealed partial class OutboxProcessor<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;
    private readonly CourierOutboxOptions _options;
    private readonly OutboxSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public OutboxProcessor(
        IServiceScopeFactory scopes,
        CourierOutboxOptions options,
        OutboxSignal signal,
        IServiceProvider services,
        ILogger<OutboxProcessor<TDbContext>>? logger = null)
    {
        _scopes = scopes;
        _options = options;
        _signal = signal;
        _time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(typeof(TDbContext).Name, _options.PollInterval.TotalMilliseconds, _options.BatchSize, _options.MaxAttempts);
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A broken cycle (database down, missing table) must not stop the processor for good.
                LogCycleFailed(ex);
            }

            if (handled >= _options.BatchSize)
            {
                continue; // a full batch: more may be waiting
            }

            try
            {
                await _signal.WaitAsync(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs one cycle and returns how many messages it tried to deliver.</summary>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        if (db.Model.FindEntityType(typeof(OutboxMessage)) is null)
        {
            throw new InvalidOperationException(
                $"{typeof(TDbContext).Name} does not map the outbox: call modelBuilder.AddCourierOutbox() in OnModelCreating and add a migration.");
        }

        var messages = db.Set<OutboxMessage>();
        await ReclaimAbandoned(db, messages, cancellationToken).ConfigureAwait(false);

        var batch = await messages
            .Where(m => m.Status == OutboxStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var handled = 0;
        foreach (var message in batch)
        {
            var now = Now();
            if (!await TryUpdate(db, message, m =>
                {
                    m.Status = OutboxStatus.Processing;
                    m.UpdatedAt = now;
                }, cancellationToken).ConfigureAwait(false))
            {
                continue; // another instance claimed it
            }

            handled++;
            await Deliver(db, message, cancellationToken).ConfigureAwait(false);
        }

        return handled;
    }

    private async Task ReclaimAbandoned(TDbContext db, DbSet<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        var abandonedBefore = Now() - _options.ProcessingTimeout;
        var abandoned = await messages
            .Where(m => m.Status == OutboxStatus.Processing && m.UpdatedAt < abandonedBefore)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reclaimed = 0;
        foreach (var message in abandoned)
        {
            var now = Now();
            if (await TryUpdate(db, message, m =>
                {
                    m.Status = OutboxStatus.Pending;
                    m.UpdatedAt = now;
                }, cancellationToken).ConfigureAwait(false))
            {
                reclaimed++;
            }
        }

        if (reclaimed > 0)
        {
            LogReclaimed(reclaimed);
        }
    }

    private async Task Deliver(TDbContext db, OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // A scope per message: handlers get their own DbContext, unaffected by this cycle's tracking.
            await using (var scope = _scopes.CreateAsyncScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                await dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
            }

            var now = Now();
            await TryUpdate(db, message, m =>
            {
                m.Status = OutboxStatus.Processed;
                m.UpdatedAt = now;
                m.ProcessedAt = now;
            }, CancellationToken.None).ConfigureAwait(false);
            LogDelivered(message.Id, message.EventType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var attempts = message.Attempts + 1;
            var failed = attempts >= _options.MaxAttempts;
            var error = Describe(ex);
            var now = Now();
            await TryUpdate(db, message, m =>
            {
                m.Attempts = attempts;
                m.LastError = error.Length > OutboxModelBuilderExtensions.MaxErrorLength ? error[..OutboxModelBuilderExtensions.MaxErrorLength] : error;
                m.Status = failed ? OutboxStatus.Failed : OutboxStatus.Pending;
                m.UpdatedAt = now;
            }, CancellationToken.None).ConfigureAwait(false);

            if (failed)
            {
                LogGaveUp(ex, message.Id, message.EventType, attempts);
            }
            else
            {
                LogAttemptFailed(ex, message.Id, message.EventType, attempts, _options.MaxAttempts);
            }
        }
    }

    /// <summary>
    /// Applies <paramref name="change"/> and saves it, only if nobody changed the status meanwhile
    /// (the status is the concurrency token). Returns <c>false</c> when another instance got there first.
    /// </summary>
    internal static async Task<bool> TryUpdate(DbContext db, OutboxMessage message, Action<OutboxMessage> change, CancellationToken cancellationToken)
    {
        change(message);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(message).State = EntityState.Detached;
            return false;
        }
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    /// <summary>Type and message of the exception and its causes (handler failures come wrapped).</summary>
    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().Name}: {exception.Message}";
        if (exception is AggregateException aggregate)
        {
            return $"{exception.GetType().Name}: [{string.Join("; ", aggregate.InnerExceptions.Select(Describe))}]";
        }

        return exception.InnerException is null ? text : $"{text} ---> {Describe(exception.InnerException)}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] Outbox processor started for {DbContext} (poll every {PollMs} ms, batch {BatchSize}, max {MaxAttempts} attempts)")]
    private partial void LogStarted(string dbContext, double pollMs, int batchSize, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] Outbox cycle failed; retrying on the next one")]
    private partial void LogCycleFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] Outbox: {Count} abandoned message(s) returned to pending")]
    private partial void LogReclaimed(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Courier] Outbox message {MessageId} delivered ({EventType})")]
    private partial void LogDelivered(Guid messageId, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] Outbox message {MessageId} ({EventType}) failed, attempt {Attempt}/{MaxAttempts}")]
    private partial void LogAttemptFailed(Exception exception, Guid messageId, string eventType, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "[Courier] Outbox message {MessageId} ({EventType}) failed {Attempts} times and was marked Failed")]
    private partial void LogGaveUp(Exception exception, Guid messageId, string eventType, int attempts);
}
