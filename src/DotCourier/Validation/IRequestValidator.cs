namespace DotCourier;

/// <summary>
/// Custom validation for <typeparamref name="TRequest"/>, run by the validation behavior
/// alongside DataAnnotations. Implementations found by assembly scanning are registered
/// automatically.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
public interface IRequestValidator<in TRequest>
{
    /// <summary>Returns the validation failures; empty when the request is valid.</summary>
    ValueTask<IEnumerable<ValidationError>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}
