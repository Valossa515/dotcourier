using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Internal;

internal abstract class RequestHandlerBase
{
    public abstract Task<object?> HandleBoxed(object request, IServiceProvider services, CancellationToken cancellationToken);
}

internal abstract class RequestHandlerWrapper<TResponse> : RequestHandlerBase
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// Closed over a concrete request type so the hot path is free of reflection:
/// one instance per request type is created and cached by <see cref="Courier"/>.
/// </summary>
internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override async Task<object?> HandleBoxed(object request, IServiceProvider services, CancellationToken cancellationToken) =>
        await Handle((IRequest<TResponse>)request, services, cancellationToken).ConfigureAwait(false);

    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;
        var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? (IRequestHandler<TRequest, TResponse>?)services.GetService<GenericRequestHandlerRegistry>()
                ?.Create(typeof(IRequestHandler<TRequest, TResponse>), services)
            ?? throw new HandlerNotFoundException(typeof(TRequest));

        RequestHandlerDelegate<TResponse> pipeline = token =>
            handler.Handle(typedRequest, token == default ? cancellationToken : token);

        // Behaviors come back in registration order and the first one must be
        // outermost, so wrap from the last to the first.
        var behaviors = services.GetServices<IPipelineBehavior<TRequest, TResponse>>() as IPipelineBehavior<TRequest, TResponse>[]
            ?? services.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var next = pipeline;
            pipeline = token => behavior.Handle(typedRequest, next, token == default ? cancellationToken : token);
        }

        return pipeline(cancellationToken);
    }
}
