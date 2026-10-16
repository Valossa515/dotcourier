using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotCourier.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Tests.Caching;

[JsonSerializable(typeof(GetStock))]
[JsonSerializable(typeof(Response<int>))]
internal sealed partial class CachingJson : JsonSerializerContext;

/// <summary>Counts which types the options were asked about.</summary>
internal sealed class CountingResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    public ConcurrentBag<Type> Asked { get; } = [];

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        Asked.Add(type);
        return inner.GetTypeInfo(type, options);
    }
}

/// <summary>
/// Under Native AOT, cached results and keys must go through the application's source-generated JSON context
/// (ResultSerializerOptions), never through reflection.
/// </summary>
public class SerializerOptionsTests
{
    [Fact]
    public async Task Results_and_keys_go_through_the_given_options()
    {
        var resolver = new CountingResolver(CachingJson.Default);
        var options = new JsonSerializerOptions { TypeInfoResolver = resolver };
        var shared = new FakeRedis(); // a shared level, so results are really serialized
        var log = new CallLog();
        var (a, _) = TestHost.Build(cfg => cfg.AddCaching(o => o.ResultSerializerOptions = options), registerServices: s => s.AddSingleton<IDistributedCache>(shared), sharedLog: log);
        var (b, _) = TestHost.Build(cfg => cfg.AddCaching(o => o.ResultSerializerOptions = options), registerServices: s => s.AddSingleton<IDistributedCache>(shared), sharedLog: log);

        var fromA = await a.Courier().Send(new GetStock(4));
        var fromB = await b.Courier().Send(new GetStock(4)); // read back from the shared level by another instance

        Assert.Equal(40, fromA.Data);
        Assert.Equal(fromA.Data, fromB.Data);
        Assert.Single(log.Entries);
        Assert.Contains(typeof(GetStock), resolver.Asked);      // the key
        Assert.Contains(typeof(Response<int>), resolver.Asked); // the stored result
    }

    [Fact]
    public async Task Types_the_options_do_not_know_fall_back_to_the_default_serializer()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = CachingJson.Default };
        var (services, log) = TestHost.Build(
            cfg => cfg.AddCaching(o =>
            {
                o.ResultSerializerOptions = options;
                o.KeySerializerOptions = new JsonSerializerOptions(); // GetPrice is not in CachingJson
            }),
            registerServices: s => s.AddSingleton<IDistributedCache>(new FakeRedis()));

        Assert.Equal(await services.Courier().Send(new GetPrice("Z1")), await services.Courier().Send(new GetPrice("Z1")));
        Assert.Single(log.Entries);
    }
}
