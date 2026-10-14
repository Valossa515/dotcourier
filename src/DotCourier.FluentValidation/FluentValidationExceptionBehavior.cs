namespace DotCourier.FluentValidation;

/// <summary>
/// Turns a FluentValidation <see cref="global::FluentValidation.ValidationException"/> thrown by a
/// handler (e.g. via <c>ValidateAndThrowAsync</c>) into DotCourier's <see cref="ValidationException"/>,
/// so <c>Dispatch</c> answers 400 with the failures instead of a generic 500. It runs innermost,
/// right around the handler, so retry never mistakes an invalid request for a transient failure.
/// </summary>
internal sealed class FluentValidationExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (global::FluentValidation.ValidationException ex)
        {
            throw new ValidationException(ex.Errors.Select(FluentValidationErrors.Map));
        }
    }
}
