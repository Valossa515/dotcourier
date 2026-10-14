using FluentValidation;
using FluentValidation.Results;

namespace DotCourier.FluentValidation;

/// <summary>
/// Runs every <see cref="IValidator{T}"/> registered for <typeparamref name="TRequest"/> as part of
/// DotCourier's validation behavior, so FluentValidation and DataAnnotations failures end up in
/// the same 400 response.
/// </summary>
internal sealed class FluentValidationRequestValidator<TRequest>(IEnumerable<IValidator<TRequest>> validators)
    : IRequestValidator<TRequest>
{
    public async ValueTask<IEnumerable<ValidationError>> ValidateAsync(TRequest request, CancellationToken cancellationToken)
    {
        List<ValidationError>? errors = null;
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                (errors ??= []).AddRange(result.Errors.Select(FluentValidationErrors.Map));
            }
        }

        return errors ?? [];
    }
}

internal static class FluentValidationErrors
{
    public static ValidationError Map(ValidationFailure failure) =>
        new(failure.PropertyName, failure.ErrorMessage, string.IsNullOrEmpty(failure.ErrorCode) ? null : failure.ErrorCode);
}
