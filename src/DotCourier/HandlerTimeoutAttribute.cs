namespace DotCourier;

/// <summary>
/// Limits how long the handler of the annotated request may run. On expiry the handler's
/// token is cancelled and the request fails with <see cref="RequestTimeoutException"/> (504).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class HandlerTimeoutAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="milliseconds">Timeout in milliseconds; must be positive.</param>
    public HandlerTimeoutAttribute(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(milliseconds);
        Milliseconds = milliseconds;
    }

    /// <summary>Timeout in milliseconds.</summary>
    public int Milliseconds { get; }

    /// <summary>Timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(Milliseconds);
}
