namespace DotCourier;

/// <summary>Marker shared by every request type.</summary>
public interface IBaseRequest
{
}

/// <summary>A request that produces a <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">Type returned by the handler.</typeparam>
public interface IRequest<out TResponse> : IBaseRequest
{
}

/// <summary>A request that produces no value.</summary>
public interface IRequest : IRequest<Unit>
{
}

/// <summary>A write operation (has side effects) that produces a <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">Type returned by the handler.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}

/// <summary>A write operation (has side effects) that produces no value.</summary>
public interface ICommand : ICommand<Unit>, IRequest
{
}

/// <summary>A read operation (no side effects) that produces a <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">Type returned by the handler.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}

/// <summary>A fire-and-forget event, delivered to every registered <see cref="INotificationHandler{TNotification}"/>.</summary>
public interface INotification
{
}
