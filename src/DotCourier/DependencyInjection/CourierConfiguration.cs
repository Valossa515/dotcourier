using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier;

/// <summary>Options for <c>services.AddCourier(cfg =&gt; ...)</c>.</summary>
public sealed class CourierConfiguration
{
    private readonly List<BehaviorRegistration> _behaviors = [];

    /// <summary>Lifetime of the courier and of the handlers, processors and validators found by scanning. Default transient.</summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>How notifications are delivered. Default <see cref="NotificationPublishStrategy.Sequential"/>.</summary>
    public NotificationPublishStrategy NotificationPublishStrategy { get; set; } = NotificationPublishStrategy.Sequential;

    /// <summary>Registers the logging behavior. Default <c>true</c>.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Registers the tracing/metrics behavior. Default <c>true</c>.</summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>Registers the validation behavior. Default <c>true</c>.</summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>Whether the validation behavior evaluates DataAnnotations. Default <c>true</c>.</summary>
    public bool UseDataAnnotations { get; set; } = true;

    /// <summary>Registers the behavior that enforces <see cref="HandlerTimeoutAttribute"/>. Default <c>true</c>.</summary>
    public bool EnableTimeouts { get; set; } = true;

    internal List<Assembly> AssembliesToRegister { get; } = [];

    internal IReadOnlyList<BehaviorRegistration> Behaviors => _behaviors;

    internal List<Action<IServiceCollection>> ServiceRegistrations { get; } = [];

    /// <summary>
    /// Registers additional services when <c>AddCourier</c> runs. Meant for extension packages
    /// (e.g. <c>cfg.AddResilience()</c>) that need services of their own next to behaviors added
    /// with <see cref="AddOpenBehavior"/>, so everything lands in one ordered registration.
    /// </summary>
    public CourierConfiguration ConfigureServices(Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        ServiceRegistrations.Add(register);
        return this;
    }

    /// <summary>Scans <paramref name="assembly"/> for handlers, processors, exception handlers and validators.</summary>
    public CourierConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!AssembliesToRegister.Contains(assembly))
        {
            AssembliesToRegister.Add(assembly);
        }

        return this;
    }

    /// <summary>Scans each of <paramref name="assemblies"/>.</summary>
    public CourierConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            RegisterServicesFromAssembly(assembly);
        }

        return this;
    }

    /// <summary>Scans the assembly that defines <typeparamref name="T"/>.</summary>
    public CourierConfiguration RegisterServicesFromAssemblyContaining<T>() => RegisterServicesFromAssembly(typeof(T).Assembly);

    /// <summary>Scans the assembly that defines <paramref name="type"/>.</summary>
    public CourierConfiguration RegisterServicesFromAssemblyContaining(Type type) => RegisterServicesFromAssembly(type.Assembly);

    /// <summary>
    /// Adds an open generic behavior such as <c>typeof(AuditBehavior&lt;,&gt;)</c> for every request.
    /// </summary>
    /// <param name="openBehaviorType">An open generic type implementing <see cref="IPipelineBehavior{TRequest,TResponse}"/>.</param>
    /// <param name="order">Position in the pipeline; see <see cref="BehaviorOrder"/>.</param>
    /// <param name="lifetime">Behavior lifetime.</param>
    public CourierConfiguration AddOpenBehavior(Type openBehaviorType, int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        if (!openBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException($"'{openBehaviorType.Name}' must be an open generic type.", nameof(openBehaviorType));
        }

        if (!openBehaviorType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
        {
            throw new ArgumentException($"'{openBehaviorType.Name}' must implement {nameof(IPipelineBehavior<object, object>)}<,>.", nameof(openBehaviorType));
        }

        _behaviors.Add(new BehaviorRegistration(typeof(IPipelineBehavior<,>), openBehaviorType, order, lifetime));
        return this;
    }

    /// <summary>Adds a behavior for one closed request/response pair.</summary>
    public CourierConfiguration AddBehavior<TService, TImplementation>(int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TImplementation : TService =>
        AddBehavior(typeof(TService), typeof(TImplementation), order, lifetime);

    /// <summary>Adds a closed behavior, registered for every <see cref="IPipelineBehavior{TRequest,TResponse}"/> it implements.</summary>
    public CourierConfiguration AddBehavior<TImplementation>(int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        var services = typeof(TImplementation).GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
            .ToArray();
        if (services.Length == 0)
        {
            throw new ArgumentException($"'{typeof(TImplementation).Name}' does not implement {nameof(IPipelineBehavior<object, object>)}<,>.");
        }

        foreach (var service in services)
        {
            AddBehavior(service, typeof(TImplementation), order, lifetime);
        }

        return this;
    }

    /// <summary>Adds a closed behavior for <paramref name="serviceType"/>.</summary>
    public CourierConfiguration AddBehavior(Type serviceType, Type implementationType, int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        _behaviors.Add(new BehaviorRegistration(serviceType, implementationType, order, lifetime));
        return this;
    }

    internal readonly record struct BehaviorRegistration(Type ServiceType, Type ImplementationType, int Order, ServiceLifetime Lifetime);
}
