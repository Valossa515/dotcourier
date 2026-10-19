using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DotCourier.AspNetCore;

/// <summary>The parts every format shares: no-content, files, and the shapes of an error.</summary>
internal static class ResultBodies
{
    /// <summary>Results that do not depend on the body format; <c>null</c> when the format decides.</summary>
    public static IActionResult? SpecialAction<T>(Response<T> response, bool includeBody)
    {
        if (IsNoContent(response, includeBody))
        {
            return new NoContentResult();
        }

        return response.IsSuccess switch
        {
            true when response.Data is FileResponse { Content: { } bytes } file => new FileContentResult(bytes, file.ContentType) { FileDownloadName = file.FileName ?? string.Empty },
            true when response.Data is FileResponse { Stream: { } stream } file => new FileStreamResult(stream, file.ContentType) { FileDownloadName = file.FileName ?? string.Empty },
            true when response.Data is IActionResult ready => ready,
            _ => null,
        };
    }

    /// <summary>Minimal API counterpart of <see cref="SpecialAction{T}"/>.</summary>
    public static IResult? SpecialResult<T>(Response<T> response, bool includeBody)
    {
        if (IsNoContent(response, includeBody))
        {
            return TypedResults.NoContent();
        }

        return response.IsSuccess switch
        {
            true when response.Data is FileResponse { Content: { } bytes } file => TypedResults.File(bytes, file.ContentType, file.FileName),
            true when response.Data is FileResponse { Stream: { } stream } file => TypedResults.Stream(stream, file.ContentType, file.FileName),
            true when response.Data is IResult ready => ready,
            _ => null,
        };
    }

    /// <summary>Data that has nothing to send: answered with 204 by the data formats.</summary>
    public static bool IsEmpty<T>(T? data) => data is null or Unit;

    /// <summary>RFC 7807: validation failures become <c>errors</c> per field; the error code goes in <c>code</c>.</summary>
    public static ProblemDetails Problem<T>(Response<T> response)
    {
        ProblemDetails problem = response.ErrorDetails is IEnumerable<ValidationError> validation
            ? new HttpValidationProblemDetails(validation
                .GroupBy(e => e.Field, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray(), StringComparer.Ordinal))
            : new ProblemDetails();
        problem.Status = response.StatusCode;
        problem.Title = response.Error;
        if (response.ErrorCode is { } code)
        {
            problem.Extensions["code"] = code;
        }

        return problem;
    }

    public static ProblemHttpResult ProblemResult<T>(Response<T> response) => TypedResults.Problem(Problem(response));

    /// <summary>One item per validation error (with its own code, else the response's), or a single item.</summary>
    public static IReadOnlyList<ErrorItem> Errors<T>(Response<T> response) =>
        response.ErrorDetails is IEnumerable<ValidationError> validation && validation.Any()
            ? validation.Select(e => new ErrorItem(e.Code ?? response.ErrorCode, e.Message, e.Field)).ToList()
            : [new ErrorItem(response.ErrorCode, response.Error ?? "An internal error occurred")];

    private static bool IsNoContent<T>(Response<T> response, bool includeBody) =>
        response.StatusCode == StatusCodes.Status204NoContent || (!includeBody && response.IsSuccess);
}
