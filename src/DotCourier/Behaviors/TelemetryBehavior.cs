using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace DotCourier.Behaviors;

/// <summary>
/// Names of the <see cref="ActivitySource"/> and <see cref="Meter"/> DotCourier emits to.
/// With OpenTelemetry: <c>.WithTracing(t =&gt; t.AddSource(CourierDiagnostics.SourceName))</c>
/// and <c>.WithMetrics(m =&gt; m.AddMeter(CourierDiagnostics.SourceName))</c>.
/// </summary>
public static class CourierDiagnostics
{
    /// <summary>Name of both the activity source and the meter.</summary>
    public const string SourceName = "DotCourier";

    /// <summary>Counter of handled requests, tagged by request type and outcome.</summary>
    public const string RequestsMetric = "courier.requests";

    /// <summary>Histogram of request durations in seconds, tagged by request type and outcome.</summary>
    public const string DurationMetric = "courier.request.duration";

    private static readonly string? Version =
        typeof(CourierDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    internal static readonly ActivitySource ActivitySource = new(SourceName, Version);
    internal static readonly Meter Meter = new(SourceName, Version);

    internal static readonly Counter<long> Requests =
        Meter.CreateCounter<long>(RequestsMetric, "{request}", "Requests handled by DotCourier");

    internal static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>(DurationMetric, "s", "Duration of requests handled by DotCourier");
}

/// <summary>
/// Opens an <see cref="Activity"/> per request and records <see cref="CourierDiagnostics.RequestsMetric"/>
/// and <see cref="CourierDiagnostics.DurationMetric"/>. Costs next to nothing when nobody listens.
/// </summary>
public sealed class TelemetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly string ActivityName = "courier " + RequestName;

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        using var activity = CourierDiagnostics.ActivitySource.StartActivity(ActivityName);
        activity?.SetTag("courier.request.type", RequestName);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            if (response is IResponseStatus { IsSuccess: false } status)
            {
                var errorType = status.ExceptionType ?? status.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                activity?.SetStatus(ActivityStatusCode.Error, status.Error);
                Record(start, "error", errorType);
            }
            else
            {
                Record(start, "success", null);
            }

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            activity?.SetTag("error.type", ex.GetType().FullName);
            Record(start, "error", ex.GetType().Name);
            throw;
        }
    }

    private static void Record(long start, string outcome, string? errorType)
    {
        var tags = new TagList
        {
            { "courier.request.type", RequestName },
            { "courier.outcome", outcome },
        };
        if (errorType is not null)
        {
            tags.Add("error.type", errorType);
        }

        CourierDiagnostics.Requests.Add(1, tags);
        CourierDiagnostics.Duration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
    }
}
