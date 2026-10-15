using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotCourier.Behaviors;
using DotCourier.Generated;

namespace DotCourier.Validation;

/// <summary>
/// Evaluates DataAnnotations on a request. Unlike <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}?, bool)"/>,
/// attributes placed on the parameters of a positional record (<c>record Cmd([Required] string Name)</c>)
/// are honoured too, since that is where C# puts them by default.
/// </summary>
internal static class DataAnnotationsValidator
{
    private static readonly ConcurrentDictionary<Type, TypeRules> Cache = new();

    /// <remarks>
    /// Validates the runtime type, as before; under trimming the members of <typeparamref name="T"/> (the request
    /// type of the pipeline) are the ones guaranteed to survive, which covers sealed request records.
    /// </remarks>
    public static List<ValidationError> Validate<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] T>(T instance)
        where T : notnull
    {
        var errors = new List<ValidationError>();
        var rules = instance.GetType() == typeof(T) ? RulesOf(typeof(T)) : RulesOfRuntimeType(instance);

        foreach (var (property, attributes) in rules.Properties)
        {
            var value = property.GetValue(instance);
            var context = Context(instance, property.Name);
            context.MemberName = property.Name;
            foreach (var attribute in attributes)
            {
                var result = attribute.GetValidationResult(value, context);
                if (result != ValidationResult.Success && result is not null)
                {
                    errors.Add(new ValidationError(property.Name, result.ErrorMessage ?? $"The field {property.Name} is invalid."));
                }
            }
        }

        // Object-level rules only make sense once every field is individually valid,
        // which matches how Validator.TryValidateObject behaves.
        if (errors.Count > 0)
        {
            return errors;
        }

        var objectContext = Context(instance, CourierDiagnostics.GetRequestName(instance.GetType()));
        foreach (var attribute in rules.TypeAttributes)
        {
            var result = attribute.GetValidationResult(instance, objectContext);
            if (result != ValidationResult.Success && result is not null)
            {
                AddResult(errors, result);
            }
        }

        if (instance is IValidatableObject validatable)
        {
            foreach (var result in validatable.Validate(objectContext))
            {
                if (result != ValidationResult.Success)
                {
                    AddResult(errors, result);
                }
            }
        }

        return errors;
    }

    private static void AddResult(List<ValidationError> errors, ValidationResult result)
    {
        var message = result.ErrorMessage ?? "The request is invalid.";
        var members = result.MemberNames.ToArray();
        if (members.Length == 0)
        {
            errors.Add(new ValidationError(string.Empty, message));
            return;
        }

        foreach (var member in members)
        {
            errors.Add(new ValidationError(member, message));
        }
    }

    private static TypeRules RulesOf([DynamicallyAccessedMembers(CompileTimeTypes.Request)] Type type) =>
        Cache.TryGetValue(type, out var rules) ? rules : Cache.GetOrAdd(type, BuildRules(type));

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "A request derived from the pipeline's request type; only the declared type's members are guaranteed under trimming.")]
    private static TypeRules RulesOfRuntimeType(object instance) => RulesOf(instance.GetType());

    /// <summary>A context with an explicit display name, so it does not look the instance's type up with reflection.</summary>
#if NET9_0_OR_GREATER
    private static ValidationContext Context(object instance, string displayName) => new(instance, displayName, null, null);
#else
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The request type's properties are kept (DynamicallyAccessedMembers on the pipeline's request type) and the display name is set right away.")]
    private static ValidationContext Context(object instance, string displayName) => new(instance) { DisplayName = displayName };
#endif

    private static TypeRules BuildRules([DynamicallyAccessedMembers(CompileTimeTypes.Request)] Type type)
    {
        var parameterAttributes = type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => p.Name is not null)
            .GroupBy(p => p.Name!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(p => p.GetCustomAttributes<ValidationAttribute>(true)).ToArray(),
                StringComparer.Ordinal);

        var properties = new List<(PropertyInfo, ValidationAttribute[])>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var attributes = property.GetCustomAttributes<ValidationAttribute>(true).ToList();
            if (parameterAttributes.TryGetValue(property.Name, out var fromParameter))
            {
                attributes.AddRange(fromParameter.Where(a => !attributes.Any(existing => existing.GetType() == a.GetType())));
            }

            if (attributes.Count > 0)
            {
                // Required first, so a missing value reports "required" before format rules.
                properties.Add((property, attributes.OrderBy(a => a is RequiredAttribute ? 0 : 1).ToArray()));
            }
        }

        return new TypeRules(properties.ToArray(), type.GetCustomAttributes<ValidationAttribute>(true).ToArray());
    }

    private sealed record TypeRules((PropertyInfo Property, ValidationAttribute[] Attributes)[] Properties, ValidationAttribute[] TypeAttributes);
}
