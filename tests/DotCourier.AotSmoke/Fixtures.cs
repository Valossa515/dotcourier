using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCourier.AotSmoke;

public sealed class CallLog
{
    private readonly List<string> _entries = [];

    public string Joined => string.Join(",", _entries);

    public void Add(string entry) => _entries.Add(entry);

    public void Clear() => _entries.Clear();
}

public sealed record Ping(string Text) : IRequest<string>;

public sealed class PingHandler(CallLog log) : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken ct)
    {
        log.Add("handler");
        return Task.FromResult("pong: " + request.Text);
    }
}

public sealed class PingPre(CallLog log) : IRequestPreProcessor<Ping>
{
    public Task Process(Ping request, CancellationToken ct)
    {
        log.Add("pre");
        return Task.CompletedTask;
    }
}

public sealed class PingPost(CallLog log) : IRequestPostProcessor<Ping, string>
{
    public Task Process(Ping request, string response, CancellationToken ct)
    {
        log.Add("post");
        return Task.CompletedTask;
    }
}

public sealed record Add(int A, int B) : IRequest<int>;

public sealed class AddHandler : IRequestHandler<Add, int>
{
    public Task<int> Handle(Add request, CancellationToken ct) => Task.FromResult(request.A + request.B);
}

public sealed record Register([Required] string? Name, [EmailAddress] string Email, [Range(18, 130)] int Age) : ICommand<Guid>;

public sealed class RegisterHandler : ICommandHandler<Register, Guid>
{
    public Task<Guid> Handle(Register request, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
}

public sealed record Fragile : IRequest<string>;

public sealed class FragileHandler : IRequestHandler<Fragile, string>
{
    public Task<string> Handle(Fragile request, CancellationToken ct) => throw new InvalidOperationException("broken");
}

public sealed class FragileFallback : IRequestExceptionHandler<Fragile, string, InvalidOperationException>
{
    public Task Handle(Fragile request, InvalidOperationException exception, RequestExceptionHandlerState<string> state, CancellationToken ct)
    {
        state.SetHandled("fallback: " + exception.Message);
        return Task.CompletedTask;
    }
}

public sealed record Conflict : ICommand;

public sealed class ConflictHandler : ICommandHandler<Conflict>
{
    public Task Handle(Conflict request, CancellationToken ct) => throw new CourierException("taken", 409);
}

public sealed record Orphan : IRequest<int>;

public sealed record Flaky : IRequest<string>;

public sealed class FlakyHandler : IRequestHandler<Flaky, string>
{
    private int _attempts;

    public Task<string> Handle(Flaky request, CancellationToken ct) =>
        ++_attempts < 3 ? throw new TimeoutException("try again") : Task.FromResult($"ok after {_attempts}");
}

[HandlerTimeout(50)]
public sealed record Slow : IRequest<string>;

public sealed class SlowHandler : IRequestHandler<Slow, string>
{
    public async Task<string> Handle(Slow request, CancellationToken ct)
    {
        await Task.Delay(5_000, ct);
        return "late";
    }
}

public sealed record Shipped(string What) : INotification;

/// <summary>No closed handler: only the open generic AuditAll&lt;T&gt; receives it.</summary>
public sealed record Pinged : INotification;

public sealed class FirstShippedHandler(CallLog log) : INotificationHandler<Shipped>
{
    public Task Handle(Shipped notification, CancellationToken ct)
    {
        log.Add("first " + notification.What);
        return Task.CompletedTask;
    }
}

public sealed class SecondShippedHandler(CallLog log) : INotificationHandler<Shipped>
{
    public Task Handle(Shipped notification, CancellationToken ct)
    {
        log.Add("second " + notification.What);
        return Task.CompletedTask;
    }
}

public sealed class AuditAll<T>(CallLog log) : INotificationHandler<T>
    where T : INotification
{
    public Task Handle(T notification, CancellationToken ct)
    {
        log.Add("audit " + typeof(T).Name);
        return Task.CompletedTask;
    }
}

public sealed record Count(int To) : IStreamRequest<int>;

public sealed class CountHandler : IStreamRequestHandler<Count, int>
{
    public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= request.To; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed class TraceBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        log.Add("trace>");
        var response = await next(ct);
        log.Add("<trace");
        return response;
    }
}

public sealed class TraceStreamBehavior<TRequest, TResponse>(CallLog log) : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, [EnumeratorCancellation] CancellationToken ct)
    {
        log.Add("stream>");
        await foreach (var item in next().WithCancellation(ct))
        {
            yield return item;
        }

        log.Add("<stream");
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Response<string>))]
[JsonSerializable(typeof(Response<DotCourier.Unit>))]
internal sealed partial class SmokeJson : JsonSerializerContext;

internal sealed class Checks
{
    private int _failed;

    public void Equal<T>(string name, T expected, T actual)
    {
        var ok = EqualityComparer<T>.Default.Equals(expected, actual);
        _failed += ok ? 0 : 1;
        Console.WriteLine(ok ? $"PASS {name}" : $"FAIL {name}: expected {expected}, got {actual}");
    }

    public int Report()
    {
        Console.WriteLine(_failed == 0 ? "All checks passed." : $"{_failed} check(s) failed.");
        return _failed == 0 ? 0 : 1;
    }
}
