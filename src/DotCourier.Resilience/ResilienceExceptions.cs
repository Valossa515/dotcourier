namespace DotCourier.Resilience;

/// <summary>
/// The circuit for this request type is open, so the call was rejected without running the
/// handler. Maps to 503.
/// </summary>
public sealed class CircuitOpenException : CourierException
{
    /// <summary>Creates the exception.</summary>
    public CircuitOpenException(Type requestType, TimeSpan? retryAfter, Exception? innerException = null, CourierMessages? messages = null)
        : base((messages ?? CourierMessages.Default).CircuitOpen.Render(requestType), innerException, 503)
    {
        RequestType = requestType;
        RetryAfter = retryAfter;
        Code = (messages ?? CourierMessages.Default).CircuitOpen.Code;
    }

    /// <summary>The rejected request type.</summary>
    public Type RequestType { get; }

    /// <summary>Time until the circuit lets a trial request through, when known.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>The rate limit for this request type is exhausted. Maps to 429.</summary>
public sealed class RateLimitExceededException : CourierException
{
    /// <summary>Creates the exception.</summary>
    public RateLimitExceededException(Type requestType, TimeSpan? retryAfter, Exception? innerException = null, CourierMessages? messages = null)
        : base((messages ?? CourierMessages.Default).RateLimitExceeded.Render(requestType), innerException, 429)
    {
        RequestType = requestType;
        RetryAfter = retryAfter;
        Code = (messages ?? CourierMessages.Default).RateLimitExceeded.Code;
    }

    /// <summary>The rejected request type.</summary>
    public Type RequestType { get; }

    /// <summary>Time until permits are available again, when known.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>Too many executions of this request type are running at once. Maps to 503.</summary>
public sealed class BulkheadRejectedException : CourierException
{
    /// <summary>Creates the exception.</summary>
    public BulkheadRejectedException(Type requestType, Exception? innerException = null, CourierMessages? messages = null)
        : base((messages ?? CourierMessages.Default).BulkheadRejected.Render(requestType), innerException, 503)
    {
        RequestType = requestType;
        Code = (messages ?? CourierMessages.Default).BulkheadRejected.Code;
    }

    /// <summary>The rejected request type.</summary>
    public Type RequestType { get; }
}
