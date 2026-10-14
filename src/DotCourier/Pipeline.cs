namespace DotCourier;

/// <summary>
/// Continuation to the next step of the pipeline (the next behavior, or the handler).
/// Passing a token replaces the one the request was sent with.
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);

/// <summary>
/// Wraps handler execution to implement a cross-cutting concern. Behaviors run in
/// order: the first one is outermost. See <see cref="BehaviorOrder"/>.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Runs the behavior; call <paramref name="next"/> to continue the pipeline.</summary>
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>Runs before the handler (and before every built-in behavior).</summary>
/// <typeparam name="TRequest">Request type.</typeparam>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    /// <summary>Processes the request.</summary>
    Task Process(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Runs after the handler completed successfully.</summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    /// <summary>Processes the request and its response.</summary>
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}

/// <summary>
/// Gets a chance to turn an exception thrown while handling <typeparamref name="TRequest"/>
/// into a response. Handlers for the most specific exception type are tried first.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
/// <typeparam name="TException">Exception type handled.</typeparam>
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>
    /// Handles the exception. Call <see cref="RequestExceptionHandlerState{TResponse}.SetHandled"/>
    /// to supply a response; otherwise the exception keeps propagating.
    /// </summary>
    Task Handle(TRequest request, TException exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken);
}

/// <summary>Lets an <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> mark an exception as handled.</summary>
/// <typeparam name="TResponse">Response type.</typeparam>
public sealed class RequestExceptionHandlerState<TResponse>
{
    /// <summary>Whether a handler supplied a response.</summary>
    public bool Handled { get; private set; }

    /// <summary>The response supplied by the handler, if any.</summary>
    public TResponse? Response { get; private set; }

    /// <summary>Marks the exception as handled and supplies the response to return instead.</summary>
    public void SetHandled(TResponse response)
    {
        Handled = true;
        Response = response;
    }
}
