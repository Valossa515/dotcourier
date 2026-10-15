using DotCourier;
using DotCourier.Behaviors;
using DotCourier.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers DotCourier in an <see cref="IServiceCollection"/>.</summary>
public static class CourierServiceCollectionExtensions
{
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
    /// and <see cref="IPublisher"/>), scans the configured assemblies and wires the pipeline.
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

        services.TryAddSingleton(new CourierOptions
        {
            NotificationPublishStrategy = configuration.NotificationPublishStrategy,
            UseDataAnnotations = configuration.UseDataAnnotations,
            Messages = configuration.Messages ?? CourierMessages.Default,
        });

        services.TryAdd(new ServiceDescriptor(typeof(ICourier), typeof(Courier), configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IMediator), sp => sp.GetRequiredService<ICourier>(), configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<ICourier>(), configuration.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<ICourier>(), configuration.Lifetime));

        var openRequestHandlers = new List<Type>();
        foreach (var assembly in configuration.AssembliesToRegister)
        {
            RegisterAssembly(services, assembly, configuration.Lifetime, openRequestHandlers);
        }

        services.TryAddSingleton(new GenericRequestHandlerRegistry(openRequestHandlers.Distinct().ToArray()));

        foreach (var register in configuration.ServiceRegistrations)
        {
            register(services);
        }

        RegisterBehaviors(services, configuration);
        return services;
    }

    private static void RegisterAssembly(IServiceCollection services, System.Reflection.Assembly assembly, ServiceLifetime lifetime, List<Type> openRequestHandlers)
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
                if (definition == typeof(IRequestHandler<,>))
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

    private static void RegisterOpenGeneric(IServiceCollection services, System.Reflection.TypeInfo type, ServiceLifetime lifetime, List<Type> openRequestHandlers)
    {
        var parameters = type.GenericTypeParameters;
        foreach (var @interface in type.ImplementedInterfaces.Where(i => i.IsGenericType))
        {
            var definition = @interface.GetGenericTypeDefinition();
            if (definition == typeof(IRequestHandler<,>))
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

    private static void RegisterBehaviors(IServiceCollection services, CourierConfiguration configuration)
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
        }

        if (configuration.EnableTelemetry)
        {
            builtIn.Add(Open(typeof(TelemetryBehavior<,>), BehaviorOrder.Telemetry));
        }

        if (configuration.EnableValidation)
        {
            builtIn.Add(Open(typeof(ValidationBehavior<,>), BehaviorOrder.Validation));
        }

        if (configuration.EnableTimeouts)
        {
            builtIn.Add(Open(typeof(TimeoutBehavior<,>), BehaviorOrder.Timeout));
        }

        // The container resolves IEnumerable<IPipelineBehavior<,>> in registration order and the
        // first entry is the outermost, so registering sorted by order fixes the pipeline shape.
        // OrderBy is stable: equal orders keep the order they were added in.
        foreach (var behavior in builtIn.Concat(configuration.Behaviors).OrderBy(b => b.Order))
        {
            services.TryAddEnumerable(new ServiceDescriptor(behavior.ServiceType, behavior.ImplementationType, behavior.Lifetime));
        }
    }

    private static CourierConfiguration.BehaviorRegistration Open(Type behaviorType, int order) =>
        new(typeof(IPipelineBehavior<,>), behaviorType, order, ServiceLifetime.Transient);
}
