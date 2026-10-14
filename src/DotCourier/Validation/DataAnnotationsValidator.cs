using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace DotCourier.Validation;

/// <summary>
/// Evaluates DataAnnotations on a request. Unlike <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}?, bool)"/>,
/// attributes placed on the parameters of a positional record (<c>record Cmd([Required] string Name)</c>)
/// are honoured too, since that is where C# puts them by default.
/// </summary>
internal static class DataAnnotationsValidator
{
    private static readonly ConcurrentDictionary<Type, TypeRules> Cache = new();

    public static List<ValidationError> Validate(object instance)
    {
        var errors = new List<ValidationError>();
        var rules = Cache.GetOrAdd(instance.GetType(), BuildRules);

        foreach (var (property, attributes) in rules.Properties)
        {
            var value = property.GetValue(instance);
            var context = new ValidationContext(instance) { MemberName = property.Name, DisplayName = property.Name };
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

        var objectContext = new ValidationContext(instance);
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

    private static TypeRules BuildRules(Type type)
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
