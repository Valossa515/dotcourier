using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotCourier.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCourier.Generated;

/// <summary>
/// Registrations produced at compile time by <c>DotCourier.SourceGenerator</c>. Not meant to be
/// called by hand: generated code registers each assembly here, and <c>AddCourier</c> uses that
/// instead of scanning the assembly with reflection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedCourierRegistry
{
    private static readonly ConcurrentDictionary<Assembly, Action<GeneratedRegistrationContext>> Registrations = new();

    /// <summary>Called from the generated module initializer of <paramref name="assembly"/>.</summary>
    public static void Register(Assembly assembly, Action<GeneratedRegistrationContext> register)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(register);
        Registrations[assembly] = register;
    }

    /// <summary>
    /// The generated registration of <paramref name="assembly"/>, if it has one. Runs the assembly's
    /// module initializer first: <c>typeof(T).Assembly</c> loads an assembly without running any of its
    /// code, so the generated initializer may not have run yet.
    /// </summary>
    internal static Action<GeneratedRegistrationContext>? Find(Assembly assembly)
    {
        if (!Registrations.TryGetValue(assembly, out var register))
        {
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            Registrations.TryGetValue(assembly, out register);
        }

        return register;
    }
}

/// <summary>
/// What generated code uses to register an assembly's handlers. Every method is generic over
/// concrete types known at compile time, so registration needs neither assembly scanning nor
/// <c>MakeGenericType</c>, and the dispatch wrappers are created up front.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedRegistrationContext
{
    private const DynamicallyAccessedMemberTypes Constructors = DynamicallyAccessedMemberTypes.PublicConstructors;

    private readonly IServiceCollection _services;
    private readonly ServiceLifetime _lifetime;
    private readonly Action _scan;
    private readonly List<Type> _openRequestHandlers;
    private readonly List<IKnownRequest> _knownRequests;
    private readonly List<BehaviorTypes> _behaviorTypes;
    private readonly bool _compileTime;

    internal GeneratedRegistrationContext(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Action scan,
        List<Type> openRequestHandlers,
        List<IKnownRequest> knownRequests,
        List<BehaviorTypes> behaviorTypes,
        bool compileTime)
    {
        _services = services;
        _lifetime = lifetime;
        _scan = scan;
        _openRequestHandlers = openRequestHandlers;
        _knownRequests = knownRequests;
        _behaviorTypes = behaviorTypes;
        _compileTime = compileTime;
    }

    /// <summary>Registers a request handler (the first one registered for a request wins).</summary>
    public void RequestHandler<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse, [DynamicallyAccessedMembers(Constructors)] THandler>()
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        _services.TryAdd(new ServiceDescriptor(typeof(IRequestHandler<TRequest, TResponse>), typeof(THandler), _lifetime));
        Courier.AddRequestWrapper(typeof(TRequest), static () => new RequestHandlerWrapperImpl<TRequest, TResponse>());
        _knownRequests.Add(new KnownRequest<TRequest, TResponse>());
    }

    /// <summary>Registers a stream request handler (the first one registered for a request wins).</summary>
    public void StreamHandler<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse, [DynamicallyAccessedMembers(Constructors)] THandler>()
        where TRequest : IStreamRequest<TResponse>
        where THandler : class, IStreamRequestHandler<TRequest, TResponse>
    {
        _services.TryAdd(new ServiceDescriptor(typeof(IStreamRequestHandler<TRequest, TResponse>), typeof(THandler), _lifetime));
        Courier.AddStreamWrapper(typeof(TRequest), static () => new StreamHandlerWrapperImpl<TRequest, TResponse>());
        _knownRequests.Add(new KnownStream<TRequest, TResponse>());
    }

    /// <summary>Registers a notification handler.</summary>
    public void NotificationHandler<TNotification, [DynamicallyAccessedMembers(Constructors)] THandler>()
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        _services.TryAddEnumerable(new ServiceDescriptor(typeof(INotificationHandler<TNotification>), typeof(THandler), _lifetime));
        Notification<TNotification>();
    }

    /// <summary>Prepares the dispatch of a notification type, so publishing it needs no runtime code generation.</summary>
    public void Notification<TNotification>()
        where TNotification : INotification =>
        Courier.AddNotificationWrapper(typeof(TNotification), static () => new NotificationHandlerWrapperImpl<TNotification>());

    /// <summary>Registers an exception handler, plus the typed invoker Native AOT uses to call it.</summary>
    public void ExceptionHandler<TRequest, TResponse, TException, [DynamicallyAccessedMembers(Constructors)] THandler>()
        where TRequest : notnull
        where TException : Exception
        where THandler : class, IRequestExceptionHandler<TRequest, TResponse, TException>
    {
        _services.TryAddEnumerable(new ServiceDescriptor(typeof(IRequestExceptionHandler<TRequest, TResponse, TException>), typeof(THandler), _lifetime));
        if (!_compileTime)
        {
            return; // outside compile-time mode the exception behavior finds handlers by reflection
        }

        _services.TryAddEnumerable(new ServiceDescriptor(
            typeof(IExceptionHandlerInvoker<TRequest, TResponse>),
            typeof(ExceptionHandlerInvoker<TRequest, TResponse, TException>),
            ServiceLifetime.Transient));
    }

    /// <summary>Registers one of the services a request can have several of: processors, validators.</summary>
    public void Service<TService, [DynamicallyAccessedMembers(Constructors)] TImplementation>()
        where TService : class
        where TImplementation : class, TService =>
        _services.TryAddEnumerable(new ServiceDescriptor(typeof(TService), typeof(TImplementation), _lifetime));

    /// <summary>Registers an open generic handler or service, with the same rules as assembly scanning.</summary>
    public void OpenGeneric([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | Constructors)] Type openGenericType) =>
        CourierServiceCollectionExtensions.RegisterOpenGeneric(_services, openGenericType, _lifetime, _openRequestHandlers);

    /// <summary>Lets compile-time mode close the open behaviors declared in this assembly.</summary>
    public void BehaviorTypes(BehaviorTypes types)
    {
        ArgumentNullException.ThrowIfNull(types);
        _behaviorTypes.Add(types);
    }

    /// <summary>
    /// Falls back to scanning the assembly with reflection, for assemblies the generator could not fully
    /// describe (e.g. a handler declared as a private nested class).
    /// </summary>
    public void ScanAssembly() => _scan();
}
