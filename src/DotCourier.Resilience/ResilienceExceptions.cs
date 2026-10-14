namespace DotCourier.Resilience;

/// <summary>
/// The circuit for this request type is open, so the call was rejected without running the
/// handler. Maps to 503.
/// </summary>
public sealed class CircuitOpenException : CourierException
{
    /// <summary>Creates the exception.</summary>
    public CircuitOpenException(Type requestType, TimeSpan? retryAfter, Exception? innerException = null)
        : base("Service temporarily unavailable: the circuit is open for this operation", innerException, 503)
    {
        RequestType = requestType;
        RetryAfter = retryAfter;
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
    public RateLimitExceededException(Type requestType, TimeSpan? retryAfter, Exception? innerException = null)
        : base("Too many requests: the rate limit for this operation is exhausted", innerException, 429)
    {
        RequestType = requestType;
        RetryAfter = retryAfter;
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
    public BulkheadRejectedException(Type requestType, Exception? innerException = null)
        : base("Service busy: too many concurrent executions of this operation", innerException, 503)
    {
        RequestType = requestType;
    }

    /// <summary>The rejected request type.</summary>
    public Type RequestType { get; }
}
