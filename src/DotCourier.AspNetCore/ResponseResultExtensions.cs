using DotCourier.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DotCourier;

/// <summary>Converts <see cref="Response{T}"/> into ASP.NET Core results that carry its status code.</summary>
public static class ResponseResultExtensions
{
    /// <summary>
    /// Converts the response into a Minimal API result: the response as JSON body with
    /// <see cref="Response{T}.StatusCode"/>; a file download for a <see cref="FileResponse"/>; or <c>204 No Content</c>
    /// when the status is 204 (e.g. <see cref="Response.NoContent"/>) or <paramref name="includeBody"/> is <c>false</c>
    /// and the response succeeded. For other body formats, inject <see cref="IResponseConverter"/>.
    /// </summary>
    public static IResult ToResult<T>(this Response<T> response, bool includeBody = true)
    {
        ArgumentNullException.ThrowIfNull(response);
        return ResultBodies.SpecialResult(response, includeBody)
            ?? new CourierJsonResult<Response<T>>(response, response.StatusCode);
    }

    /// <summary>
    /// Awaits the response and converts it, so an endpoint can be written as
    /// <c>(ICourier c, ...) =&gt; c.Dispatch(query).ToResult()</c>.
    /// </summary>
    public static async Task<IResult> ToResult<T>(this Task<Response<T>> response, bool includeBody = true)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.ConfigureAwait(false)).ToResult(includeBody);
    }

    /// <summary>MVC counterpart of <see cref="ToResult{T}(Response{T}, bool)"/>.</summary>
    public static IActionResult ToActionResult<T>(this Response<T> response, bool includeBody = true)
    {
        ArgumentNullException.ThrowIfNull(response);
        return ResultBodies.SpecialAction(response, includeBody)
            ?? new ObjectResult(response) { StatusCode = response.StatusCode };
    }

    /// <summary>Awaits the response and converts it into an MVC result.</summary>
    public static async Task<IActionResult> ToActionResult<T>(this Task<Response<T>> response, bool includeBody = true)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.ConfigureAwait(false)).ToActionResult(includeBody);
    }
}
