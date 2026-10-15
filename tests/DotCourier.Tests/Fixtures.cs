using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCourier.Tests;

public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

/// <summary>Collects every log entry so tests can assert on what was logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public LogEntry[] Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
    }
}

public sealed class CallLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    public string[] Entries => _entries.ToArray();

    public void Add(string entry) => _entries.Enqueue(entry);
}

internal static class TestHost
{
    public static (ServiceProvider Services, CallLog Log) Build(
        Action<CourierConfiguration>? configure = null,
        CapturingLoggerProvider? logs = null,
        Action<IServiceCollection>? registerServices = null,
        CallLog? sharedLog = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
            }
        });
        var log = sharedLog ?? new CallLog();
        services.AddSingleton(log);
        registerServices?.Invoke(services);
        services.AddCourier(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CallLog>();
            configure?.Invoke(cfg);
        });

        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }), log);
    }

    public static ICourier Courier(this ServiceProvider services) => services.GetRequiredService<ICourier>();
}

// --- requests with a value -------------------------------------------------

public sealed record Ping(string Message) : IRequest<string>;

public sealed class PingHandler(CallLog log) : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
    {
        log.Add("handler");
        return Task.FromResult("pong: " + request.Message);
    }
}

public sealed class PingPreProcessor(CallLog log) : IRequestPreProcessor<Ping>
{
    public Task Process(Ping request, CancellationToken cancellationToken)
    {
        log.Add("pre");
        return Task.CompletedTask;
    }
}

public sealed class PingPostProcessor(CallLog log) : IRequestPostProcessor<Ping, string>
{
    public Task Process(Ping request, string response, CancellationToken cancellationToken)
    {
        log.Add("post:" + response);
        return Task.CompletedTask;
    }
}

// --- commands / queries ----------------------------------------------------

public sealed record CreateItem(string Name) : ICommand;

public sealed class CreateItemHandler(CallLog log) : ICommandHandler<CreateItem>
{
    public Task Handle(CreateItem request, CancellationToken cancellationToken)
    {
        log.Add("created " + request.Name);
        return Task.CompletedTask;
    }
}

public sealed record GetItem(int Id) : IQuery<Response<string>>;

public sealed class GetItemHandler : IQueryHandler<GetItem, Response<string>>
{
    public Task<Response<string>> Handle(GetItem request, CancellationToken cancellationToken) =>
        Task.FromResult<Response<string>>(request.Id == 0
            ? Response.NotFound("Item 0 not found")
            : $"item {request.Id}");
}

// --- failures --------------------------------------------------------------

public sealed record Boom(bool AsCourierException) : IRequest<int>;

public sealed class BoomHandler : IRequestHandler<Boom, int>
{
    public Task<int> Handle(Boom request, CancellationToken cancellationToken) =>
        request.AsCourierException
            ? throw new CourierException("Out of stock", 409)
            : throw new InvalidOperationException("connection string: Password=hunter2");
}

public sealed record NoHandler : IRequest<int>;

public sealed record Fragile : IRequest<string>;

public sealed class FragileException(string message) : InvalidOperationException(message);

public sealed class FragileHandler : IRequestHandler<Fragile, string>
{
    public Task<string> Handle(Fragile request, CancellationToken cancellationToken) => throw new FragileException("broken");
}

public sealed class FragileFallback : IRequestExceptionHandler<Fragile, string, InvalidOperationException>
{
    public Task Handle(Fragile request, InvalidOperationException exception, RequestExceptionHandlerState<string> state, CancellationToken cancellationToken)
    {
        state.SetHandled("fallback after " + exception.Message);
        return Task.CompletedTask;
    }
}

// --- validation ------------------------------------------------------------

public sealed record Register(
    [Required] string? Name,
    [EmailAddress] string Email,
    [Range(18, 130)] int Age) : ICommand<Guid>;

public sealed class RegisterHandler(CallLog log) : ICommandHandler<Register, Guid>
{
    public Task<Guid> Handle(Register request, CancellationToken cancellationToken)
    {
        log.Add("registered");
        return Task.FromResult(Guid.NewGuid());
    }
}

public sealed class RegisterValidator : IRequestValidator<Register>
{
    public ValueTask<IEnumerable<ValidationError>> ValidateAsync(Register request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ValidationError>>(request.Name == "admin"
            ? [new ValidationError(nameof(Register.Name), "Name is reserved")]
            : []);
}

// --- timeouts --------------------------------------------------------------

[HandlerTimeout(100)]
public sealed record Slow(int DelayMs) : IRequest<string>;

public sealed class SlowHandler : IRequestHandler<Slow, string>
{
    public async Task<string> Handle(Slow request, CancellationToken cancellationToken)
    {
        await Task.Delay(request.DelayMs, cancellationToken);
        return "done";
    }
}

[HandlerTimeout(100)]
public sealed record Stubborn : IRequest<string>;

public sealed class StubbornHandler : IRequestHandler<Stubborn, string>
{
    public async Task<string> Handle(Stubborn request, CancellationToken cancellationToken)
    {
        await Task.Delay(2_000, CancellationToken.None); // ignores the token on purpose
        return "too late";
    }
}

// --- behaviors -------------------------------------------------------------

public sealed class OuterBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Add("outer>");
        var response = await next();
        log.Add("<outer");
        return response;
    }
}

public sealed class InnerBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Add("inner>");
        var response = await next();
        log.Add("<inner");
        return response;
    }
}

public sealed class PingOnlyBehavior : IPipelineBehavior<Ping, string>
{
    public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) =>
        (await next()) + " (decorated)";
}

// --- notifications ---------------------------------------------------------

public sealed record ItemCreated(string Name) : INotification;

public sealed class FirstItemCreatedHandler(CallLog log) : INotificationHandler<ItemCreated>
{
    public Task Handle(ItemCreated notification, CancellationToken cancellationToken)
    {
        log.Add("first");
        return Task.CompletedTask;
    }
}

public sealed class FailingItemCreatedHandler(CallLog log) : INotificationHandler<ItemCreated>
{
    public Task Handle(ItemCreated notification, CancellationToken cancellationToken)
    {
        if (notification.Name == "fail")
        {
            log.Add("failing");
            throw new InvalidOperationException("handler failed");
        }

        log.Add("second");
        return Task.CompletedTask;
    }
}

public sealed class LastItemCreatedHandler(CallLog log) : INotificationHandler<ItemCreated>
{
    public Task Handle(ItemCreated notification, CancellationToken cancellationToken)
    {
        log.Add("last");
        return Task.CompletedTask;
    }
}

public sealed record NobodyListens : INotification;

// --- telemetry -------------------------------------------------------------

public sealed record TelemetryProbe(bool Fail) : IRequest<int>;

public sealed class TelemetryProbeHandler : IRequestHandler<TelemetryProbe, int>
{
    public Task<int> Handle(TelemetryProbe request, CancellationToken cancellationToken) =>
        request.Fail ? throw new CourierException("probe failed", 422) : Task.FromResult(42);
}
