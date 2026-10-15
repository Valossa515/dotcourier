using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotCourier.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier;

/// <summary>Options for <c>services.AddCourier(cfg =&gt; ...)</c>.</summary>
public sealed class CourierConfiguration
{
    private const DynamicallyAccessedMemberTypes OpenBehavior = DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicConstructors;

    private readonly List<BehaviorRegistration> _behaviors = [];
    private readonly List<BehaviorTypes> _behaviorTypes = [];

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

    /// <summary>
    /// Use the registrations generated at compile time by <c>DotCourier.SourceGenerator</c> for the
    /// assemblies that have them, instead of scanning those assemblies with reflection. Assemblies built
    /// without the generator are always scanned. Default <c>true</c>.
    /// </summary>
    public bool UseGeneratedRegistrations { get; set; } = true;

    /// <summary>
    /// Compile-time mode: everything DotCourier would otherwise build at runtime with reflection
    /// (<c>MakeGenericType</c>) must come from DotCourier.SourceGenerator instead. Behaviors are registered
    /// closed for every known request, exception handlers are called through typed invokers, and a request
    /// without a generated registration fails with a clear message instead of being scanned. Default
    /// <c>true</c> under Native AOT, where it is required, and <c>false</c> otherwise; turn it on in tests to
    /// find what would break before publishing with <c>PublishAot</c>.
    /// </summary>
    public bool RequireCompileTimeRegistrations { get; set; } = !RuntimeFeature.IsDynamicCodeSupported;

    /// <summary>Registers the behavior that enforces <see cref="HandlerTimeoutAttribute"/>. Default <c>true</c>.</summary>
    public bool EnableTimeouts { get; set; } = true;

    /// <summary>
    /// The fixed messages DotCourier writes into error responses (unexpected error, validation,
    /// timeout, resilience rejections...). Replace them to translate or add codes, e.g.
    /// <c>cfg.Messages.UnexpectedError = new("Ocorreu um erro inesperado", code: "000");</c>
    /// </summary>
    public CourierMessages Messages { get; set; } = new();

    internal List<Assembly> AssembliesToRegister { get; } = [];

    /// <summary>
    /// The assemblies registered for scanning so far. Extension packages read it inside
    /// <see cref="ConfigureServices"/>, which runs after configuration is complete.
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies => AssembliesToRegister;

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
    public CourierConfiguration AddOpenBehavior([DynamicallyAccessedMembers(OpenBehavior)] Type openBehaviorType, int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
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
    public CourierConfiguration AddBehavior<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TImplementation : TService =>
        AddBehavior(typeof(TService), typeof(TImplementation), order, lifetime);

    /// <summary>Adds a closed behavior, registered for every <see cref="IPipelineBehavior{TRequest,TResponse}"/> it implements.</summary>
    public CourierConfiguration AddBehavior<[DynamicallyAccessedMembers(OpenBehavior)] TImplementation>(int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
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

    /// <summary>
    /// Adds an open generic stream behavior such as <c>typeof(AuditStreamBehavior&lt;,&gt;)</c> for every
    /// stream request.
    /// </summary>
    /// <param name="openBehaviorType">An open generic type implementing <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/>.</param>
    /// <param name="order">Position in the stream pipeline; see <see cref="BehaviorOrder"/>.</param>
    /// <param name="lifetime">Behavior lifetime.</param>
    public CourierConfiguration AddOpenStreamBehavior([DynamicallyAccessedMembers(OpenBehavior)] Type openBehaviorType, int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        if (!openBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException($"'{openBehaviorType.Name}' must be an open generic type.", nameof(openBehaviorType));
        }

        if (!openBehaviorType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamPipelineBehavior<,>)))
        {
            throw new ArgumentException($"'{openBehaviorType.Name}' must implement {nameof(IStreamPipelineBehavior<object, object>)}<,>.", nameof(openBehaviorType));
        }

        _behaviors.Add(new BehaviorRegistration(typeof(IStreamPipelineBehavior<,>), openBehaviorType, order, lifetime));
        return this;
    }

    /// <summary>Adds a stream behavior for one closed request/item pair (MediatR's <c>AddStreamBehavior</c>).</summary>
    public CourierConfiguration AddStreamBehavior<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TImplementation : TService =>
        AddBehavior(typeof(TService), typeof(TImplementation), order, lifetime);

    /// <summary>Adds a closed stream behavior for <paramref name="serviceType"/> (MediatR's <c>AddStreamBehavior</c>).</summary>
    public CourierConfiguration AddStreamBehavior(Type serviceType, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType, int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient) =>
        AddBehavior(serviceType, implementationType, order, lifetime);

    /// <summary>Adds a closed behavior for <paramref name="serviceType"/>.</summary>
    public CourierConfiguration AddBehavior(Type serviceType, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType, int order = BehaviorOrder.Default, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        _behaviors.Add(new BehaviorRegistration(serviceType, implementationType, order, lifetime));
        return this;
    }

    /// <summary>
    /// Lets compile-time mode (<see cref="RequireCompileTimeRegistrations"/>, always on under Native AOT)
    /// close the open behaviors your package adds with <see cref="AddOpenBehavior"/>. Extension packages call it
    /// next to <see cref="AddOpenBehavior"/>; application code rarely needs it, since DotCourier.SourceGenerator
    /// does it for the open behaviors declared in your assemblies.
    /// </summary>
    public CourierConfiguration AddBehaviorTypes(BehaviorTypes types)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (!_behaviorTypes.Contains(types))
        {
            _behaviorTypes.Add(types);
        }

        return this;
    }

    internal IReadOnlyList<BehaviorTypes> BehaviorTypeProviders => _behaviorTypes;

    internal readonly record struct BehaviorRegistration(
        Type ServiceType,
        [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type ImplementationType,
        int Order,
        ServiceLifetime Lifetime);
}
