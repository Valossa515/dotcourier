using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DotCourier.AspNetCore;

/// <summary>Default <see cref="IResponseConverter"/>.</summary>
internal sealed class ResponseConverter(CourierResponseOptions options, CourierOptions? courierOptions = null) : IResponseConverter
{
    public IActionResult ToActionResult<T>(Response<T>? response, bool includeBody = true)
    {
        response ??= Missing<T>();
        if (ResultBodies.SpecialAction(response, includeBody) is { } special)
        {
            return special;
        }

        return options.Format switch
        {
            ResponseBodyFormat.Envelope => Json(response, response.StatusCode),
            _ when response.IsSuccess => ResultBodies.IsEmpty(response.Data) ? new NoContentResult() : Json(response.Data, response.StatusCode),
            ResponseBodyFormat.DataOrProblemDetails => new ObjectResult(ResultBodies.Problem(response))
            {
                StatusCode = response.StatusCode,
                ContentTypes = { "application/problem+json" },
            },
            _ => Json(ResultBodies.Errors(response), response.StatusCode),
        };
    }

    public IResult ToResult<T>(Response<T>? response, bool includeBody = true)
    {
        response ??= Missing<T>();
        if (ResultBodies.SpecialResult(response, includeBody) is { } special)
        {
            return special;
        }

        return options.Format switch
        {
            ResponseBodyFormat.Envelope => new CourierJsonResult<Response<T>>(response, response.StatusCode, options.SerializerOptions),
            _ when response.IsSuccess => ResultBodies.IsEmpty(response.Data)
                ? TypedResults.NoContent()
                : new CourierJsonResult<T>(response.Data!, response.StatusCode, options.SerializerOptions),
            ResponseBodyFormat.DataOrProblemDetails => ResultBodies.ProblemResult(response),
            _ => new CourierJsonResult<IReadOnlyList<ErrorItem>>(ResultBodies.Errors(response), response.StatusCode, options.SerializerOptions),
        };
    }

    /// <summary>MVC: the application's formatters, or a JSON result with the configured options.</summary>
    private IActionResult Json(object? body, int statusCode) =>
        options.SerializerOptions is { } serializer
            ? new JsonResult(body, serializer) { StatusCode = statusCode }
            : new ObjectResult(body) { StatusCode = statusCode };

    /// <summary>A handler (or a caller) produced no response: answered like an unexpected error.</summary>
    private Response<T> Missing<T>()
    {
        var message = (courierOptions?.Messages ?? CourierMessages.Default).UnexpectedError;
        return Response<T>.Failure(message.Render(typeof(T)), StatusCodes.Status500InternalServerError, code: message.Code);
    }
}
