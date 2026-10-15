using System.Text.Json.Serialization;

namespace DotCourier;

/// <summary>
/// Immutable result of an operation: optional data, an error message, a success flag
/// and an HTTP-style status code. Serializes to
/// <c>{ "data", "error", "errorCode"?, "success", "statusCode", "errorDetails"? }</c>.
/// </summary>
/// <typeparam name="T">Payload type.</typeparam>
public sealed class Response<T> : IResponseStatus, IEquatable<Response<T>>
{
    internal const string GenericErrorMessage = "An internal error occurred";

    /// <summary>Deserialization constructor. Prefer the static factories.</summary>
    [JsonConstructor]
    public Response(T? data, string? error, bool isSuccess, int statusCode, object? errorDetails = null, string? errorCode = null)
        : this(data, error, isSuccess, statusCode, false, null, errorDetails, errorCode)
    {
    }

    private Response(T? data, string? error, bool isSuccess, int statusCode,
        bool isValidationFailure, string? exceptionType, object? errorDetails, string? errorCode = null)
    {
        Data = data;
        Error = error;
        ErrorCode = errorCode;
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        IsValidationFailure = isValidationFailure;
        ExceptionType = exceptionType;
        ErrorDetails = errorDetails;
    }

    /// <summary>The payload (default on errors).</summary>
    [JsonPropertyName("data")]
    public T? Data { get; }

    /// <summary>The error message, or <c>null</c> on success.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; }

    /// <summary>Optional machine-readable error code (e.g. <c>"ORD-404"</c>), for clients that branch on errors.</summary>
    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; }

    /// <summary>Whether the operation succeeded.</summary>
    [JsonPropertyName("success")]
    public bool IsSuccess { get; }

    /// <summary>HTTP-style status code.</summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; }

    /// <summary>Structured error payload (e.g. the <see cref="ValidationError"/> list), if any.</summary>
    [JsonPropertyName("errorDetails")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ErrorDetails { get; }

    /// <summary>Whether this error came from request validation.</summary>
    [JsonIgnore]
    public bool IsValidationFailure { get; }

    /// <summary>Simple name of the exception behind this error, for metrics and logs.</summary>
    [JsonIgnore]
    public string? ExceptionType { get; }

    /// <summary>Whether <see cref="Data"/> is not null.</summary>
    [JsonIgnore]
    public bool HasData => Data is not null;

    /// <summary>Whether <see cref="Error"/> is not null.</summary>
    [JsonIgnore]
    public bool HasError => Error is not null;

    /// <summary>Whether <see cref="ErrorDetails"/> is not null.</summary>
    [JsonIgnore]
    public bool HasErrorDetails => ErrorDetails is not null;

    /// <summary>Returns <see cref="ErrorDetails"/> as <typeparamref name="TDetails"/>, or default when absent or of another type.</summary>
    public TDetails? GetErrorDetails<TDetails>() => ErrorDetails is TDetails details ? details : default;

    /// <summary>Returns <see cref="Data"/>, or throws a <see cref="ResponseException"/> when this is an error.</summary>
    public T? GetDataOrThrow()
    {
        if (!IsSuccess)
        {
            throw new ResponseException(Error ?? GenericErrorMessage, StatusCode) { Code = ErrorCode };
        }

        return Data;
    }

    /// <summary>A successful response.</summary>
    public static Response<T> Success(T? data, int statusCode = 200) =>
        new(data, null, true, statusCode, false, null, null);

    /// <summary>An error response.</summary>
    public static Response<T> Failure(string message, int statusCode = 500, object? details = null, string? code = null) =>
        new(default, message, false, statusCode, false, null, details, code);

    /// <summary>A validation error response (400 by default), flagged by <see cref="IsValidationFailure"/>.</summary>
    public static Response<T> ValidationFailure(string message, object? details = null, int statusCode = 400) =>
        new(default, message, false, statusCode, true, nameof(ValidationException), details);

    /// <summary>
    /// An error response built from an exception. Only <see cref="CourierException"/>
    /// messages and status codes are propagated; anything else becomes a generic 500.
    /// </summary>
    public static Response<T> FromException(Exception exception) => FromException(exception, null);

    /// <summary>
    /// An error response built from an exception, using <paramref name="messages"/> for the
    /// generic message of non-<see cref="CourierException"/> errors.
    /// </summary>
    public static Response<T> FromException(Exception exception, CourierMessages? messages)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ValidationException v => new(default, v.Message, false, v.StatusCode, true, nameof(ValidationException), v.Errors, v.Code),
            CourierException c => new(default, c.Message, false, c.StatusCode, false, c.GetType().Name, null, c.Code),
            _ => Unexpected(exception, (messages ?? CourierMessages.Default).UnexpectedError),
        };
    }

    private static Response<T> Unexpected(Exception exception, CourierMessage message) =>
        new(default, message.Render(), false, 500, false, exception.GetType().Name, null, message.Code);

    /// <summary>Wraps a value in a successful response.</summary>
    public static implicit operator Response<T>(T value) => Success(value);

    /// <summary>Converts a <see cref="ResponseError"/> (from <see cref="Response.Failure"/> and friends) into an error response.</summary>
    public static implicit operator Response<T>(ResponseError error) => Failure(error.Message, error.StatusCode, error.Details, error.Code);

    /// <summary>Converts an <see cref="EmptyResponse"/> (from <see cref="Response.NoContent"/>) into a successful response without data.</summary>
    public static implicit operator Response<T>(EmptyResponse empty) => Success(default, empty.StatusCode);

    /// <inheritdoc />
    public bool Equals(Response<T>? other) =>
        other is not null
        && IsSuccess == other.IsSuccess
        && StatusCode == other.StatusCode
        && IsValidationFailure == other.IsValidationFailure
        && EqualityComparer<T?>.Default.Equals(Data, other.Data)
        && Error == other.Error
        && ErrorCode == other.ErrorCode
        && Equals(ErrorDetails, other.ErrorDetails);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Response<T>);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Data, Error, ErrorCode, IsSuccess, StatusCode, IsValidationFailure, ErrorDetails);

    /// <summary>Describes the response without printing <see cref="Data"/>, which may hold sensitive values.</summary>
    public override string ToString() =>
        $"Response {{ Data = {(HasData ? "<present>" : "null")}, Error = {Error}, ErrorCode = {ErrorCode}, Success = {IsSuccess}, StatusCode = {StatusCode}, ExceptionType = {ExceptionType} }}";
}

/// <summary>
/// Non-generic view of a <see cref="Response{T}"/>, so behaviors can inspect the outcome of
/// handlers that return responses without knowing the payload type.
/// </summary>
public interface IResponseStatus
{
    /// <summary>Whether the operation succeeded.</summary>
    bool IsSuccess { get; }

    /// <summary>HTTP-style status code.</summary>
    int StatusCode { get; }

    /// <summary>The error message, or <c>null</c> on success.</summary>
    string? Error { get; }

    /// <summary>Simple name of the exception behind the error, if any.</summary>
    string? ExceptionType { get; }
}

/// <summary>An error not yet bound to a payload type; converts implicitly to any <see cref="Response{T}"/>.</summary>
/// <param name="Message">Error message.</param>
/// <param name="StatusCode">HTTP-style status code.</param>
/// <param name="Details">Optional structured error payload.</param>
/// <param name="Code">Optional machine-readable error code.</param>
public readonly record struct ResponseError(string Message, int StatusCode = 500, object? Details = null, string? Code = null);

/// <summary>A success without payload (e.g. 204); converts implicitly to any <see cref="Response{T}"/>.</summary>
/// <param name="StatusCode">HTTP-style status code.</param>
public readonly record struct EmptyResponse(int StatusCode = 204);

/// <summary>Factories with type inference for <see cref="Response{T}"/>.</summary>
public static class Response
{
    /// <summary>A successful response.</summary>
    public static Response<T> Success<T>(T? data, int statusCode = 200) => Response<T>.Success(data, statusCode);

    /// <summary>A successful response without payload.</summary>
    public static Response<Unit> Success() => Response<Unit>.Success(Unit.Value);

    /// <summary>A 201 response carrying the created resource.</summary>
    public static Response<T> Created<T>(T? data) => Response<T>.Success(data, 201);

    /// <summary>A 204 success without payload, convertible to any <see cref="Response{T}"/>.</summary>
    public static EmptyResponse NoContent() => new(204);

    /// <summary>An error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError Failure(string message, int statusCode = 500, object? details = null, string? code = null) =>
        new(message, statusCode, details, code);

    /// <summary>A 400 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError BadRequest(string message, object? details = null, string? code = null) => new(message, 400, details, code);

    /// <summary>A 401 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError Unauthorized(string message, string? code = null) => new(message, 401, null, code);

    /// <summary>A 403 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError Forbidden(string message, string? code = null) => new(message, 403, null, code);

    /// <summary>A 404 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError NotFound(string message, string? code = null) => new(message, 404, null, code);

    /// <summary>A 409 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError Conflict(string message, string? code = null) => new(message, 409, null, code);
}
