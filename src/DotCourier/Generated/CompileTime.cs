using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DotCourier.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCourier.Generated;

/// <summary>
/// Closes open generic behaviors (<c>typeof(MyBehavior&lt;,&gt;)</c>) for a request known at compile time,
/// without <c>MakeGenericType</c>. Native AOT cannot build generic types at runtime, so with
/// <see cref="CourierConfiguration.RequireCompileTimeRegistrations"/> every open behavior is closed this way
/// for every request: the core closes its own, extension packages register one for theirs
/// (<see cref="CourierConfiguration.AddBehaviorTypes"/>), and DotCourier.SourceGenerator writes one for the
/// open behaviors declared in your assembly.
/// </summary>
/// <remarks>Return <c>typeof(MyBehavior&lt;TRequest, TResponse&gt;)</c> for the types you own, <c>null</c> otherwise.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class BehaviorTypes
{
    /// <summary>The closed <see cref="IPipelineBehavior{TRequest,TResponse}"/> for <paramref name="openBehavior"/>, or <c>null</c>.</summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public abstract Type? CloseRequestBehavior<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>(Type openBehavior)
        where TRequest : notnull;

    /// <summary>The closed <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/> for <paramref name="openBehavior"/>, or <c>null</c>.</summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public virtual Type? CloseStreamBehavior<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>(Type openBehavior)
        where TRequest : notnull => null;
}

/// <summary>Members that must survive trimming on request types (DataAnnotations reads them).</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CompileTimeTypes
{
    /// <summary>Public properties and constructors: validation attributes live on both (positional records).</summary>
    public const DynamicallyAccessedMemberTypes Request = DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors;
}

/// <summary>The core's built-in behaviors.</summary>
internal sealed class CoreBehaviorTypes : BehaviorTypes
{
    public static readonly CoreBehaviorTypes Instance = new();

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type? CloseRequestBehavior<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>(Type openBehavior)
    {
        if (openBehavior == typeof(RequestExceptionProcessorBehavior<,>))
        {
            return typeof(RequestExceptionProcessorBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(RequestPreProcessorBehavior<,>))
        {
            return typeof(RequestPreProcessorBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(RequestPostProcessorBehavior<,>))
        {
            return typeof(RequestPostProcessorBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(LoggingBehavior<,>))
        {
            return typeof(LoggingBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(TelemetryBehavior<,>))
        {
            return typeof(TelemetryBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(ValidationBehavior<,>))
        {
            return typeof(ValidationBehavior<TRequest, TResponse>);
        }

        return openBehavior == typeof(TimeoutBehavior<,>) ? typeof(TimeoutBehavior<TRequest, TResponse>) : null;
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type? CloseStreamBehavior<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>(Type openBehavior)
    {
        if (openBehavior == typeof(StreamLoggingBehavior<,>))
        {
            return typeof(StreamLoggingBehavior<TRequest, TResponse>);
        }

        if (openBehavior == typeof(StreamTelemetryBehavior<,>))
        {
            return typeof(StreamTelemetryBehavior<TRequest, TResponse>);
        }

        return openBehavior == typeof(StreamValidationBehavior<,>) ? typeof(StreamValidationBehavior<TRequest, TResponse>) : null;
    }
}

/// <summary>A request or stream type registered at compile time, kept so its behaviors can be closed for it.</summary>
internal interface IKnownRequest
{
    void RegisterBehaviors(ClosedBehaviorRegistration registration);
}

internal sealed class KnownRequest<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse> : IKnownRequest
    where TRequest : notnull
{
    public void RegisterBehaviors(ClosedBehaviorRegistration registration) => registration.Request<TRequest, TResponse>();
}

internal sealed class KnownStream<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse> : IKnownRequest
    where TRequest : notnull
{
    public void RegisterBehaviors(ClosedBehaviorRegistration registration) => registration.Stream<TRequest, TResponse>();
}

/// <summary>
/// Registers every configured behavior closed for each known request, in pipeline order, instead of as
/// open generics the container would close with <c>MakeGenericType</c>.
/// </summary>
internal sealed class ClosedBehaviorRegistration(
    IServiceCollection services,
    IReadOnlyList<CourierConfiguration.BehaviorRegistration> behaviors,
    IReadOnlyList<BehaviorTypes> types)
{
    public void Request<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>()
        where TRequest : notnull
    {
        foreach (var behavior in behaviors)
        {
            if (behavior.ServiceType == typeof(IPipelineBehavior<TRequest, TResponse>))
            {
                services.TryAddEnumerable(new ServiceDescriptor(behavior.ServiceType, behavior.ImplementationType, behavior.Lifetime));
            }
            else if (behavior.ServiceType == typeof(IPipelineBehavior<,>))
            {
                Type? closed = null;
                foreach (var provider in types)
                {
                    closed = provider.CloseRequestBehavior<TRequest, TResponse>(behavior.ImplementationType);
                    if (closed is not null)
                    {
                        break;
                    }
                }

                services.TryAddEnumerable(new ServiceDescriptor(typeof(IPipelineBehavior<TRequest, TResponse>), closed ?? throw Unclosable(behavior, typeof(TRequest)), behavior.Lifetime));
            }
        }
    }

    public void Stream<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>()
        where TRequest : notnull
    {
        foreach (var behavior in behaviors)
        {
            if (behavior.ServiceType == typeof(IStreamPipelineBehavior<TRequest, TResponse>))
            {
                services.TryAddEnumerable(new ServiceDescriptor(behavior.ServiceType, behavior.ImplementationType, behavior.Lifetime));
            }
            else if (behavior.ServiceType == typeof(IStreamPipelineBehavior<,>))
            {
                Type? closed = null;
                foreach (var provider in types)
                {
                    closed = provider.CloseStreamBehavior<TRequest, TResponse>(behavior.ImplementationType);
                    if (closed is not null)
                    {
                        break;
                    }
                }

                services.TryAddEnumerable(new ServiceDescriptor(typeof(IStreamPipelineBehavior<TRequest, TResponse>), closed ?? throw Unclosable(behavior, typeof(TRequest)), behavior.Lifetime));
            }
        }
    }

    /// <summary>Closed behaviors for requests that have no compile-time registration are registered as they are.</summary>
    public void RemainingClosedBehaviors()
    {
        foreach (var behavior in behaviors.Where(b => !b.ServiceType.IsGenericTypeDefinition))
        {
            services.TryAddEnumerable(new ServiceDescriptor(behavior.ServiceType, behavior.ImplementationType, behavior.Lifetime));
        }
    }

    private static InvalidOperationException Unclosable(CourierConfiguration.BehaviorRegistration behavior, Type requestType) =>
        new(
            $"The behavior '{CourierDiagnostics.GetRequestName(behavior.ImplementationType)}' cannot be closed for " +
            $"'{CourierDiagnostics.GetRequestName(requestType)}' without generating code at runtime. Declare it in an assembly " +
            "built with DotCourier.SourceGenerator (without generic constraints other than notnull), or register its closed " +
            "BehaviorTypes with cfg.AddBehaviorTypes(...).");
}

/// <summary>Calls the <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>s of one exception type, without reflection.</summary>
internal interface IExceptionHandlerInvoker<TRequest, TResponse>
    where TRequest : notnull
{
    Type ExceptionType { get; }

    Task Handle(TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken);
}

internal sealed class ExceptionHandlerInvoker<TRequest, TResponse, TException>(IEnumerable<IRequestExceptionHandler<TRequest, TResponse, TException>> handlers)
    : IExceptionHandlerInvoker<TRequest, TResponse>
    where TRequest : notnull
    where TException : Exception
{
    public Type ExceptionType => typeof(TException);

    public async Task Handle(TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            await handler.Handle(request, (TException)exception, state, cancellationToken).ConfigureAwait(false);
            if (state.Handled)
            {
                return;
            }
        }
    }
}
