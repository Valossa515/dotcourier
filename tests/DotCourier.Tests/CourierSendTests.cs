using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Tests;

public class CourierSendTests
{
    [Fact]
    public async Task Send_returns_handler_result()
    {
        var (services, log) = TestHost.Build();

        var result = await services.Courier().Send(new Ping("hi"));

        Assert.Equal("pong: hi", result);
        Assert.Contains("handler", log.Entries);
    }

    [Fact]
    public async Task Send_handles_void_commands()
    {
        var (services, log) = TestHost.Build();

        var result = await services.Courier().Send(new CreateItem("book"));

        Assert.Equal(Unit.Value, result);
        Assert.Equal(["created book"], log.Entries);
    }

    [Fact]
    public async Task Send_by_object_uses_runtime_type()
    {
        var (services, _) = TestHost.Build();

        var result = await services.Courier().Send((object)new Ping("boxed"));

        Assert.Equal("pong: boxed", result);
    }

    [Fact]
    public async Task Send_propagates_exceptions_like_MediatR()
    {
        var (services, _) = TestHost.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Courier().Send(new Boom(false)));
        await Assert.ThrowsAsync<HandlerNotFoundException>(() => services.Courier().Send(new NoHandler()));
    }

    [Fact]
    public void MediatR_interfaces_resolve_to_the_courier()
    {
        var (services, _) = TestHost.Build();

        Assert.IsType<Courier>(services.GetRequiredService<IMediator>());
        Assert.IsType<Courier>(services.GetRequiredService<ISender>());
        Assert.IsType<Courier>(services.GetRequiredService<IPublisher>());
    }

    [Fact]
    public void AddCourier_requires_an_assembly_to_scan()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCourier(_ => { }));
    }

    [Fact]
    public async Task Pre_and_post_processors_wrap_the_handler()
    {
        var (services, log) = TestHost.Build();

        await services.Courier().Send(new Ping("x"));

        Assert.Equal(["pre", "handler", "post:pong: x"], log.Entries);
    }

    [Fact]
    public async Task Exception_handler_for_a_base_type_supplies_the_response()
    {
        var (services, _) = TestHost.Build();

        var result = await services.Courier().Send(new Fragile());

        Assert.Equal("fallback after broken", result);
    }
}
