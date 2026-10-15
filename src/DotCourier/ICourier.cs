namespace DotCourier;

/// <summary>Sends requests to their single handler.</summary>
public interface ISender
{
    /// <summary>
    /// Sends a request through the pipeline to its handler. Exceptions propagate
    /// to the caller (same semantics as MediatR); use
    /// <see cref="ICourier.Dispatch{TResponse}(IRequest{TResponse}, CancellationToken)"/>
    /// to get a <see cref="Response{T}"/> instead.
    /// </summary>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Sends a request whose type is only known at runtime.</summary>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a stream request through the stream pipeline to its handler and returns the items as
    /// they are produced. Nothing runs until the stream is read; exceptions surface while reading.
    /// </summary>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Creates a stream for a request whose type is only known at runtime.</summary>
    IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default);
}

/// <summary>Publishes notifications to every registered handler.</summary>
public interface IPublisher
{
    /// <summary>Publishes a notification according to the configured <see cref="NotificationPublishStrategy"/>.</summary>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>Publishes a notification whose type is only known at runtime.</summary>
    Task Publish(object notification, CancellationToken cancellationToken = default);
}

/// <summary>Sends requests and publishes notifications. Kept for drop-in migration from MediatR.</summary>
public interface IMediator : ISender, IPublisher
{
}

/// <summary>
/// The DotCourier dispatcher: everything <see cref="IMediator"/> does, plus
/// <c>Dispatch</c>, which converts the outcome into a <see cref="Response{T}"/>.
/// </summary>
public interface ICourier : IMediator
{
    /// <summary>
    /// Sends a request and wraps the outcome in a <see cref="Response{T}"/>. Exceptions
    /// are converted to error responses and never rethrown, except cancellation
    /// requested through <paramref name="cancellationToken"/>. Only
    /// <see cref="CourierException"/> messages reach the response; any other
    /// exception yields a generic message so internals do not leak.
    /// </summary>
    Task<Response<TResponse>> Dispatch<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request whose handler already returns a <see cref="Response{T}"/>; the
    /// handler's response is returned as-is instead of being wrapped again.
    /// </summary>
    Task<Response<TResponse>> Dispatch<TResponse>(IRequest<Response<TResponse>> request, CancellationToken cancellationToken = default);
}
