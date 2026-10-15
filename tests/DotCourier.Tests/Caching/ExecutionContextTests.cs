using System.Globalization;

namespace DotCourier.Tests.Caching;

/// <summary>What a handler sees of the caller's ambient (AsyncLocal) state.</summary>
public sealed record AmbientSnapshot(string UiCulture, string? Tenant);

public static class Ambient
{
    public static readonly AsyncLocal<string?> Tenant = new();
}

[Cacheable]
public sealed record ReadAmbientCached(int Id) : IQuery<AmbientSnapshot>;

[Idempotent]
public sealed record ReadAmbientIdempotent(int Id) : ICommand<AmbientSnapshot>;

public sealed class ReadAmbientCachedHandler : IQueryHandler<ReadAmbientCached, AmbientSnapshot>
{
    public async Task<AmbientSnapshot> Handle(ReadAmbientCached query, CancellationToken ct)
    {
        await Task.Yield();
        return new AmbientSnapshot(CultureInfo.CurrentUICulture.Name, Ambient.Tenant.Value);
    }
}

public sealed class ReadAmbientIdempotentHandler : ICommandHandler<ReadAmbientIdempotent, AmbientSnapshot>
{
    public async Task<AmbientSnapshot> Handle(ReadAmbientIdempotent command, CancellationToken ct)
    {
        await Task.Yield();
        return new AmbientSnapshot(CultureInfo.CurrentUICulture.Name, Ambient.Tenant.Value);
    }
}

/// <summary>
/// HybridCache runs the factory on a separate work item without the caller's ExecutionContext when
/// the caller's token can be cancelled, which is always the case for an HTTP request
/// (HttpContext.RequestAborted). These tests use such a token on purpose.
/// </summary>
public class ExecutionContextTests
{
    // A culture unlikely to be the machine default, so the test cannot pass by accident.
    private static readonly CultureInfo Requested = new("fr-FR");

    [Fact]
    public async Task Cached_requests_run_with_the_callers_culture_and_async_locals()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddCaching());
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Requested;
            Ambient.Tenant.Value = "acme";

            using var request = new CancellationTokenSource(); // like HttpContext.RequestAborted
            var seen = await services.Courier().Send(new ReadAmbientCached(1), request.Token);

            Assert.Equal("fr-FR", seen.UiCulture);
            Assert.Equal("acme", seen.Tenant);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
            Ambient.Tenant.Value = null;
        }
    }

    [Fact]
    public async Task Idempotent_requests_run_with_the_callers_culture_and_async_locals()
    {
        var (services, _) = TestHost.Build(cfg => cfg.AddCaching());
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Requested;
            Ambient.Tenant.Value = "acme";

            using var request = new CancellationTokenSource(); // like HttpContext.RequestAborted
            var seen = await services.Courier().Send(new ReadAmbientIdempotent(1), request.Token);

            Assert.Equal("fr-FR", seen.UiCulture);
            Assert.Equal("acme", seen.Tenant);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
            Ambient.Tenant.Value = null;
        }
    }

    [Fact]
    public async Task Configured_messages_rendered_inside_a_cached_request_use_the_callers_culture()
    {
        var (services, _) = TestHost.Build(cfg =>
        {
            cfg.Messages.RateLimitExceeded = CourierMessage.From(
                () => CultureInfo.CurrentUICulture.Name == "fr-FR" ? "Trop de requêtes" : "Too many requests");
            cfg.AddCaching();
            cfg.AddResilience(o => o.ForRequest<ReadAmbientCached>(r =>
            {
                r.RateLimiter.Enabled = true;
                r.RateLimiter.PermitLimit = 1;
                r.RateLimiter.Window = TimeSpan.FromMinutes(1);
            }));
        });
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Requested;
            using var request = new CancellationTokenSource(); // like HttpContext.RequestAborted
            await services.Courier().Dispatch(new ReadAmbientCached(1), request.Token);
            var rejected = await services.Courier().Dispatch(new ReadAmbientCached(2), request.Token); // cache miss, over the limit

            Assert.Equal(429, rejected.StatusCode);
            Assert.Equal("Trop de requêtes", rejected.Error);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
