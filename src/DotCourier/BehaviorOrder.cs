namespace DotCourier;

/// <summary>
/// Positions of the built-in behaviors in the pipeline (lower = outermost). Pass one of
/// these, or a value in between, as the <c>order</c> of your own behaviors to place them
/// relative to the built-ins. Behaviors with the same order keep registration order.
/// </summary>
/// <remarks>
/// Retry sits outside the transaction so each attempt gets a fresh one; the circuit
/// breaker sits outside retry so an open circuit is rejected once instead of retried; the
/// bulkhead sits inside retry so a permit is not held across backoff delays.
/// </remarks>
public static class BehaviorOrder
{
    /// <summary><see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> processing (outermost).</summary>
    public const int ExceptionHandlers = -2000;

    /// <summary><see cref="IRequestPreProcessor{TRequest}"/> processing.</summary>
    public const int PreProcessors = -1900;

    /// <summary><see cref="IRequestPostProcessor{TRequest,TResponse}"/> processing.</summary>
    public const int PostProcessors = -1800;

    /// <summary>Logging behavior.</summary>
    public const int Logging = -1000;

    /// <summary>Idempotency behavior (reserved).</summary>
    public const int Idempotency = -995;

    /// <summary>Tracing and metrics behavior.</summary>
    public const int Telemetry = -990;

    /// <summary>Caching behavior (reserved).</summary>
    public const int Caching = -950;

    /// <summary>Validation behavior.</summary>
    public const int Validation = -900;

    /// <summary>Rate limiter behavior (reserved).</summary>
    public const int RateLimiter = -870;

    /// <summary>Circuit breaker behavior (reserved).</summary>
    public const int CircuitBreaker = -860;

    /// <summary>Retry behavior (reserved).</summary>
    public const int Retry = -850;

    /// <summary>Bulkhead behavior (reserved).</summary>
    public const int Bulkhead = -840;

    /// <summary>Transaction behavior (reserved).</summary>
    public const int Transaction = -800;

    /// <summary>Timeout behavior for <see cref="HandlerTimeoutAttribute"/> requests.</summary>
    public const int Timeout = -700;

    /// <summary>Default order of user behaviors: inside every built-in one.</summary>
    public const int Default = 0;
}
