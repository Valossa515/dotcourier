namespace DotCourier;

/// <summary>How <see cref="IPublisher.Publish{TNotification}"/> runs the handlers of a notification.</summary>
public enum NotificationPublishStrategy
{
    /// <summary>
    /// Handlers run one by one in registration order. A failing handler is logged
    /// and the remaining handlers still run; <c>Publish</c> does not throw. Default.
    /// </summary>
    Sequential,

    /// <summary>
    /// Handlers run concurrently and <c>Publish</c> completes when all of them have.
    /// Failures are logged and do not throw. Handlers must not share non-thread-safe
    /// scoped services (such as an EF Core <c>DbContext</c>).
    /// </summary>
    ParallelWhenAll,

    /// <summary>
    /// Handlers run one by one; the first failure stops the remaining handlers and is
    /// rethrown wrapped in a <see cref="NotificationHandlerException"/>.
    /// </summary>
    StopOnFirstError,

    /// <summary>
    /// Handlers run one by one and all of them run, even after a failure; then <c>Publish</c> throws:
    /// the <see cref="NotificationHandlerException"/> of the failure, or an <see cref="AggregateException"/>
    /// of them when several handlers failed. The outbox delivers with it, so a failure is retried.
    /// </summary>
    SequentialThenThrow,
}
