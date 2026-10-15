using Microsoft.EntityFrameworkCore;

namespace DotCourier.Outbox;

/// <summary>Where an <see cref="OutboxMessage"/> is in its delivery.</summary>
public enum OutboxStatus
{
    /// <summary>Waiting to be delivered (new, or failed and due for another attempt).</summary>
    Pending,

    /// <summary>Claimed by a processor and being delivered.</summary>
    Processing,

    /// <summary>Delivered.</summary>
    Processed,

    /// <summary>Gave up after <see cref="CourierOutboxOptions.MaxAttempts"/> failed attempts.</summary>
    Failed,
}

/// <summary>
/// One row of the outbox: a notification captured inside a business transaction, to be delivered
/// later by the outbox processor. Mapped in your <see cref="DbContext"/> by
/// <see cref="OutboxModelBuilderExtensions.AddCourierOutbox"/>.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    /// <summary>Unique identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Type of the notification, assembly-qualified without version (survives upgrades).</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>The notification serialized as JSON.</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>Delivery state. Also the concurrency token that makes claiming a message atomic.</summary>
    public OutboxStatus Status { get; internal set; }

    /// <summary>Failed delivery attempts so far.</summary>
    public int Attempts { get; internal set; }

    /// <summary>The last delivery error (exception type and message), or <c>null</c>.</summary>
    public string? LastError { get; internal set; }

    /// <summary>When the message was captured (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Last status change (UTC); used to reclaim messages left in <see cref="OutboxStatus.Processing"/>.</summary>
    public DateTime UpdatedAt { get; internal set; }

    /// <summary>When the message was delivered (UTC), or <c>null</c>.</summary>
    public DateTime? ProcessedAt { get; internal set; }

    internal static OutboxMessage Create(string eventType, string payload, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EventType = eventType,
        Payload = payload,
        Status = OutboxStatus.Pending,
        CreatedAt = now,
        UpdatedAt = now,
    };
}

/// <summary>Maps the outbox table in your model.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>Longest stored <see cref="OutboxMessage.LastError"/>.</summary>
    internal const int MaxErrorLength = 2000;

    /// <summary>
    /// Maps <see cref="OutboxMessage"/> to <paramref name="tableName"/>. Call it from
    /// <c>OnModelCreating</c>, then add a migration as for any other table.
    /// </summary>
    public static ModelBuilder AddCourierOutbox(this ModelBuilder modelBuilder, string tableName = "courier_outbox", string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable(tableName, schema);
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.EventType).HasMaxLength(512).IsRequired();
            message.Property(m => m.Payload).IsRequired();
            message.Property(m => m.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
            message.Property(m => m.LastError).HasMaxLength(MaxErrorLength);
            message.HasIndex(m => new { m.Status, m.CreatedAt });
        });
        return modelBuilder;
    }
}
