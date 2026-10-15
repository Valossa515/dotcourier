using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DotCourier.Generated;
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
public sealed class RequestExceptionProcessorBehavior<TRequest, TResponse>(IServiceProvider services, CourierOptions? options = null)
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
            if (options?.RequireCompileTimeRegistrations == true)
            {
                if (await HandleWithInvokers(request, exception, state, cancellationToken).ConfigureAwait(false))
                {
                    return state.Response!;
                }

                throw;
            }

            if (await HandleWithReflection(request, exception, state, cancellationToken).ConfigureAwait(false))
            {
                return state.Response!;
            }

            throw;
        }
    }

    /// <summary>Compile-time mode: the typed invokers generated code registered next to each exception handler.</summary>
    private async Task<bool> HandleWithInvokers(TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken)
    {
        var invokers = services.GetServices<IExceptionHandlerInvoker<TRequest, TResponse>>().ToArray();
        for (var type = exception.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var invoker in invokers)
            {
                if (invoker.ExceptionType != type)
                {
                    continue;
                }

                await invoker.Handle(request, exception, state, cancellationToken).ConfigureAwait(false);
                if (state.Handled)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only reached outside compile-time mode, which is always on under Native AOT.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Only reached outside compile-time mode, which is always on under Native AOT.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Only reached outside compile-time mode, which is always on under Native AOT.")]
    private async Task<bool> HandleWithReflection(TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken)
    {
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
                    return true;
                }
            }
        }

        return false;
    }
}
