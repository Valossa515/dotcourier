using FluentValidation;
using FluentValidation.Results;

namespace DotCourier.FluentValidation;

/// <summary>
/// Runs every <see cref="IValidator{T}"/> registered for <typeparamref name="TRequest"/> as part of
/// DotCourier's validation behavior, so FluentValidation and DataAnnotations failures end up in
/// the same 400 response.
/// </summary>
internal sealed class FluentValidationRequestValidator<TRequest> : IRequestValidator<TRequest>
{
    private readonly IValidator<TRequest>[] _validators;

    public FluentValidationRequestValidator(IEnumerable<IValidator<TRequest>> validators, SkippedValidators skipped)
    {
        ArgumentNullException.ThrowIfNull(skipped); // resolving it reports the skipped validators
        _validators = validators as IValidator<TRequest>[] ?? validators.ToArray();
    }

    public async ValueTask<IEnumerable<ValidationError>> ValidateAsync(TRequest request, CancellationToken cancellationToken)
    {
        List<ValidationError>? errors = null;
        foreach (var validator in _validators)
        {
            if (IsInheritedByAnother(validator))
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                (errors ??= []).AddRange(result.Errors.Select(FluentValidationErrors.Map));
            }
        }

        return errors ?? [];
    }

    /// <summary>
    /// A subclass already runs its base's rules: with both <c>PageValidator&lt;GetProducts&gt;</c> (open
    /// generic) and <c>GetProductsValidator : PageValidator&lt;GetProducts&gt;</c> registered, only the
    /// subclass runs, so the errors are not reported twice.
    /// </summary>
    private bool IsInheritedByAnother(IValidator<TRequest> validator)
    {
        if (_validators.Length < 2)
        {
            return false;
        }

        var type = validator.GetType();
        return Array.Exists(_validators, other => other.GetType().IsSubclassOf(type));
    }
}

internal static class FluentValidationErrors
{
    public static ValidationError Map(ValidationFailure failure) =>
        new(failure.PropertyName, failure.ErrorMessage, string.IsNullOrEmpty(failure.ErrorCode) ? null : failure.ErrorCode);
}
