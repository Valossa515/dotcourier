using DotCourier.Validation;

namespace DotCourier.Behaviors;

/// <summary>
/// Validates the request with DataAnnotations (unless disabled) and every registered
/// <see cref="IRequestValidator{TRequest}"/>, throwing a <see cref="ValidationException"/>
/// (400) with all failures before the handler runs.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IRequestValidator<TRequest>> validators,
    CourierOptions? options = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly bool _useDataAnnotations = options?.UseDataAnnotations ?? true;

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        await RequestValidation.ThrowIfInvalidAsync(request, validators, _useDataAnnotations, options, cancellationToken).ConfigureAwait(false);
        return await next(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The validation shared by requests and streams.</summary>
internal static class RequestValidation
{
    public static async Task ThrowIfInvalidAsync<TRequest>(
        TRequest request,
        IEnumerable<IRequestValidator<TRequest>> validators,
        bool useDataAnnotations,
        CourierOptions? options,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        var errors = useDataAnnotations ? DataAnnotationsValidator.Validate(request) : [];

        foreach (var validator in validators)
        {
            errors.AddRange(await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors, options?.Messages, typeof(TRequest));
        }
    }
}
