using DotCourier.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotCourier.Tests.Caching;

public class CachingTests
{
    private static (ServiceProvider Services, CallLog Log) Build(Action<CourierCachingOptions>? options = null) =>
        TestHost.Build(cfg => cfg.AddCaching(options));

    [Fact]
    public async Task Cacheable_query_runs_the_handler_once()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        Assert.Equal("A1#1", await courier.Send(new GetPrice("A1")));
        Assert.Equal("A1#1", await courier.Send(new GetPrice("A1")));
        Assert.Equal("B2#1", await courier.Send(new GetPrice("B2")));

        Assert.Equal(["price A1", "price B2"], log.Entries);
    }

    [Fact]
    public async Task Keys_use_the_request_content_not_ToString()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        // Both records print "SearchProducts { Terms = System.Collections.Generic.List`1[System.String] }".
        Assert.Equal("a+b", await courier.Send(new SearchProducts(["a", "b"])));
        Assert.Equal("c", await courier.Send(new SearchProducts(["c"])));
        Assert.Equal("a+b", await courier.Send(new SearchProducts(["a", "b"])));

        Assert.Equal(2, log.Entries.Length);
    }

    [Fact]
    public async Task Explicit_cache_key_is_used_when_provided()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        await courier.Send(new GetByExplicitKey("acme", DateTime.UtcNow));
        await courier.Send(new GetByExplicitKey("acme", DateTime.UtcNow.AddMinutes(1)));

        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task Error_responses_are_not_cached_but_successes_round_trip()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        var first = await courier.Dispatch(new GetStock(0));
        var second = await courier.Dispatch(new GetStock(0));
        var okFirst = await courier.Dispatch(new GetStock(3));
        var okCached = await courier.Dispatch(new GetStock(3));

        Assert.Equal(404, first.StatusCode);
        Assert.Equal("STK-404", second.ErrorCode);
        Assert.Equal(okFirst, okCached);
        Assert.Equal(30, okCached.Data);
        Assert.Equal(["stock 0", "stock 0", "stock 3"], log.Entries);
    }

    [Fact]
    public async Task Exceptions_are_not_cached()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        await Assert.ThrowsAsync<InvalidOperationException>(() => courier.Send(new GetFlakyRate("USD")));
        Assert.Equal(5.2m, await courier.Send(new GetFlakyRate("USD")));
        Assert.Equal(5.2m, await courier.Send(new GetFlakyRate("USD")));

        Assert.Equal(2, log.Entries.Length);
    }

    [Fact]
    public async Task Unmarked_queries_are_cached_only_with_CacheAllQueries()
    {
        var (plain, plainLog) = Build();
        await plain.Courier().Send(new GetUnmarked(1));
        await plain.Courier().Send(new GetUnmarked(1));

        var (all, allLog) = Build(o => o.CacheAllQueries = true);
        await all.Courier().Send(new GetUnmarked(1));
        await all.Courier().Send(new GetUnmarked(1));

        Assert.Equal(2, plainLog.Entries.Length);
        Assert.Single(allLog.Entries);
    }

    [Fact]
    public async Task Entries_expire()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        await courier.Send(new GetShortLived(1));
        await Task.Delay(1_300);
        await courier.Send(new GetShortLived(1));

        Assert.Equal(2, log.Entries.Length);
    }

    [Fact]
    public async Task Successful_command_invalidates_the_listed_queries()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        await courier.Send(new GetPrice("A1"));
        await courier.Dispatch(new UpdatePrice("A1", Fail: true));   // failed: cache kept
        await courier.Send(new GetPrice("A1"));
        await courier.Dispatch(new UpdatePrice("A1", Fail: false));  // succeeded: cache dropped
        var refreshed = await courier.Send(new GetPrice("A1"));

        Assert.Equal("A1#2", refreshed);
        Assert.Equal(["price A1", "price A1"], log.Entries);
    }

    [Fact]
    public async Task Open_generic_invalidation_drops_every_closed_query()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        await courier.Send(new Lookup<Supplier>(1));
        await courier.Send(new Lookup<Warehouse>(1));
        await courier.Send(new Lookup<Supplier>(1));
        await courier.Send(new RefreshLookups());
        await courier.Send(new Lookup<Supplier>(1));
        await courier.Send(new Lookup<Warehouse>(1));

        Assert.Equal(["lookup Supplier", "lookup Warehouse", "lookup Supplier", "lookup Warehouse"], log.Entries);
    }

    [Fact]
    public async Task ICourierCache_invalidates_by_type_by_request_and_everything()
    {
        var (services, log) = Build();
        var courier = services.Courier();
        var cache = services.GetRequiredService<ICourierCache>();

        await courier.Send(new GetPrice("A1"));
        await courier.Send(new GetPrice("B2"));
        await cache.InvalidateAsync(new GetPrice("A1"));
        await courier.Send(new GetPrice("A1"));   // miss
        await courier.Send(new GetPrice("B2"));   // still cached

        await cache.InvalidateAsync<GetPrice>();
        await courier.Send(new GetPrice("B2"));   // miss

        await courier.Send(new GetStock(1));
        await cache.InvalidateAllAsync();
        await courier.Send(new GetStock(1));      // miss

        Assert.Equal(["price A1", "price B2", "price A1", "price B2", "stock 1", "stock 1"], log.Entries);
    }

    [Fact]
    public async Task Idempotent_command_runs_once_and_returns_the_recorded_result()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        var first = await courier.Send(new PlaceOrder("ana", 10));
        var duplicate = await courier.Send(new PlaceOrder("ana", 10));
        var other = await courier.Send(new PlaceOrder("bia", 10));

        Assert.Equal("order-1", first);
        Assert.Equal("order-1", duplicate);
        Assert.Equal("order-2", other);
        Assert.Equal(["order ana", "order bia"], log.Entries);
    }

    [Fact]
    public async Task Concurrent_duplicates_wait_for_the_first_execution()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => courier.Send(new PlaceOrder("carl", 99))));

        Assert.All(results, r => Assert.Equal("order-1", r));
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task Failed_idempotent_requests_are_not_recorded()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        var rejected = await courier.Dispatch(new Charge("key-1", -5));
        var accepted = await courier.Dispatch(new Charge("key-1", 50));

        Assert.Equal(400, rejected.StatusCode);
        Assert.Equal("charged 50", accepted.Data);
        Assert.Equal(["charge -5", "charge 50"], log.Entries);
    }

    [Fact]
    public async Task Explicit_idempotency_key_dedupes_requests_with_different_content()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        var first = await courier.Dispatch(new Charge("key-2", 50));
        var replay = await courier.Dispatch(new Charge("key-2", 70)); // same key, different body: answered from the record

        Assert.Equal("charged 50", replay.Data);
        Assert.Equal(first, replay);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task Forgetting_an_idempotent_result_lets_it_run_again()
    {
        var (services, log) = Build();
        var courier = services.Courier();

        await courier.Send(new PlaceOrder("dan", 1));
        await services.GetRequiredService<ICourierCache>().ForgetIdempotentResultAsync(new PlaceOrder("dan", 1));
        await courier.Send(new PlaceOrder("dan", 1));

        Assert.Equal(2, log.Entries.Length);
    }

    [Fact]
    public async Task Instances_sharing_a_distributed_cache_share_results()
    {
        // Stands in for Redis: one IDistributedCache shared by two "instances". (HybridCache ignores
        // MemoryDistributedCache itself as a second level, so it is wrapped.)
        var shared = new FakeRedis();
        var log = new CallLog();
        var (instanceA, _) = TestHost.Build(cfg => cfg.AddCaching(), registerServices: s => s.AddSingleton<IDistributedCache>(shared), sharedLog: log);
        var (instanceB, _) = TestHost.Build(cfg => cfg.AddCaching(), registerServices: s => s.AddSingleton<IDistributedCache>(shared), sharedLog: log);

        var fromA = await instanceA.Courier().Send(new GetPrice("Z9"));
        var fromB = await instanceB.Courier().Send(new GetPrice("Z9"));
        var orderA = await instanceA.Courier().Send(new PlaceOrder("eve", 5));
        var orderB = await instanceB.Courier().Send(new PlaceOrder("eve", 5));

        Assert.Equal(fromA, fromB);
        Assert.Equal(orderA, orderB);
        Assert.Equal(["price Z9", "order eve"], log.Entries);
    }

    [Fact]
    public async Task An_idempotent_result_reaches_the_shared_cache_before_the_response_returns()
    {
        // HybridCache writes the shared level in the background after handing the value back. With a
        // slow Redis, a retry sent to another instance right away would miss the record and run the
        // command again: the idempotency behavior must wait for that write.
        var shared = new FakeRedis(writeDelay: TimeSpan.FromMilliseconds(300));
        var log = new CallLog();
        var (instanceA, _) = TestHost.Build(cfg => cfg.AddCaching(), registerServices: s => s.AddSingleton<IDistributedCache>(shared), sharedLog: log);
        var (instanceB, _) = TestHost.Build(cfg => cfg.AddCaching(), registerServices: s => s.AddSingleton<IDistributedCache>(shared), sharedLog: log);

        var orderA = await instanceA.Courier().Send(new PlaceOrder("fay", 2));
        var orderB = await instanceB.Courier().Send(new PlaceOrder("fay", 2));

        Assert.Equal(orderA, orderB);
        Assert.Equal(["order fay"], log.Entries);
    }

    [Fact]
    public async Task Results_kept_out_of_the_cache_do_not_produce_error_logs()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = TestHost.Build(cfg => cfg.AddCaching(), logs);

        await services.Courier().Dispatch(new GetStock(0));          // error response: skipped
        await services.Courier().Dispatch(new Charge("key-9", -1));  // failed idempotent request: skipped

        // Only the logging behavior's usual "failed (status=...)" lines: nothing from HybridCache,
        // and the internal exception that carries the result out of the cache is never logged.
        Assert.DoesNotContain(logs.Entries, e => e.Exception is not null);
        Assert.DoesNotContain(logs.Entries, e => e.Category.StartsWith("Microsoft.Extensions.Caching", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_options_fail_at_startup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.DefaultCacheDuration = TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => Build(o => o.KeyPrefix = " "));
    }
}

/// <summary>An out-of-process cache stand-in: a plain IDistributedCache that HybridCache treats as a real L2.</summary>
/// <summary>An in-memory stand-in for Redis; <paramref name="writeDelay"/> imitates a slow network write.</summary>
internal sealed class FakeRedis(TimeSpan writeDelay = default) : IDistributedCache
{
    private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

    public byte[]? Get(string key) => _inner.Get(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _inner.Set(key, value, options);

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (writeDelay > TimeSpan.Zero)
        {
            await Task.Delay(writeDelay, CancellationToken.None);
        }

        await _inner.SetAsync(key, value, options, token);
    }

    public void Refresh(string key) => _inner.Refresh(key);

    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

    public void Remove(string key) => _inner.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
}
