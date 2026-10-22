using DotCourier.FluentValidation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DotCourier;

/// <summary>Adds FluentValidation support to a DotCourier pipeline.</summary>
public static class CourierFluentValidationExtensions
{
    /// <summary>
    /// Runs every registered <see cref="IValidator{T}"/> in the validation step (together with
    /// DataAnnotations and <see cref="IRequestValidator{TRequest}"/>), and maps FluentValidation's
    /// <see cref="global::FluentValidation.ValidationException"/> thrown by handlers to a 400.
    /// Requires <see cref="CourierConfiguration.EnableValidation"/> (on by default).
    /// </summary>
    /// <param name="configuration">The courier configuration.</param>
    /// <param name="registerValidators">
    /// Also register the validators found in the assemblies given to <c>RegisterServicesFrom*</c>
    /// (transient). Generic validators such as <c>PageValidator&lt;T&gt; : AbstractValidator&lt;T&gt; where T : IPaged</c>
    /// are registered open and apply to every request that satisfies their constraints.
    /// Pass <c>false</c> if you register them yourself, e.g. with <c>AddValidatorsFromAssembly</c>.
    /// </param>
    public static CourierConfiguration AddFluentValidation(this CourierConfiguration configuration, bool registerValidators = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration
            .ConfigureServices(services =>
            {
                services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestValidator<>), typeof(FluentValidationRequestValidator<>)));
                var skipped = registerValidators ? RegisterValidators(services, configuration) : [];
                services.TryAddSingleton(sp => new SkippedValidators(skipped, sp.GetService<ILoggerFactory>()));
            })
            .AddOpenBehavior(typeof(FluentValidationExceptionBehavior<,>), int.MaxValue);
    }

    /// <returns>The generic validators the container cannot close, to be reported once.</returns>
    private static List<Type> RegisterValidators(IServiceCollection services, CourierConfiguration configuration)
    {
        var skipped = new List<Type>();
        var validators = configuration.Assemblies
            .SelectMany(a => a.DefinedTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var type in validators)
        {
            foreach (var @interface in type.ImplementedInterfaces)
            {
                if (!@interface.IsGenericType || @interface.GetGenericTypeDefinition() != typeof(IValidator<>))
                {
                    continue;
                }

                if (!type.IsGenericTypeDefinition)
                {
                    services.TryAddEnumerable(ServiceDescriptor.Transient(@interface, type));
                }
                else if (@interface.GetGenericArguments().SequenceEqual(type.GetGenericArguments()))
                {
                    // e.g. PageValidator<T> : AbstractValidator<T> where T : IPaged: the container closes it
                    // per request and skips the requests that do not satisfy the constraints.
                    services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IValidator<>), type));
                }
                else
                {
                    // e.g. ItemsValidator<T> : AbstractValidator<Batch<T>>: IValidator<X> cannot be mapped to it.
                    skipped.Add(type);
                }
            }
        }

        return skipped;
    }
}
