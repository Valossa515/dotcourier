using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DotCourier.Generated;
using System.Runtime.CompilerServices;
using DotCourier.Internal;
using DotCourier.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier.Behaviors;

/// <summary>
/// Validates a stream request (DataAnnotations and every <see cref="IRequestValidator{TRequest}"/>)
/// before the first item is produced; an invalid request throws <see cref="ValidationException"/>.
/// </summary>
public sealed class StreamValidationBehavior<[DynamicallyAccessedMembers(CompileTimeTypes.Request)] TRequest, TResponse>(
    IEnumerable<IRequestValidator<TRequest>> validators,
    CourierOptions? options = null)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RequestValidation.ThrowIfInvalidAsync(request, validators, options?.UseDataAnnotations ?? true, options, cancellationToken).ConfigureAwait(false);
        await foreach (var item in next().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}

/// <summary>
/// Logs when a stream starts and how it ends: completed (with the item count), stopped early by the
/// caller, cancelled or failed. Only the request type name is logged, never its contents or items.
/// </summary>
public sealed partial class StreamLoggingBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string RequestName = CourierDiagnostics.GetRequestName(typeof(TRequest));
    private readonly ILogger _logger;

    /// <summary>Creates the behavior.</summary>
    public StreamLoggingBehavior(ILogger<StreamLoggingBehavior<TRequest, TResponse>>? logger = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        long start = 0;
        return StreamObserver.Observe(next, cancellationToken, new StreamObserver.Callbacks
        {
            Started = () =>
            {
                LogStreaming(RequestName);
                start = Stopwatch.GetTimestamp();
            },
            Completed = count => LogCompleted(RequestName, count, Elapsed(start)),
            Stopped = count => LogStopped(RequestName, count, Elapsed(start)),
            Failed = (exception, count) =>
            {
                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    LogCancelled(RequestName, count, Elapsed(start));
                }
                else if (exception is CourierException courier)
                {
                    LogFailed(RequestName, count, Elapsed(start), courier.StatusCode, courier.Message);
                }
                else
                {
                    LogThrew(exception, RequestName, exception.GetType().Name, count, Elapsed(start));
                }
            },
        });
    }

    private static double Elapsed(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] --> Streaming {RequestType}")]
    private partial void LogStreaming(string requestType);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] <-- {RequestType} streamed {Count} item(s) in {ElapsedMs:0.0}ms")]
    private partial void LogCompleted(string requestType, int count, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] <-- {RequestType} stream stopped by the caller after {Count} item(s) in {ElapsedMs:0.0}ms")]
    private partial void LogStopped(string requestType, int count, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] <-- {RequestType} stream cancelled by the caller after {Count} item(s) in {ElapsedMs:0.0}ms")]
    private partial void LogCancelled(string requestType, int count, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] <-- {RequestType} stream failed after {Count} item(s) in {ElapsedMs:0.0}ms (status={StatusCode}, error={Error})")]
    private partial void LogFailed(string requestType, int count, double elapsedMs, int statusCode, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "[Courier] <-- {RequestType} stream threw {ExceptionType} after {Count} item(s) in {ElapsedMs:0.0}ms")]
    private partial void LogThrew(Exception exception, string requestType, string exceptionType, int count, double elapsedMs);
}

/// <summary>
/// Opens an <see cref="Activity"/> that spans the whole stream (tagged with the item count) and
/// records the same request metrics as <see cref="TelemetryBehavior{TRequest,TResponse}"/>.
/// </summary>
public sealed class StreamTelemetryBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string RequestName = CourierDiagnostics.GetRequestName(typeof(TRequest));
    private static readonly string ActivityName = "courier " + RequestName;

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Activity? activity = null;
        long start = 0;
        return StreamObserver.Observe(next, cancellationToken, new StreamObserver.Callbacks
        {
            Started = () =>
            {
                activity = CourierDiagnostics.ActivitySource.StartActivity(ActivityName);
                activity?.SetTag("courier.request.type", RequestName);
                start = Stopwatch.GetTimestamp();
            },

            // Each step of the stream runs in the caller's context: put the activity back around it so
            // the handler's own spans (database, HTTP) are its children, not the caller's.
            Activity = () => activity,
            Completed = count => End(activity, start, count, "success", null),
            Stopped = count => End(activity, start, count, "success", null),
            Failed = (exception, count) =>
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                activity?.SetTag("error.type", exception.GetType().FullName);
                End(activity, start, count, "error", exception.GetType().Name);
            },
        });
    }

    private static void End(Activity? activity, long start, int count, string outcome, string? errorType)
    {
        activity?.SetTag("courier.stream.items", count);
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
        activity?.Dispose();
    }
}

/// <summary>Enumerates a stream and reports its start, end (complete, stopped early, failed) and item count.</summary>
internal static class StreamObserver
{
    internal sealed class Callbacks
    {
        public Action? Started { get; init; }

        public Func<Activity?>? Activity { get; init; }

        public Action<int>? Completed { get; init; }

        public Action<int>? Stopped { get; init; }

        public Action<Exception, int>? Failed { get; init; }
    }

    public static async IAsyncEnumerable<T> Observe<T>(StreamHandlerDelegate<T> next, CancellationToken token, Callbacks callbacks, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        callbacks.Started?.Invoke();
        var count = 0;
        var finished = false;
        IAsyncEnumerator<T> enumerator;
        try
        {
            enumerator = next().GetAsyncEnumerator(cancellationToken == default ? token : cancellationToken);
        }
        catch (Exception ex)
        {
            callbacks.Failed?.Invoke(ex, count);
            throw;
        }

        try
        {
            while (true)
            {
                bool hasItem;
                var previous = System.Diagnostics.Activity.Current;
                try
                {
                    if (callbacks.Activity?.Invoke() is { } activity)
                    {
                        System.Diagnostics.Activity.Current = activity;
                    }

                    hasItem = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    finished = true;
                    callbacks.Failed?.Invoke(ex, count);
                    throw;
                }
                finally
                {
                    System.Diagnostics.Activity.Current = previous;
                }

                if (!hasItem)
                {
                    break;
                }

                count++;
                yield return enumerator.Current;
            }

            finished = true;
            callbacks.Completed?.Invoke(count);
        }
        finally
        {
            if (!finished)
            {
                callbacks.Stopped?.Invoke(count); // the caller stopped reading (break, or an exception of its own)
            }

            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}
