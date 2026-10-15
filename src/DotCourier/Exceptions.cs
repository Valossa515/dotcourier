using System.Text.Json.Serialization;
using DotCourier.Behaviors;

namespace DotCourier;

/// <summary>
/// Base exception whose message is safe to show to callers: <see cref="Response{T}"/>
/// propagates its <see cref="Exception.Message"/> and <see cref="StatusCode"/>, while every
/// other exception type is reduced to a generic message. Derive from it for business
/// errors you want to surface (e.g. not found, conflict).
/// </summary>
public class CourierException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Message shown to callers.</param>
    /// <param name="statusCode">HTTP-style status code for the error response.</param>
    public CourierException(string message, int statusCode = 500)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>Creates the exception with an inner cause (not exposed to callers).</summary>
    public CourierException(string message, Exception? innerException, int statusCode = 500)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP-style status code for the error response.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// Optional machine-readable error code, propagated to <see cref="Response{T}.ErrorCode"/>.
    /// Set it with an initializer: <c>throw new CourierException("...", 409) { Code = "STK-001" };</c>
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>No <see cref="IRequestHandler{TRequest,TResponse}"/> is registered for a request type.</summary>
public sealed class HandlerNotFoundException : CourierException
{
    /// <summary>Creates the exception for <paramref name="requestType"/> with the default message.</summary>
    public HandlerNotFoundException(Type requestType)
        : this(requestType, null)
    {
    }

    /// <summary>Creates the exception with the <see cref="CourierMessages.HandlerNotFound"/> message.</summary>
    public HandlerNotFoundException(Type requestType, CourierMessages? messages)
        : base((messages ?? CourierMessages.Default).HandlerNotFound.Render(requestType))
    {
        RequestType = requestType;
        Code = (messages ?? CourierMessages.Default).HandlerNotFound.Code;
    }

    /// <summary>The request type without a handler.</summary>
    public Type RequestType { get; }
}

/// <summary>A field-level validation failure.</summary>
/// <param name="Field">Name of the invalid member (empty for object-level errors).</param>
/// <param name="Message">Description of the failure.</param>
/// <param name="Code">Optional machine-readable code (e.g. a FluentValidation error code).</param>
public sealed record ValidationError(
    string Field,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code = null);

/// <summary>Request validation failed. Maps to a 400 response carrying <see cref="Errors"/> as details.</summary>
public sealed class ValidationException : CourierException
{
    /// <summary>Creates the exception with the default message.</summary>
    public ValidationException(IEnumerable<ValidationError> errors)
        : this(null, errors)
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    public ValidationException(string? message, IEnumerable<ValidationError> errors)
        : this(message, (errors ?? []).ToArray(), null, null)
    {
    }

    /// <summary>Creates the exception with the <see cref="CourierMessages.ValidationFailed"/> message.</summary>
    public ValidationException(IEnumerable<ValidationError> errors, CourierMessages? messages, Type? requestType = null)
        : this(null, (errors ?? []).ToArray(), messages, requestType)
    {
    }

    private ValidationException(string? message, ValidationError[] errors, CourierMessages? messages, Type? requestType)
        : base(message ?? (messages ?? CourierMessages.Default).ValidationFailed.Render(requestType, errors.Length), 400)
    {
        Errors = errors;
        if (message is null)
        {
            Code = (messages ?? CourierMessages.Default).ValidationFailed.Code;
        }
    }

    /// <summary>The validation failures.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }
}

/// <summary>A request annotated with <see cref="HandlerTimeoutAttribute"/> did not finish in time. Maps to 504.</summary>
public sealed class RequestTimeoutException : CourierException
{
    /// <summary>Creates the exception with the default message.</summary>
    public RequestTimeoutException(Type requestType, TimeSpan timeout)
        : this(requestType, timeout, null)
    {
    }

    /// <summary>Creates the exception with the <see cref="CourierMessages.RequestTimeout"/> message.</summary>
    public RequestTimeoutException(Type requestType, TimeSpan timeout, CourierMessages? messages)
        : base((messages ?? CourierMessages.Default).RequestTimeout.Render(requestType, timeout: timeout), 504)
    {
        RequestType = requestType;
        Timeout = timeout;
        Code = (messages ?? CourierMessages.Default).RequestTimeout.Code;
    }

    /// <summary>The request type that timed out.</summary>
    public Type RequestType { get; }

    /// <summary>The timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>A notification handler failed under <see cref="NotificationPublishStrategy.StopOnFirstError"/>.</summary>
public sealed class NotificationHandlerException : CourierException
{
    /// <summary>Creates the exception.</summary>
    public NotificationHandlerException(Type handlerType, Type notificationType, Exception innerException)
        : base($"Notification handler '{CourierDiagnostics.GetRequestName(handlerType)}' failed for '{CourierDiagnostics.GetRequestName(notificationType)}'", innerException)
    {
        HandlerType = handlerType;
        NotificationType = notificationType;
    }

    /// <summary>The handler that failed.</summary>
    public Type HandlerType { get; }

    /// <summary>The notification being published.</summary>
    public Type NotificationType { get; }
}

/// <summary>Thrown by <see cref="Response{T}.GetDataOrThrow"/> when the response is an error.</summary>
public sealed class ResponseException : CourierException
{
    /// <summary>Creates the exception.</summary>
    public ResponseException(string message, int statusCode)
        : base(message, statusCode)
    {
    }
}
