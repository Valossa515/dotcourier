using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Behaviors;

/// <summary>Runs every <see cref="IRequestPreProcessor{TRequest}"/> before continuing the pipeline.</summary>
public sealed class RequestPreProcessorBehavior<TRequest, TResponse>(IEnumerable<IRequestPreProcessor<TRequest>> processors)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        foreach (var processor in processors)
        {
            await processor.Process(request, cancellationToken).ConfigureAwait(false);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Runs every <see cref="IRequestPostProcessor{TRequest,TResponse}"/> after the pipeline completed successfully.</summary>
public sealed class RequestPostProcessorBehavior<TRequest, TResponse>(IEnumerable<IRequestPostProcessor<TRequest, TResponse>> processors)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken).ConfigureAwait(false);
        foreach (var processor in processors)
        {
            await processor.Process(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}

/// <summary>
/// Offers exceptions to the matching <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>s,
/// from the most specific exception type to <see cref="Exception"/>. The first handler that calls
/// <see cref="RequestExceptionHandlerState{TResponse}.SetHandled"/> supplies the response.
/// </summary>
public sealed class RequestExceptionProcessorBehavior<TRequest, TResponse>(IServiceProvider services)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, (Type HandlerType, MethodInfo Method)> HandlerTypes = new();

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var state = new RequestExceptionHandlerState<TResponse>();
            for (var type = exception.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                var (handlerType, method) = HandlerTypes.GetOrAdd(type, static exceptionType =>
                {
                    var closed = typeof(IRequestExceptionHandler<,,>).MakeGenericType(typeof(TRequest), typeof(TResponse), exceptionType);
                    return (closed, closed.GetMethod(nameof(IRequestExceptionHandler<object, object, Exception>.Handle))!);
                });

                foreach (var handler in services.GetServices(handlerType))
                {
                    try
                    {
                        await ((Task)method.Invoke(handler, [request, exception, state, cancellationToken])!).ConfigureAwait(false);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException is not null)
                    {
                        ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                    }

                    if (state.Handled)
                    {
                        return state.Response!;
                    }
                }
            }

            throw;
        }
    }
}
