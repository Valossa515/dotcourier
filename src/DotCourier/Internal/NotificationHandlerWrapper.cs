using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Internal;

internal readonly record struct NotificationHandlerExecutor(
    object HandlerInstance,
    Func<INotification, CancellationToken, Task> Callback);

internal abstract class NotificationHandlerWrapper
{
    public abstract NotificationHandlerExecutor[] GetExecutors(IServiceProvider services);
}

internal sealed class NotificationHandlerWrapperImpl<TNotification> : NotificationHandlerWrapper
    where TNotification : INotification
{
    public override NotificationHandlerExecutor[] GetExecutors(IServiceProvider services) =>
        services.GetServices<INotificationHandler<TNotification>>()
            .Select(static handler => new NotificationHandlerExecutor(
                handler,
                (notification, token) => handler.Handle((TNotification)notification, token)))
            .ToArray();
}
