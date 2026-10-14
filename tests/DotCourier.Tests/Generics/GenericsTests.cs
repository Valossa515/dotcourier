using DotCourier.Behaviors;

namespace DotCourier.Tests.Generics;

public class GenericsTests
{
    [Fact]
    public async Task Handler_inheriting_from_a_generic_base_class_is_registered()
    {
        var (services, log) = TestHost.Build();

        Assert.Equal("renamed to x", await services.Courier().Send(new Rename("x")));
        Assert.Equal(["audit Rename"], log.Entries);
    }

    [Fact]
    public async Task Handler_through_a_custom_interface_is_registered()
    {
        var (services, _) = TestHost.Build();

        Assert.Equal("HI", await services.Courier().Send(new Shout("hi")));
    }

    [Fact]
    public async Task One_open_generic_handler_serves_every_closed_request()
    {
        var (services, log) = TestHost.Build();
        var courier = services.Courier();

        Assert.Equal(42, await courier.Send(new Create<int>(42)));
        Assert.Equal("book", await courier.Send(new Create<string>("book")));
        Assert.Equal(7, await courier.Send(new Create<int>(7)));

        Assert.Equal(["create Int32", "create String", "create Int32"], log.Entries);
    }

    [Fact]
    public async Task Open_generic_void_handler_works()
    {
        var (services, log) = TestHost.Build();

        await services.Courier().Send(new Delete<Invoice>(3));

        Assert.Equal(["delete Invoice 3"], log.Entries);
    }

    [Fact]
    public async Task Generic_requests_go_through_the_pipeline_and_Dispatch()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Create<string>("pen"));

        Assert.True(response.IsSuccess);
        Assert.Equal("pen", response.Data);
    }

    [Fact]
    public async Task Constraints_are_respected()
    {
        var (services, _) = TestHost.Build();
        var courier = services.Courier();

        Assert.Equal("archived Invoice", await courier.Send(new Archive<Invoice>(new Invoice())));

        var ex = await Assert.ThrowsAsync<HandlerNotFoundException>(() => courier.Send(new Archive<int>(5)));
        Assert.Contains("Archive<Int32>", ex.Message);
    }

    [Fact]
    public async Task Two_fitting_open_handlers_are_reported_instead_of_picking_one()
    {
        var (services, _) = TestHost.Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => services.Courier().Send(new Twin<int>(1)));

        Assert.Contains("TwinHandlerA<Int32>", ex.Message);
        Assert.Contains("TwinHandlerB<Int32>", ex.Message);
    }

    [Fact]
    public async Task Open_generic_notification_handler_applies_only_where_constraints_fit()
    {
        var (services, log) = TestHost.Build();
        var courier = services.Courier();

        await courier.Publish(new OrderShipped(1));
        await courier.Publish(new CacheWarmed());
        await courier.Publish(new ItemCreated("ok")); // not IAudited: only its own handlers run

        Assert.Equal(["audited OrderShipped", "first", "second", "last"], log.Entries);
    }

    [Fact]
    public async Task Open_generic_validator_applies_to_every_matching_request()
    {
        var (services, _) = TestHost.Build();

        var invalid = await services.Courier().Dispatch(new LoadCustomer(0));
        var valid = await services.Courier().Dispatch(new LoadCustomer(5));

        Assert.Equal(400, invalid.StatusCode);
        var error = Assert.Single(invalid.GetErrorDetails<IReadOnlyList<ValidationError>>()!);
        Assert.Equal("ID-001", error.Code);
        Assert.Equal("customer 5", valid.Data);
    }

    [Theory]
    [InlineData(typeof(Rename), "Rename")]
    [InlineData(typeof(Create<int>), "Create<Int32>")]
    [InlineData(typeof(Create<List<string>>), "Create<List<String>>")]
    [InlineData(typeof(Dictionary<string, Create<int>>), "Dictionary<String, Create<Int32>>")]
    public void Request_names_spell_out_generic_arguments(Type type, string expected)
    {
        Assert.Equal(expected, CourierDiagnostics.GetRequestName(type));
    }
}
