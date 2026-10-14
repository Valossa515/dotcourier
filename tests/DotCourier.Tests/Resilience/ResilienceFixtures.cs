using System.ComponentModel.DataAnnotations;

namespace DotCourier.Tests.Resilience;

/// <summary>Fails <paramref name="Failures"/> times, then succeeds.</summary>
public sealed record Flaky(string Key, int Failures) : IQuery<string>;

public sealed class FlakyHandler(CallLog log) : IQueryHandler<Flaky, string>
{
    public Task<string> Handle(Flaky request, CancellationToken cancellationToken)
    {
        log.Add(request.Key);
        var attempt = log.Entries.Count(e => e == request.Key);
        return attempt <= request.Failures
            ? throw new InvalidOperationException("transient failure")
            : Task.FromResult($"ok after {attempt}");
    }
}

public sealed record AlwaysFails(string Key) : ICommand;

public sealed class AlwaysFailsHandler(CallLog log) : ICommandHandler<AlwaysFails>
{
    public Task Handle(AlwaysFails request, CancellationToken cancellationToken)
    {
        log.Add(request.Key);
        throw new InvalidOperationException("dependency down");
    }
}

public sealed record MissingThing(string Key) : IQuery<string>;

public sealed class MissingThingHandler(CallLog log) : IQueryHandler<MissingThing, string>
{
    public Task<string> Handle(MissingThing request, CancellationToken cancellationToken)
    {
        log.Add(request.Key);
        throw new CourierException("not found", 404);
    }
}

public sealed record Limited([Required] string? Name) : IQuery<string>;

public sealed class LimitedHandler : IQueryHandler<Limited, string>
{
    public Task<string> Handle(Limited request, CancellationToken cancellationToken) => Task.FromResult("ok " + request.Name);
}

/// <summary>The first attempt of key "slow-fail" throws; anything else completes after <paramref name="WorkMs"/>.</summary>
public sealed record Crowded(string Key, int WorkMs) : ICommand<string>;

public sealed class CrowdedHandler(CallLog log) : ICommandHandler<Crowded, string>
{
    public async Task<string> Handle(Crowded request, CancellationToken cancellationToken)
    {
        log.Add(request.Key);
        if (request.Key == "slow-fail" && log.Entries.Count(e => e == request.Key) == 1)
        {
            throw new InvalidOperationException("first attempt fails");
        }

        await Task.Delay(request.WorkMs, cancellationToken);
        return "done " + request.Key;
    }
}
