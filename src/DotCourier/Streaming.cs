namespace DotCourier;

/// <summary>
/// A request answered by a stream of <typeparamref name="TResponse"/> items rather than one value,
/// e.g. an export or a large query read page by page. Send it with
/// <see cref="ISender.CreateStream{TResponse}(IStreamRequest{TResponse}, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResponse">Type of each item.</typeparam>
public interface IStreamRequest<out TResponse>;

/// <summary>Produces the items of a <typeparamref name="TRequest"/> stream, one by one.</summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Type of each item.</typeparam>
public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <summary>
    /// Returns the items. Write it as an async iterator (<c>yield return</c>): it starts when the
    /// caller begins reading, and stops when the caller stops or <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Continuation to the next step of a stream pipeline (the next behavior, or the handler).</summary>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>();

/// <summary>
/// Wraps a stream to implement a cross-cutting concern: it can run code before the first item,
/// observe or transform each item, and run code when the stream ends. Behaviors run in
/// <see cref="BehaviorOrder"/>; the first one is outermost.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Type of each item.</typeparam>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Returns the stream; enumerate <paramref name="next"/>() to continue the pipeline.</summary>
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
