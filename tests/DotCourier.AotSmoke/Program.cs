using System.Text;
using System.Text.Json;
using DotCourier;
using DotCourier.AotSmoke;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// DotCourier under Native AOT: core features, Resilience and ASP.NET Core results. Compile-time mode is
// on by default under AOT; it is also forced here so a regular (JIT) run exercises the same paths.
// (PublishAot also turns dynamic code off for a plain `dotnet run`, to surface AOT problems early.)
Console.WriteLine($"Dynamic code: {(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "available" : "unavailable")}");
var log = new CallLog();
var services = new ServiceCollection()
    .AddSingleton(log)
    .AddCourier(cfg => cfg
        .RequireCompileTimeOnly()
        .RegisterServicesFromAssemblyContaining<Ping>()
        .AddOpenBehavior(typeof(TraceBehavior<,>), order: 10)
        .AddOpenStreamBehavior(typeof(TraceStreamBehavior<,>))
        .AddResilience(o => o.ForRequest<Flaky>(r =>
        {
            r.Retry.Enabled = true;
            r.Retry.MaxAttempts = 3;
            r.Retry.Delay = TimeSpan.Zero;
            r.Retry.UseJitter = false;
        })))
    .ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, SmokeJson.Default))
    .BuildServiceProvider();

var checks = new Checks();
await using (var scope = services.CreateAsyncScope())
{
    var courier = scope.ServiceProvider.GetRequiredService<ICourier>();

    checks.Equal("compile-time mode is on", true, scope.ServiceProvider.GetRequiredService<CourierOptions>().RequireCompileTimeRegistrations);

    log.Clear();
    checks.Equal("send (reference response)", "pong: hi", await courier.Send(new Ping("hi")));
    checks.Equal("  ...pre/post processors and behaviors in order", "pre,trace>,handler,<trace,post", log.Joined);

    log.Clear();
    checks.Equal("send (value-type response)", 5, await courier.Send(new Add(2, 3)));
    checks.Equal("  ...behavior closed for a value type", "trace>,<trace", log.Joined);

    var invalid = await courier.Dispatch(new Register(null, "x", 3));
    checks.Equal("DataAnnotations on a positional record -> 400", 400, invalid.StatusCode);
    checks.Equal("  ...one error per field", "Name,Email,Age", string.Join(",", invalid.GetErrorDetails<IReadOnlyList<ValidationError>>()?.Select(e => e.Field) ?? []));

    checks.Equal("exception handler supplies the response", "fallback: broken", await courier.Send(new Fragile()));
    checks.Equal("CourierException -> status code", 409, (await courier.Dispatch(new Conflict())).StatusCode);
    checks.Equal("missing handler -> error response", false, (await courier.Dispatch(new Orphan())).IsSuccess);
    checks.Equal("timeout -> 504", 504, (await courier.Dispatch(new Slow())).StatusCode);

    log.Clear();
    await courier.Publish(new Shipped("box"));
    checks.Equal("notification: closed handlers, then the open generic one", "first box,second box,audit Shipped", log.Joined);

    log.Clear();
    await courier.Publish(new Pinged());
    checks.Equal("notification with only an open generic handler", "audit Pinged", log.Joined);

    log.Clear();
    var items = new List<int>();
    await foreach (var item in courier.CreateStream(new Count(3)))
    {
        items.Add(item);
    }

    checks.Equal("stream", "1,2,3", string.Join(",", items));
    checks.Equal("  ...stream behavior", "stream>,<stream", log.Joined);

    var json = JsonSerializer.Serialize(await courier.Dispatch(new Ping("json")), SmokeJson.Default.ResponseString);
    checks.Equal("Response<T> with source-generated JSON", """{"data":"pong: json","error":null,"success":true,"statusCode":200}""", json);

    checks.Equal("resilience: retry absorbs two failures", "ok after 3", await courier.Send(new Flaky()));

    var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    using var body = new MemoryStream();
    http.Response.Body = body;
    await (await courier.Dispatch(new Conflict())).ToResult().ExecuteAsync(http);
    checks.Equal("ASP.NET Core ToResult(): status code", 409, http.Response.StatusCode);
    checks.Equal("  ...JSON body from the app's JSON options", """{"data":null,"error":"taken","success":false,"statusCode":409}""", Encoding.UTF8.GetString(body.ToArray()));
}

return checks.Report();

internal static class SmokeConfiguration
{
    public static CourierConfiguration RequireCompileTimeOnly(this CourierConfiguration configuration)
    {
        configuration.RequireCompileTimeRegistrations = true;
        return configuration;
    }
}
