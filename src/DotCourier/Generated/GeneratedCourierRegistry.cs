using System.Collections.Concurrent;
using System.ComponentModel;
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
    private readonly IServiceCollection _services;
    private readonly ServiceLifetime _lifetime;
    private readonly Assembly _assembly;
    private readonly Action<Assembly> _scan;
    private readonly Action<Type> _openGeneric;

    internal GeneratedRegistrationContext(IServiceCollection services, ServiceLifetime lifetime, Assembly assembly, Action<Assembly> scan, Action<Type> openGeneric)
    {
        _services = services;
        _lifetime = lifetime;
        _assembly = assembly;
        _scan = scan;
        _openGeneric = openGeneric;
    }

    /// <summary>Registers a request handler (the first one registered for a request wins).</summary>
    public void RequestHandler<TRequest, TResponse, THandler>()
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        _services.TryAdd(new ServiceDescriptor(typeof(IRequestHandler<TRequest, TResponse>), typeof(THandler), _lifetime));
        Courier.AddRequestWrapper(typeof(TRequest), static () => new RequestHandlerWrapperImpl<TRequest, TResponse>());
    }

    /// <summary>Registers a stream request handler (the first one registered for a request wins).</summary>
    public void StreamHandler<TRequest, TResponse, THandler>()
        where TRequest : IStreamRequest<TResponse>
        where THandler : class, IStreamRequestHandler<TRequest, TResponse>
    {
        _services.TryAdd(new ServiceDescriptor(typeof(IStreamRequestHandler<TRequest, TResponse>), typeof(THandler), _lifetime));
        Courier.AddStreamWrapper(typeof(TRequest), static () => new StreamHandlerWrapperImpl<TRequest, TResponse>());
    }

    /// <summary>Registers a notification handler.</summary>
    public void NotificationHandler<TNotification, THandler>()
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        _services.TryAddEnumerable(new ServiceDescriptor(typeof(INotificationHandler<TNotification>), typeof(THandler), _lifetime));
        Courier.AddNotificationWrapper(typeof(TNotification), static () => new NotificationHandlerWrapperImpl<TNotification>());
    }

    /// <summary>Registers one of the services a request can have several of: processors, exception handlers, validators.</summary>
    public void Service<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService =>
        _services.TryAddEnumerable(new ServiceDescriptor(typeof(TService), typeof(TImplementation), _lifetime));

    /// <summary>Registers an open generic handler or service, with the same rules as assembly scanning.</summary>
    public void OpenGeneric(Type openGenericType) => _openGeneric(openGenericType);

    /// <summary>
    /// Falls back to scanning the assembly with reflection, for assemblies the generator could not fully
    /// describe (e.g. a handler declared as a private nested class).
    /// </summary>
    public void ScanAssembly() => _scan(_assembly);
}
