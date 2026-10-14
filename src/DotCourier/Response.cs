using System.Text.Json.Serialization;

namespace DotCourier;

/// <summary>
/// Immutable result of an operation: optional data, an error message, a success flag
/// and an HTTP-style status code. Serializes to
/// <c>{ "data", "error", "success", "statusCode", "errorDetails"? }</c>.
/// </summary>
/// <typeparam name="T">Payload type.</typeparam>
public sealed class Response<T> : IResponseStatus, IEquatable<Response<T>>
{
    internal const string GenericErrorMessage = "An internal error occurred";

    /// <summary>Deserialization constructor. Prefer the static factories.</summary>
    [JsonConstructor]
    public Response(T? data, string? error, bool isSuccess, int statusCode, object? errorDetails = null)
        : this(data, error, isSuccess, statusCode, false, null, errorDetails)
    {
    }

    private Response(T? data, string? error, bool isSuccess, int statusCode,
        bool isValidationFailure, string? exceptionType, object? errorDetails)
    {
        Data = data;
        Error = error;
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
            throw new ResponseException(Error ?? GenericErrorMessage, StatusCode);
        }

        return Data;
    }

    /// <summary>A successful response.</summary>
    public static Response<T> Success(T? data, int statusCode = 200) =>
        new(data, null, true, statusCode, false, null, null);

    /// <summary>An error response.</summary>
    public static Response<T> Failure(string message, int statusCode = 500, object? details = null) =>
        new(default, message, false, statusCode, false, null, details);

    /// <summary>A validation error response (400 by default), flagged by <see cref="IsValidationFailure"/>.</summary>
    public static Response<T> ValidationFailure(string message, object? details = null, int statusCode = 400) =>
        new(default, message, false, statusCode, true, nameof(ValidationException), details);

    /// <summary>
    /// An error response built from an exception. Only <see cref="CourierException"/>
    /// messages and status codes are propagated; anything else becomes a generic 500.
    /// </summary>
    public static Response<T> FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ValidationException v => new(default, v.Message, false, v.StatusCode, true, nameof(ValidationException), v.Errors),
            CourierException c => new(default, c.Message, false, c.StatusCode, false, c.GetType().Name, null),
            _ => new(default, GenericErrorMessage, false, 500, false, exception.GetType().Name, null),
        };
    }

    /// <summary>Wraps a value in a successful response.</summary>
    public static implicit operator Response<T>(T value) => Success(value);

    /// <summary>Converts a <see cref="ResponseError"/> (from <see cref="Response.Failure"/> and friends) into an error response.</summary>
    public static implicit operator Response<T>(ResponseError error) => Failure(error.Message, error.StatusCode, error.Details);

    /// <inheritdoc />
    public bool Equals(Response<T>? other) =>
        other is not null
        && IsSuccess == other.IsSuccess
        && StatusCode == other.StatusCode
        && IsValidationFailure == other.IsValidationFailure
        && EqualityComparer<T?>.Default.Equals(Data, other.Data)
        && Error == other.Error
        && Equals(ErrorDetails, other.ErrorDetails);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Response<T>);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Data, Error, IsSuccess, StatusCode, IsValidationFailure, ErrorDetails);

    /// <summary>Describes the response without printing <see cref="Data"/>, which may hold sensitive values.</summary>
    public override string ToString() =>
        $"Response {{ Data = {(HasData ? "<present>" : "null")}, Error = {Error}, Success = {IsSuccess}, StatusCode = {StatusCode}, ExceptionType = {ExceptionType} }}";
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
public readonly record struct ResponseError(string Message, int StatusCode = 500, object? Details = null);

/// <summary>Factories with type inference for <see cref="Response{T}"/>.</summary>
public static class Response
{
    /// <summary>A successful response.</summary>
    public static Response<T> Success<T>(T? data, int statusCode = 200) => Response<T>.Success(data, statusCode);

    /// <summary>A successful response without payload.</summary>
    public static Response<Unit> Success() => Response<Unit>.Success(Unit.Value);

    /// <summary>An error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError Failure(string message, int statusCode = 500, object? details = null) => new(message, statusCode, details);

    /// <summary>A 400 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError BadRequest(string message, object? details = null) => new(message, 400, details);

    /// <summary>A 404 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError NotFound(string message) => new(message, 404);

    /// <summary>A 409 error convertible to any <see cref="Response{T}"/>.</summary>
    public static ResponseError Conflict(string message) => new(message, 409);
}
