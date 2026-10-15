using System.Reflection;

namespace DotCourier.Behaviors;

/// <summary>
/// Enforces <see cref="HandlerTimeoutAttribute"/>: the handler receives a token that is
/// cancelled when the timeout elapses, and the caller gets a
/// <see cref="RequestTimeoutException"/> (504) even if the handler ignores the token.
/// Requests without the attribute pass straight through.
/// </summary>
public sealed class TimeoutBehavior<TRequest, TResponse>(CourierOptions? options = null) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly TimeSpan? Timeout =
        typeof(TRequest).GetCustomAttribute<HandlerTimeoutAttribute>(inherit: true)?.Timeout;

    /// <inheritdoc />
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
        Timeout is { } timeout ? HandleWithTimeout(next, timeout, cancellationToken) : next(cancellationToken);

    private async Task<TResponse> HandleWithTimeout(RequestHandlerDelegate<TResponse> next, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            return await next(cts.Token).WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new RequestTimeoutException(typeof(TRequest), timeout, options?.Messages);
        }
    }
}
