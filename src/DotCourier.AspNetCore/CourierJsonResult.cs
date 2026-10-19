using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotCourier;

/// <summary>
/// Writes a value as JSON with a status code, using <paramref name="serializerOptions"/> or else the application's
/// (<c>ConfigureHttpJsonOptions</c>). It asks those options for the type's metadata instead of serializing with
/// reflection, so it works under Native AOT once a source-generated context lists the type.
/// </summary>
internal sealed class CourierJsonResult<TValue>(TValue value, int statusCode, JsonSerializerOptions? serializerOptions = null)
    : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<TValue>
{
    private const string Justification = "Only for options without a TypeInfoResolver while reflection-based serialization is enabled, which it never is under Native AOT.";

    public int? StatusCode => statusCode;

    public TValue Value => value;

    object? IValueHttpResult.Value => value;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var options = serializerOptions
            ?? httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions
            ?? new JsonOptions().SerializerOptions;

        httpContext.Response.StatusCode = statusCode;
        return options.TypeInfoResolver is null && JsonSerializer.IsReflectionEnabledByDefault
            ? WriteWithReflection(httpContext, options)
            : httpContext.Response.WriteAsJsonAsync(value, (JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue)), contentType: null, httpContext.RequestAborted);
    }

    // GetTypeInfo throws on options without a resolver (e.g. new JsonSerializerOptions { ... } in your own code).
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    private Task WriteWithReflection(HttpContext httpContext, JsonSerializerOptions options) =>
        httpContext.Response.WriteAsJsonAsync(value, options, httpContext.RequestAborted);
}
