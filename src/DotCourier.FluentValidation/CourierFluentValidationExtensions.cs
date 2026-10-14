using DotCourier.FluentValidation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// (transient). Pass <c>false</c> if you register them yourself, e.g. with <c>AddValidatorsFromAssembly</c>.
    /// </param>
    public static CourierConfiguration AddFluentValidation(this CourierConfiguration configuration, bool registerValidators = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration
            .ConfigureServices(services =>
            {
                services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IRequestValidator<>), typeof(FluentValidationRequestValidator<>)));
                if (registerValidators)
                {
                    RegisterValidators(services, configuration);
                }
            })
            .AddOpenBehavior(typeof(FluentValidationExceptionBehavior<,>), int.MaxValue);
    }

    private static void RegisterValidators(IServiceCollection services, CourierConfiguration configuration)
    {
        var validators = configuration.Assemblies
            .SelectMany(a => a.DefinedTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false });

        foreach (var type in validators)
        {
            foreach (var @interface in type.ImplementedInterfaces)
            {
                if (@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IValidator<>))
                {
                    services.TryAddEnumerable(ServiceDescriptor.Transient(@interface, type));
                }
            }
        }
    }
}
