using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier.Behaviors;

/// <summary>
/// Logs the start, duration and outcome of every request. Only the request type name is
/// logged, never its contents, so sensitive fields do not end up in logs.
/// </summary>
public sealed partial class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private readonly ILogger _logger;

    /// <summary>Creates the behavior.</summary>
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>>? logger = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        LogHandling(RequestName);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (response is IResponseStatus { IsSuccess: false } status)
            {
                LogFailedResponse(RequestName, elapsed, status.StatusCode, status.Error);
            }
            else
            {
                LogCompleted(RequestName, elapsed);
            }

            return response;
        }
        catch (CourierException ex)
        {
            LogFailedResponse(RequestName, Stopwatch.GetElapsedTime(start).TotalMilliseconds, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            LogThrew(RequestName, ex.GetType().Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] --> Handling {RequestType}")]
    private partial void LogHandling(string requestType);

    [LoggerMessage(Level = LogLevel.Information, Message = "[Courier] <-- {RequestType} completed in {ElapsedMs:0.0}ms")]
    private partial void LogCompleted(string requestType, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[Courier] <-- {RequestType} failed in {ElapsedMs:0.0}ms (status={StatusCode}, error={Error})")]
    private partial void LogFailedResponse(string requestType, double elapsedMs, int statusCode, string? error);

    [LoggerMessage(Level = LogLevel.Error, Message = "[Courier] <-- {RequestType} threw {ExceptionType} after {ElapsedMs:0.0}ms")]
    private partial void LogThrew(string requestType, string exceptionType, double elapsedMs);
}
