using System.Collections.Concurrent;
using DotCourier.Caching;
using DotCourier.Caching.Redis;
using DotCourier.Tests.Kafka;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace DotCourier.Tests.Caching;

/// <summary>Stands in for Redis' lock: one per test, shared by the "instances" like a real Redis would be.</summary>
internal sealed class SharedLock : IIdempotencyLock
{
    private readonly ConcurrentDictionary<string, string> _held = new();

    public int Acquisitions;

    public bool IsHeld => !_held.IsEmpty;

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        if (!_held.TryAdd(key, token))
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Interlocked.Increment(ref Acquisitions);
        return Task.FromResult<IAsyncDisposable?>(new Release(_held, key, token));
    }

    /// <summary>As if another instance were running the request.</summary>
    public void HoldEverything(string key) => _held[key] = "someone else";

    private sealed class Release(ConcurrentDictionary<string, string> held, string key, string token) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            held.TryRemove(new KeyValuePair<string, string>(key, token));
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class IdempotencyLockTests
{
    private readonly FakeRedis _redis = new();
    private readonly SharedLock _lock = new();
    private readonly CallLog _log = new();

    /// <summary>One application instance: its own container and memory, sharing the cache server and the lock.</summary>
    private ServiceProvider Instance(Action<CourierCachingOptions>? caching = null, bool withLock = true, CapturingLoggerProvider? logs = null) =>
        TestHost.Build(
            cfg =>
            {
                cfg.AddCaching(caching);
                cfg.Messages.IdempotencyInProgress = new("{request} is still running", code: "IDEMP-409");
            },
            logs,
            s =>
            {
                s.AddSingleton<IDistributedCache>(_redis);
                if (withLock)
                {
                    s.AddSingleton<IIdempotencyLock>(_lock);
                }
            },
            _log).Services;

    [Fact]
    public async Task Without_a_shared_lock_simultaneous_duplicates_on_two_instances_both_run()
    {
        var (a, b) = (Instance(withLock: false), Instance(withLock: false));

        await Task.WhenAll(a.Courier().Send(new PlaceOrder("ana", 10)), b.Courier().Send(new PlaceOrder("ana", 10)));

        Assert.Equal(2, _log.Entries.Count(e => e == "order ana")); // the gap the lock closes
    }

    [Fact]
    public async Task With_a_shared_lock_simultaneous_duplicates_on_two_instances_run_once()
    {
        var logs = new CapturingLoggerProvider();
        var (a, b) = (Instance(logs: logs), Instance(logs: logs));

        var results = await Task.WhenAll(
            a.Courier().Send(new PlaceOrder("ana", 10)),
            b.Courier().Send(new PlaceOrder("ana", 10)),
            a.Courier().Send(new PlaceOrder("ana", 10)),
            b.Courier().Send(new PlaceOrder("ana", 10)));

        Assert.Single(_log.Entries, e => e == "order ana");
        Assert.Single(results.Distinct());
        Assert.False(_lock.IsHeld);
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task A_recorded_result_is_returned_without_taking_the_lock()
    {
        var (a, b) = (Instance(), Instance());
        var first = await a.Courier().Send(new PlaceOrder("bia", 5));

        var again = await b.Courier().Send(new PlaceOrder("bia", 5));

        Assert.Equal(first, again);
        Assert.Equal(1, _lock.Acquisitions);
    }

    [Fact]
    public async Task A_duplicate_gives_up_with_409_when_the_other_instance_takes_too_long()
    {
        var b = Instance(o => o.IdempotencyLockWait = TimeSpan.FromMilliseconds(150));
        _lock.HoldEverything(b.GetRequiredService<CacheKeys>().ForIdempotency(new PlaceOrder("caio", 1)) + ":lock");

        var response = await b.Courier().Dispatch(new PlaceOrder("caio", 1));

        Assert.Equal(409, response.StatusCode);
        Assert.Equal("PlaceOrder is still running", response.Error);
        Assert.Equal("IDEMP-409", response.ErrorCode);
        Assert.DoesNotContain("order caio", _log.Entries);
    }

    [Fact]
    public async Task A_failure_releases_the_lock_and_is_not_recorded()
    {
        var (a, b) = (Instance(), Instance());

        var failed = await a.Courier().Send(new Charge("k-1", 0));
        var retried = await b.Courier().Send(new Charge("k-1", 0));

        Assert.Equal(400, failed.StatusCode);
        Assert.Equal(400, retried.StatusCode);
        Assert.Equal(2, _log.Entries.Count(e => e == "charge 0")); // failures run again, as without the lock
        Assert.False(_lock.IsHeld);
    }

    [Fact]
    public void Lock_options_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Instance(o => o.IdempotencyLockDuration = TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Instance(o => o.IdempotencyLockWait = TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AddRedisIdempotencyLock_registers_the_Redis_lock()
    {
        var services = new ServiceCollection();
        services.AddCourier(cfg => cfg.RegisterServicesFromAssemblyContaining<CallLog>().AddCaching().AddRedisIdempotencyLock(_ => null!));

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IIdempotencyLock));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [DockerFact]
    public async Task The_Redis_lock_is_exclusive_released_only_by_its_holder_and_expires()
    {
        await using var container = new RedisBuilder("redis:7.4-alpine").Build();
        await container.StartAsync();
        using var redis = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        var instanceA = new RedisIdempotencyLock(redis);
        var instanceB = new RedisIdempotencyLock(redis);

        var held = await instanceA.TryAcquireAsync("order-1:lock", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(held);
        Assert.Null(await instanceB.TryAcquireAsync("order-1:lock", TimeSpan.FromSeconds(30), CancellationToken.None));

        await held.DisposeAsync();
        var next = await instanceB.TryAcquireAsync("order-1:lock", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(next);

        // A holder whose lock expired must not release the lock someone else took since.
        var stale = await instanceA.TryAcquireAsync("order-2:lock", TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.NotNull(stale);
        await Task.Delay(400);
        var current = await instanceB.TryAcquireAsync("order-2:lock", TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(current);
        await stale.DisposeAsync();
        Assert.Null(await instanceA.TryAcquireAsync("order-2:lock", TimeSpan.FromSeconds(30), CancellationToken.None));
    }
}
