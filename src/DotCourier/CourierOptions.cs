namespace DotCourier;

/// <summary>Runtime settings resolved from <c>AddCourier</c>; registered as a singleton.</summary>
public sealed class CourierOptions
{
    /// <summary>How notifications are delivered to their handlers.</summary>
    public NotificationPublishStrategy NotificationPublishStrategy { get; init; } = NotificationPublishStrategy.Sequential;

    /// <summary>Whether the validation behavior evaluates DataAnnotations attributes.</summary>
    public bool UseDataAnnotations { get; init; } = true;

    /// <summary>The fixed messages written into error responses.</summary>
    public CourierMessages Messages { get; init; } = CourierMessages.Default;
}
