using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotCourier;

/// <summary>
/// Writes a <see cref="Response{T}"/> as JSON with its status code, using the application's JSON options
/// (<c>ConfigureHttpJsonOptions</c>). It asks those options for the type's metadata instead of serializing
/// with reflection, so it works under Native AOT once a source-generated context lists <c>Response&lt;T&gt;</c>.
/// </summary>
internal sealed class CourierJsonResult<T>(Response<T> response) : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<Response<T>>
{
    public int? StatusCode => response.StatusCode;

    public Response<T> Value => response;

    object? IValueHttpResult.Value => response;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var options = httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions
            ?? new JsonOptions().SerializerOptions;
        var typeInfo = (JsonTypeInfo<Response<T>>)options.GetTypeInfo(typeof(Response<T>));

        httpContext.Response.StatusCode = response.StatusCode;
        return httpContext.Response.WriteAsJsonAsync(response, typeInfo, contentType: null, httpContext.RequestAborted);
    }
}
