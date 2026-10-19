using System.Text.Json;
using System.Text.Json.Serialization;
using DotCourier.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCourier.AspNetCore
{
    /// <summary>What the HTTP body of a <see cref="Response{T}"/> looks like.</summary>
    public enum ResponseBodyFormat
    {
        /// <summary>The whole <see cref="Response{T}"/>: <c>{ "data", "error", "errorCode", "success", "statusCode", "errorDetails" }</c>. Default.</summary>
        Envelope,

        /// <summary>
        /// Success: only the data (204 when there is none). Failure: RFC 7807 problem details, with the error code in
        /// <c>code</c> and, for a validation failure, the messages per field in <c>errors</c>.
        /// </summary>
        DataOrProblemDetails,

        /// <summary>
        /// Success: only the data (204 when there is none). Failure: a list of
        /// <c>{ "code", "message", "field" }</c>, one per validation error, or a single one otherwise.
        /// </summary>
        DataOrErrorList,
    }

    /// <summary>Options for <c>cfg.AddResponseConverter(...)</c>.</summary>
    public sealed class CourierResponseOptions
    {
        /// <summary>The body format. Default <see cref="ResponseBodyFormat.Envelope"/>.</summary>
        public ResponseBodyFormat Format { get; set; } = ResponseBodyFormat.Envelope;

        /// <summary>
        /// JSON options for the bodies (naming, ignored nulls, enum names, your own converters). Default <c>null</c>:
        /// the application's (<c>ConfigureHttpJsonOptions</c> for Minimal APIs, <c>AddJsonOptions</c> for MVC). Under
        /// Native AOT, their <c>TypeInfoResolver</c> must know the bodies' types.
        /// </summary>
        public JsonSerializerOptions? SerializerOptions { get; set; }
    }

    /// <summary>One error in the <see cref="ResponseBodyFormat.DataOrErrorList"/> format.</summary>
    /// <param name="Code">Machine-readable code (the validation error's, or the response's).</param>
    /// <param name="Message">Human-readable message.</param>
    /// <param name="Field">The invalid field, for validation errors.</param>
    public sealed record ErrorItem(
        string? Code,
        string Message,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field = null);

    /// <summary>
    /// Converts <see cref="Response{T}"/> into HTTP results in the configured <see cref="ResponseBodyFormat"/>, for MVC
    /// controllers (<see cref="ToActionResult{T}"/>) and Minimal APIs (<see cref="ToResult{T}"/>). Register it with
    /// <c>cfg.AddResponseConverter(...)</c> and inject it.
    /// </summary>
    public interface IResponseConverter
    {
        /// <summary>
        /// The MVC result for <paramref name="response"/>: its status code and body, a file for a
        /// <see cref="FileResponse"/>, <c>204</c> on success when <paramref name="includeBody"/> is <c>false</c>, and
        /// <c>500</c> when <paramref name="response"/> is <c>null</c>.
        /// </summary>
        IActionResult ToActionResult<T>(Response<T>? response, bool includeBody = true);

        /// <summary>The Minimal API counterpart of <see cref="ToActionResult{T}"/>.</summary>
        IResult ToResult<T>(Response<T>? response, bool includeBody = true);
    }
}

namespace DotCourier
{
    /// <summary>Registers <see cref="IResponseConverter"/>.</summary>
    public static class CourierResponseConverterExtensions
    {
        /// <summary>
        /// Registers <see cref="IResponseConverter"/> with the body format and JSON options of your API, e.g.
        /// <c>cfg.AddResponseConverter(o =&gt; o.Format = ResponseBodyFormat.DataOrProblemDetails)</c>.
        /// </summary>
        public static CourierConfiguration AddResponseConverter(this CourierConfiguration configuration, Action<CourierResponseOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var options = new CourierResponseOptions();
            configure?.Invoke(options);

            return configuration.ConfigureServices(services =>
            {
                services.TryAddSingleton(options);
                services.TryAddSingleton<IResponseConverter, ResponseConverter>();
            });
        }

        /// <summary>Awaits <paramref name="response"/> and converts it: <c>converter.ToActionResult(courier.Dispatch(command))</c>.</summary>
        public static async Task<IActionResult> ToActionResult<T>(this IResponseConverter converter, Task<Response<T>> response, bool includeBody = true)
        {
            ArgumentNullException.ThrowIfNull(converter);
            ArgumentNullException.ThrowIfNull(response);
            return converter.ToActionResult(await response.ConfigureAwait(false), includeBody);
        }

        /// <summary>Awaits <paramref name="response"/> and converts it: <c>converter.ToResult(courier.Dispatch(query))</c>.</summary>
        public static async Task<IResult> ToResult<T>(this IResponseConverter converter, Task<Response<T>> response, bool includeBody = true)
        {
            ArgumentNullException.ThrowIfNull(converter);
            ArgumentNullException.ThrowIfNull(response);
            return converter.ToResult(await response.ConfigureAwait(false), includeBody);
        }
    }
}
