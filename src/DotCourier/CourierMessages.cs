using System.Globalization;
using DotCourier.Behaviors;

namespace DotCourier;

/// <summary>
/// A message DotCourier puts in error responses, with an optional machine-readable code.
/// Either fixed text (<c>new("...")</c>, or a plain string thanks to the implicit conversion) or
/// produced on every use with <see cref="From"/>, so it can follow the current request's culture
/// (e.g. <c>CourierMessage.From(() =&gt; Resources.UnexpectedError)</c>).
/// Placeholders <c>{request}</c>, <c>{count}</c> and <c>{timeoutMs}</c> are filled in when they apply.
/// </summary>
public sealed class CourierMessage
{
    private readonly Func<string> _text;

    /// <summary>A fixed message.</summary>
    /// <param name="text">The text, optionally with placeholders.</param>
    /// <param name="code">Optional code, sent as <see cref="Response{T}.ErrorCode"/>.</param>
    public CourierMessage(string text, string? code = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = () => text;
        Code = code;
    }

    private CourierMessage(Func<string> text, string? code)
    {
        _text = text;
        Code = code;
    }

    /// <summary>Optional machine-readable code, sent as <see cref="Response{T}.ErrorCode"/>.</summary>
    public string? Code { get; }

    /// <summary>
    /// A message produced each time it is used, e.g. from a <c>.resx</c> resource, so it is
    /// rendered in the culture of the request being handled.
    /// </summary>
    public static CourierMessage From(Func<string> text, string? code = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new CourierMessage(text, code);
    }

    /// <summary>A fixed message without a code.</summary>
    public static implicit operator CourierMessage(string text) => new(text);

    /// <summary>Produces the text and fills in the placeholders.</summary>
    public string Render(Type? requestType = null, int? count = null, TimeSpan? timeout = null)
    {
        var text = _text() ?? string.Empty;
        if (requestType is not null)
        {
            text = text.Replace("{request}", CourierDiagnostics.GetRequestName(requestType), StringComparison.Ordinal);
        }

        if (count is { } c)
        {
            text = text.Replace("{count}", c.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
        }

        if (timeout is { } t)
        {
            text = text.Replace("{timeoutMs}", t.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture), StringComparison.Ordinal);
        }

        return text;
    }

    /// <inheritdoc />
    public override string ToString() => Render();
}

/// <summary>
/// The fixed messages DotCourier writes into error responses. Change them with
/// <c>cfg.Messages.X = ...</c> in <c>AddCourier</c>; the defaults are the English texts below.
/// Messages you write yourself (<c>Response.NotFound("...")</c>, <c>CourierException</c>) are not affected.
/// </summary>
public sealed class CourierMessages
{
    /// <summary>Any exception that is not a <see cref="CourierException"/> (500). Default: "An internal error occurred".</summary>
    public CourierMessage UnexpectedError { get; set; } = "An internal error occurred";

    /// <summary>Request validation failed (400). Placeholders: <c>{count}</c>, <c>{request}</c>.</summary>
    public CourierMessage ValidationFailed { get; set; } = "Validation failed with {count} error(s)";

    /// <summary>No handler is registered (500). Placeholder: <c>{request}</c>.</summary>
    public CourierMessage HandlerNotFound { get; set; } = "No handler registered for request type '{request}'.";

    /// <summary>A <see cref="HandlerTimeoutAttribute"/> elapsed (504). Placeholders: <c>{request}</c>, <c>{timeoutMs}</c>.</summary>
    public CourierMessage RequestTimeout { get; set; } = "Request '{request}' timed out after {timeoutMs} ms";

    /// <summary>DotCourier.Resilience: the circuit is open (503). Placeholder: <c>{request}</c>.</summary>
    public CourierMessage CircuitOpen { get; set; } = "Service temporarily unavailable: the circuit is open for this operation";

    /// <summary>DotCourier.Resilience: the rate limit is exhausted (429). Placeholder: <c>{request}</c>.</summary>
    public CourierMessage RateLimitExceeded { get; set; } = "Too many requests: the rate limit for this operation is exhausted";

    /// <summary>DotCourier.Resilience: the bulkhead is full (503). Placeholder: <c>{request}</c>.</summary>
    public CourierMessage BulkheadRejected { get; set; } = "Service busy: too many concurrent executions of this operation";

    /// <summary>The defaults, used where no configuration is available.</summary>
    public static CourierMessages Default { get; } = new();
}
