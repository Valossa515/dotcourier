using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotCourier;
using DotCourier.Behaviors;
using DotCourier.Generated;
using DotCourier.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers DotCourier in an <see cref="IServiceCollection"/>.</summary>
public static class CourierServiceCollectionExtensions
{
    private const DynamicallyAccessedMemberTypes OpenGenericMembers = DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicConstructors;

    private static readonly Type[] EnumerableServices =
    [
        typeof(INotificationHandler<>),
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>),
        typeof(IRequestExceptionHandler<,,>),
        typeof(IRequestValidator<>),
    ];

    /// <summary>
    /// Registers <see cref="ICourier"/> (also exposed as <see cref="IMediator"/>, <see cref="ISender"/>
    /// and <see cref="IPublisher"/>), scans the configured assemblies and wires the pipelines (requests
    /// and streams).
    /// </summary>
    public static IServiceCollection AddCourier(this IServiceCollection services, Action<CourierConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new CourierConfiguration();
        configure(configuration);

        if (configuration.AssembliesToRegister.Count == 0)
        {
            throw new ArgumentException(
                "No assemblies to scan. Call cfg.RegisterServicesFromAssemblyContaining<T>() or cfg.RegisterServicesFromAssembly(...).");
        }

        var compileTime = configuration.RequireCompileTimeRegistrations;
        services.TryAddSingleton(new CourierOptions
        {
            NotificationPublishStrategy = configuration.NotificationPublishStrategy,
            UseDataAnnotations = configuration.UseDataAnnotations,
            Messages = configuration.Messages ?? CourierMessages.Default,
            RequireCompileTimeRegistrations = compileTime,
        });

        services.TryAdd(new ServiceDescriptor(typeof(ICourier), typeof(Courier), configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IMediator), sp => sp.GetRequiredService<ICourier>(), configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<ICourier>(), configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<ICourier>(), configuration.Lifetime));

        var openRequestHandlers = new List<Type>();
        var knownRequests = new List<IKnownRequest>();
        var behaviorTypes = new List<BehaviorTypes> { CoreBehaviorTypes.Instance };
        foreach (var assembly in configuration.AssembliesToRegister)
        {
            // An assembly built with DotCourier.SourceGenerator describes its own handlers: no scanning.
            var generated = configuration.UseGeneratedRegistrations || compileTime ? GeneratedCourierRegistry.Find(assembly) : null;
            if (generated is null)
            {
                Scan(services, assembly, configuration.Lifetime, openRequestHandlers, compileTime);
                continue;
            }

            generated(new GeneratedRegistrationContext(
                services,
                configuration.Lifetime,
                () => Scan(services, assembly, configuration.Lifetime, openRequestHandlers, compileTime),
                openRequestHandlers,
                knownRequests,
                behaviorTypes,
                compileTime));
        }

        services.TryAddSingleton(new GenericRequestHandlerRegistry(compileTime ? [] : openRequestHandlers.Distinct().ToArray()));

        foreach (var register in configuration.ServiceRegistrations)
        {
            register(services);
        }

        RegisterBehaviors(services, configuration, compileTime ? (knownRequests, behaviorTypes.Concat(configuration.BehaviorTypeProviders).ToList()) : null);
        return services;
    }

    /// <summary>The reflection scan, for assemblies without generated registrations; never reached in compile-time mode.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Compile-time mode (always on under Native AOT) throws before scanning; scanning only runs where reflection is available.")]
    private static void Scan(IServiceCollection services, Assembly assembly, ServiceLifetime lifetime, List<Type> openRequestHandlers, bool compileTime)
    {
        if (compileTime)
        {
            throw new InvalidOperationException(
                $"The assembly '{assembly.GetName().Name}' has no compile-time registrations, which compile-time mode (and Native AOT) " +
                "requires: add the DotCourier.SourceGenerator package to that project. If it is already there, look for warning " +
                "DOTCOURIER001, which names the handler the generated code cannot reach.");
        }

        RegisterAssembly(services, assembly, lifetime, openRequestHandlers);
    }

    [RequiresUnreferencedCode("Scans the assembly's types with reflection; use DotCourier.SourceGenerator for trimmed and Native AOT applications.")]
    private static void RegisterAssembly(IServiceCollection services, Assembly assembly, ServiceLifetime lifetime, List<Type> openRequestHandlers)
    {
        foreach (var type in assembly.DefinedTypes.Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            if (type.IsGenericTypeDefinition)
            {
                RegisterOpenGeneric(services, type, lifetime, openRequestHandlers);
                continue;
            }

            if (type.ContainsGenericParameters)
            {
                continue;
            }

            foreach (var @interface in type.ImplementedInterfaces.Where(i => i.IsGenericType))
            {
                var definition = @interface.GetGenericTypeDefinition();
                if (definition == typeof(IRequestHandler<,>) || definition == typeof(IStreamRequestHandler<,>))
                {
                    // One handler per request: the first registration wins.
                    services.TryAdd(new ServiceDescriptor(@interface, type, lifetime));
                }
                else if (Array.IndexOf(EnumerableServices, definition) >= 0)
                {
                    services.TryAddEnumerable(new ServiceDescriptor(@interface, type, lifetime));
                }
            }
        }
    }

    internal static void RegisterOpenGeneric(IServiceCollection services, [DynamicallyAccessedMembers(OpenGenericMembers)] Type type, ServiceLifetime lifetime, List<Type> openRequestHandlers)
    {
        var parameters = type.GetGenericArguments();
        foreach (var @interface in type.GetInterfaces().Where(i => i.IsGenericType))
        {
            var definition = @interface.GetGenericTypeDefinition();
            if (definition == typeof(IRequestHandler<,>) || definition == typeof(IStreamRequestHandler<,>))
            {
                // e.g. CreateHandler<T> : IRequestHandler<Create<T>, T>: closed on first dispatch.
                openRequestHandlers.Add(type);
            }
            else if (Array.IndexOf(EnumerableServices, definition) >= 0
                && @interface.GetGenericArguments().SequenceEqual(parameters))
            {
                // e.g. AuditAll<T> : INotificationHandler<T>: the container closes these itself,
                // and skips them for types that do not satisfy the constraints.
                services.TryAddEnumerable(new ServiceDescriptor(definition, type, lifetime));
            }
        }
    }

    private static void RegisterBehaviors(
        IServiceCollection services,
        CourierConfiguration configuration,
        (List<IKnownRequest> Requests, List<BehaviorTypes> Types)? compileTime)
    {
        var builtIn = new List<CourierConfiguration.BehaviorRegistration>
        {
            Open(typeof(RequestExceptionProcessorBehavior<,>), BehaviorOrder.ExceptionHandlers),
            Open(typeof(RequestPreProcessorBehavior<,>), BehaviorOrder.PreProcessors),
            Open(typeof(RequestPostProcessorBehavior<,>), BehaviorOrder.PostProcessors),
        };
        if (configuration.EnableLogging)
        {
            builtIn.Add(Open(typeof(LoggingBehavior<,>), BehaviorOrder.Logging));
            builtIn.Add(OpenStream(typeof(StreamLoggingBehavior<,>), BehaviorOrder.Logging));
        }

        if (configuration.EnableTelemetry)
        {
            builtIn.Add(Open(typeof(TelemetryBehavior<,>), BehaviorOrder.Telemetry));
            builtIn.Add(OpenStream(typeof(StreamTelemetryBehavior<,>), BehaviorOrder.Telemetry));
        }

        if (configuration.EnableValidation)
        {
            builtIn.Add(Open(typeof(ValidationBehavior<,>), BehaviorOrder.Validation));
            builtIn.Add(OpenStream(typeof(StreamValidationBehavior<,>), BehaviorOrder.Validation));
        }

        if (configuration.EnableTimeouts)
        {
            builtIn.Add(Open(typeof(TimeoutBehavior<,>), BehaviorOrder.Timeout));
        }

        // The container resolves IEnumerable<IPipelineBehavior<,>> in registration order and the
        // first entry is the outermost, so registering sorted by order fixes the pipeline shape.
        // OrderBy is stable: equal orders keep the order they were added in.
        var behaviors = builtIn.Concat(configuration.Behaviors).OrderBy(b => b.Order).ToList();
        if (compileTime is not { } known)
        {
            foreach (var behavior in behaviors)
            {
                services.TryAddEnumerable(new ServiceDescriptor(behavior.ServiceType, behavior.ImplementationType, behavior.Lifetime));
            }

            return;
        }

        // Compile-time mode: the container must not close open generics, so close each behavior for each
        // request known at compile time, keeping the same order.
        var registration = new ClosedBehaviorRegistration(services, behaviors, known.Types);
        foreach (var request in known.Requests)
        {
            request.RegisterBehaviors(registration);
        }

        registration.RemainingClosedBehaviors();
    }

    private static CourierConfiguration.BehaviorRegistration Open([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type behaviorType, int order) =>
        new(typeof(IPipelineBehavior<,>), behaviorType, order, ServiceLifetime.Transient);

    private static CourierConfiguration.BehaviorRegistration OpenStream([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type behaviorType, int order) =>
        new(typeof(IStreamPipelineBehavior<,>), behaviorType, order, ServiceLifetime.Transient);
}
