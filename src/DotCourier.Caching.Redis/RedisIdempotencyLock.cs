using DotCourier.Caching;
using DotCourier.Caching.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace DotCourier.Caching.Redis
{
    /// <summary>
    /// <see cref="IIdempotencyLock"/> on Redis: <c>SET key token NX PX duration</c> takes it, and a script that
    /// deletes the key only if it still holds our token releases it, so an expired lock taken over by another
    /// instance is never released by mistake.
    /// </summary>
    public sealed class RedisIdempotencyLock(IConnectionMultiplexer redis) : IIdempotencyLock
    {
        private const string ReleaseScript = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            end
            return 0
            """;

        /// <inheritdoc />
        public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            cancellationToken.ThrowIfCancellationRequested();

            var database = redis.GetDatabase();
            var token = Guid.NewGuid().ToString("N");
            var taken = await database.StringSetAsync(key, token, duration, When.NotExists).ConfigureAwait(false);
            return taken ? new Handle(database, key, token) : null;
        }

        private sealed class Handle(IDatabase database, string key, string token) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync() =>
                await database.ScriptEvaluateAsync(ReleaseScript, [key], [token]).ConfigureAwait(false);
        }
    }
}

namespace DotCourier
{
    /// <summary>Adds the Redis idempotency lock to DotCourier.Caching.</summary>
    public static class CourierRedisCachingExtensions
    {
        /// <summary>
        /// Makes <see cref="IdempotentAttribute"/> requests run once across every instance sharing
        /// <paramref name="configuration"/> (a StackExchange.Redis connection string, e.g. <c>"localhost:6379"</c>).
        /// Requires <c>AddCaching()</c>. Use the same Redis as the shared cache level
        /// (<c>AddStackExchangeRedisCache</c>), so the result one instance records is the one the others read.
        /// </summary>
        public static CourierConfiguration AddRedisIdempotencyLock(this CourierConfiguration courier, string configuration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
            return courier.AddRedisIdempotencyLock(_ => ConnectionMultiplexer.Connect(configuration));
        }

        /// <summary>
        /// Same as <see cref="AddRedisIdempotencyLock(CourierConfiguration, string)"/>, with a connection you
        /// provide, e.g. <c>sp =&gt; sp.GetRequiredService&lt;IConnectionMultiplexer&gt;()</c> to share the application's one.
        /// </summary>
        public static CourierConfiguration AddRedisIdempotencyLock(this CourierConfiguration courier, Func<IServiceProvider, IConnectionMultiplexer> connection)
        {
            ArgumentNullException.ThrowIfNull(courier);
            ArgumentNullException.ThrowIfNull(connection);
            return courier.ConfigureServices(services =>
                services.TryAddSingleton<IIdempotencyLock>(sp => new RedisIdempotencyLock(connection(sp))));
        }
    }
}
