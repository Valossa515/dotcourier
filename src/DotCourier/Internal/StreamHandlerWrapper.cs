using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Internal;

internal abstract class StreamHandlerBase
{
    public abstract IAsyncEnumerable<object?> HandleBoxed(object request, IServiceProvider services, CancellationToken cancellationToken);
}

internal abstract class StreamHandlerWrapper<TResponse> : StreamHandlerBase
{
    public abstract IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// Closed over a concrete stream request type, cached by <see cref="Courier"/>. Like MediatR, nothing
/// runs (not even the handler lookup) until the caller starts reading the stream.
/// </summary>
internal sealed class StreamHandlerWrapperImpl<TRequest, TResponse> : StreamHandlerWrapper<TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public override async IAsyncEnumerable<object?> HandleBoxed(object request, IServiceProvider services, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in Handle((IStreamRequest<TResponse>)request, services, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public override async IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider services, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The token is the caller's merged with any given to WithCancellation; every step receives it.
        var typedRequest = (TRequest)request;
        var handler = services.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?? (IStreamRequestHandler<TRequest, TResponse>?)services.GetService<GenericRequestHandlerRegistry>()
                ?.Create(typeof(IStreamRequestHandler<TRequest, TResponse>), services)
            ?? throw new HandlerNotFoundException(typeof(TRequest), services.GetService<CourierOptions>()?.Messages);

        StreamHandlerDelegate<TResponse> pipeline = () => handler.Handle(typedRequest, cancellationToken);

        // Registration order, first outermost: wrap from the last to the first.
        var behaviors = services.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>() as IStreamPipelineBehavior<TRequest, TResponse>[]
            ?? services.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>().ToArray();
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = pipeline;
            pipeline = () => behavior.Handle(typedRequest, next, cancellationToken);
        }

        await foreach (var item in pipeline().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
