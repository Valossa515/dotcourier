using System.Collections.Concurrent;
using DotCourier.Behaviors;
using DotCourier.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier;

/// <summary>
/// Default <see cref="ICourier"/>. Handlers, behaviors and processors are resolved from
/// the <see cref="IServiceProvider"/> the courier was created with, so scoped services
/// follow the caller's scope.
/// </summary>
public partial class Courier : ICourier
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> RequestHandlers = new();
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapper> NotificationHandlers = new();

    private readonly IServiceProvider _services;
    private readonly CourierOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates the courier.</summary>
    public Courier(IServiceProvider services, CourierOptions? options = null, ILogger<Courier>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? new CourierOptions();
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <inheritdoc />
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = (RequestHandlerWrapper<TResponse>)RequestHandlers.GetOrAdd(request.GetType(), CreateRequestWrapper);
        return wrapper.Handle(request, _services, cancellationToken);
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wrapper = RequestHandlers.GetOrAdd(request.GetType(), CreateRequestWrapper);
        return wrapper.HandleBoxed(request, _services, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Response<TResponse>> Dispatch<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await Send(request, cancellationToken).ConfigureAwait(false);
            return Response<TResponse>.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToErrorResponse<TResponse>(request, ex);
        }
    }

    /// <inheritdoc />
    public async Task<Response<TResponse>> Dispatch<TResponse>(IRequest<Response<TResponse>> request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await Send(request, cancellationToken).ConfigureAwait(false);
            return result ?? Response<TResponse>.Success(default);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToErrorResponse<TResponse>(request, ex);
        }
    }

    /// <inheritdoc />
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification =>
        Publish((object)notification, cancellationToken);

    /// <inheritdoc />
    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification is not INotification typed)
        {
            throw new ArgumentException(
                $"'{CourierDiagnostics.GetRequestName(notification.GetType())}' does not implement {nameof(INotification)}.", nameof(notification));
        }

        var wrapper = NotificationHandlers.GetOrAdd(notification.GetType(), static type =>
            (NotificationHandlerWrapper)Activator.CreateInstance(
                typeof(NotificationHandlerWrapperImpl<>).MakeGenericType(type))!);
        var executors = wrapper.GetExecutors(_services);

        if (executors.Length == 0)
        {
            LogNoNotificationHandlers(CourierDiagnostics.GetRequestName(notification.GetType()));
            return Task.CompletedTask;
        }

        return _options.NotificationPublishStrategy switch
        {
            NotificationPublishStrategy.ParallelWhenAll => PublishParallel(executors, typed, cancellationToken),
            NotificationPublishStrategy.StopOnFirstError => PublishStopOnFirstError(executors, typed, cancellationToken),
            _ => PublishSequential(executors, typed, cancellationToken),
        };
    }

    private static RequestHandlerBase CreateRequestWrapper(Type requestType)
    {
        var requestInterface = requestType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
            ?? throw new ArgumentException(
                $"'{requestType.Name}' does not implement {nameof(IRequest)}<TResponse>.", nameof(requestType));

        var responseType = requestInterface.GetGenericArguments()[0];
        var wrapperType = typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType);
        return (RequestHandlerBase)Activator.CreateInstance(wrapperType)!;
    }

    private Response<TResponse> ToErrorResponse<TResponse>(object request, Exception exception)
    {
        if (exception is CourierException)
        {
            LogCourierFailure(CourierDiagnostics.GetRequestName(request.GetType()), exception.GetType().Name, exception.Message);
        }
        else if (!ExceptionLogMarker.IsMarked(exception))
        {
            // Only when the logging behavior did not already record it (e.g. it is disabled).
            LogUnexpectedFailure(exception, CourierDiagnostics.GetRequestName(request.GetType()));
        }

        return Response<TResponse>.FromException(exception, _options.Messages);
    }

    private async Task PublishSequential(NotificationHandlerExecutor[] executors, INotification notification, CancellationToken cancellationToken)
    {
        foreach (var executor in executors)
        {
            await RunLoggingFailures(executor, notification, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task PublishParallel(NotificationHandlerExecutor[] executors, INotification notification, CancellationToken cancellationToken)
    {
        var tasks = new Task[executors.Length];
        for (var i = 0; i < executors.Length; i++)
        {
            tasks[i] = RunLoggingFailures(executors[i], notification, cancellationToken);
        }

        return Task.WhenAll(tasks);
    }

    private static async Task PublishStopOnFirstError(NotificationHandlerExecutor[] executors, INotification notification, CancellationToken cancellationToken)
    {
        foreach (var executor in executors)
        {
            try
            {
                await executor.Callback(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new NotificationHandlerException(executor.HandlerInstance.GetType(), notification.GetType(), ex);
            }
        }
    }

    private async Task RunLoggingFailures(NotificationHandlerExecutor executor, INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await executor.Callback(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogNotificationHandlerFailure(ex, CourierDiagnostics.GetRequestName(executor.HandlerInstance.GetType()), CourierDiagnostics.GetRequestName(notification.GetType()));
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No notification handlers registered for {NotificationType}")]
    private partial void LogNoNotificationHandlers(string notificationType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request {RequestType} failed with {ExceptionType}: {Message}")]
    private partial void LogCourierFailure(string requestType, string exceptionType, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Request {RequestType} failed with an unexpected exception")]
    private partial void LogUnexpectedFailure(Exception exception, string requestType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification handler {HandlerType} failed for {NotificationType}")]
    private partial void LogNotificationHandlerFailure(Exception exception, string handlerType, string notificationType);
}
