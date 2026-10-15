using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
    private static readonly ConcurrentDictionary<Type, StreamHandlerBase> StreamHandlers = new();

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
        var wrapper = (RequestHandlerWrapper<TResponse>)RequestWrapper(request.GetType());
        return wrapper.Handle(request, _services, cancellationToken);
    }

    /// <inheritdoc />
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequestWrapper(request.GetType()).HandleBoxed(request, _services, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StreamWrapper(request.GetType()) is StreamHandlerWrapper<TResponse> wrapper
            ? wrapper.Handle(request, _services, cancellationToken)
            : MissingStream<TResponse>(request.GetType(), _options.Messages);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StreamWrapper(request.GetType()) is { } wrapper
            ? wrapper.HandleBoxed(request, _services, cancellationToken)
            : MissingStream<object?>(request.GetType(), _options.Messages);
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
    public Task Publish(object notification, CancellationToken cancellationToken = default) =>
        Publish(notification, _options.NotificationPublishStrategy, cancellationToken);

    /// <summary>
    /// Publishes <paramref name="notification"/> with <paramref name="strategy"/> instead of the
    /// configured one, e.g. <see cref="NotificationPublishStrategy.SequentialThenThrow"/> to learn
    /// whether every handler succeeded.
    /// </summary>
    public Task Publish(object notification, NotificationPublishStrategy strategy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification is not INotification typed)
        {
            throw new ArgumentException(
                $"'{CourierDiagnostics.GetRequestName(notification.GetType())}' does not implement {nameof(INotification)}.", nameof(notification));
        }

        var executors = NotificationWrapper(notification.GetType())?.GetExecutors(_services) ?? [];

        if (executors.Length == 0)
        {
            LogNoNotificationHandlers(CourierDiagnostics.GetRequestName(notification.GetType()));
            return Task.CompletedTask;
        }

        return strategy switch
        {
            NotificationPublishStrategy.ParallelWhenAll => PublishParallel(executors, typed, cancellationToken),
            NotificationPublishStrategy.StopOnFirstError => PublishStopOnFirstError(executors, typed, cancellationToken),
            NotificationPublishStrategy.SequentialThenThrow => PublishSequentialThenThrow(executors, typed, cancellationToken),
            _ => PublishSequential(executors, typed, cancellationToken),
        };
    }

    // Wrappers built by generated registrations (DotCourier.SourceGenerator): closed generic types
    // created with `new`, so dispatching these requests never needs MakeGenericType.
    internal static void AddRequestWrapper(Type requestType, Func<RequestHandlerBase> create) => RequestHandlers.GetOrAdd(requestType, static (_, c) => c(), create);

    internal static void AddStreamWrapper(Type requestType, Func<StreamHandlerBase> create) => StreamHandlers.GetOrAdd(requestType, static (_, c) => c(), create);

    internal static void AddNotificationWrapper(Type notificationType, Func<NotificationHandlerWrapper> create) => NotificationHandlers.GetOrAdd(notificationType, static (_, c) => c(), create);

    internal static bool HasRequestWrapper(Type requestType) => RequestHandlers.ContainsKey(requestType);

    internal static bool HasStreamWrapper(Type requestType) => StreamHandlers.ContainsKey(requestType);

    internal static bool HasNotificationWrapper(Type notificationType) => NotificationHandlers.ContainsKey(notificationType);

    // Outside compile-time mode, types without a generated wrapper get one built with reflection. In
    // compile-time mode (always on under Native AOT, where MakeGenericType cannot run) they have no handler
    // registered at compile time, which is reported like any missing handler.
    private const string Guarded = "Only reached outside compile-time mode, which is always on under Native AOT.";

    private RequestHandlerBase RequestWrapper(Type requestType)
    {
        if (RequestHandlers.TryGetValue(requestType, out var wrapper))
        {
            return wrapper;
        }

        return _options.RequireCompileTimeRegistrations
            ? throw new HandlerNotFoundException(requestType, _options.Messages)
            : RequestHandlers.GetOrAdd(requestType, CreateRequestWrapper);
    }

    private StreamHandlerBase? StreamWrapper(Type requestType)
    {
        if (StreamHandlers.TryGetValue(requestType, out var wrapper))
        {
            return wrapper;
        }

        return _options.RequireCompileTimeRegistrations ? null : StreamHandlers.GetOrAdd(requestType, CreateStreamWrapper);
    }

    private NotificationHandlerWrapper? NotificationWrapper(Type notificationType)
    {
        if (NotificationHandlers.TryGetValue(notificationType, out var wrapper))
        {
            return wrapper;
        }

        return _options.RequireCompileTimeRegistrations ? null : NotificationHandlers.GetOrAdd(notificationType, CreateNotificationWrapper);
    }

    /// <summary>A stream that fails on first read, like one whose handler is missing.</summary>
    private static async IAsyncEnumerable<T> MissingStream<T>(Type requestType, CourierMessages messages)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        if (requestType is not null)
        {
            throw new HandlerNotFoundException(requestType, messages);
        }

        yield break;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Guarded)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = Guarded)]
    private static NotificationHandlerWrapper CreateNotificationWrapper(Type notificationType) =>
        (NotificationHandlerWrapper)Activator.CreateInstance(typeof(NotificationHandlerWrapperImpl<>).MakeGenericType(notificationType))!;

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Guarded)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = Guarded)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = Guarded)]
    private static StreamHandlerBase CreateStreamWrapper(Type requestType)
    {
        var streamInterface = requestType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamRequest<>))
            ?? throw new ArgumentException(
                $"'{requestType.Name}' does not implement {nameof(IStreamRequest<object>)}<TResponse>.", nameof(requestType));

        var responseType = streamInterface.GetGenericArguments()[0];
        var wrapperType = typeof(StreamHandlerWrapperImpl<,>).MakeGenericType(requestType, responseType);
        return (StreamHandlerBase)Activator.CreateInstance(wrapperType)!;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Guarded)]
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = Guarded)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = Guarded)]
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

    private static async Task PublishSequentialThenThrow(NotificationHandlerExecutor[] executors, INotification notification, CancellationToken cancellationToken)
    {
        List<NotificationHandlerException>? failures = null;
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
                (failures ??= []).Add(new NotificationHandlerException(executor.HandlerInstance.GetType(), notification.GetType(), ex));
            }
        }

        if (failures is { Count: 1 })
        {
            throw failures[0];
        }

        if (failures is not null)
        {
            throw new AggregateException($"{failures.Count} notification handlers failed for '{CourierDiagnostics.GetRequestName(notification.GetType())}'", failures);
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
