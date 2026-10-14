namespace DotCourier.Tests;

public class CourierDispatchTests
{
    [Fact]
    public async Task Dispatch_wraps_the_result_in_a_successful_response()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Ping("hi"));

        Assert.True(response.IsSuccess);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("pong: hi", response.Data);
    }

    [Fact]
    public async Task Dispatch_does_not_double_wrap_handlers_that_return_responses()
    {
        var (services, _) = TestHost.Build();

        // Assigning to Response<string> (not Response<Response<string>>) proves the right overload binds.
        Response<string> found = await services.Courier().Dispatch(new GetItem(7));
        Response<string> missing = await services.Courier().Dispatch(new GetItem(0));

        Assert.Equal("item 7", found.Data);
        Assert.False(missing.IsSuccess);
        Assert.Equal(404, missing.StatusCode);
        Assert.Equal("Item 0 not found", missing.Error);
    }

    [Fact]
    public async Task Dispatch_hides_messages_of_unexpected_exceptions()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Boom(false));

        Assert.False(response.IsSuccess);
        Assert.Equal(500, response.StatusCode);
        Assert.Equal("An internal error occurred", response.Error);
        Assert.DoesNotContain("hunter2", response.ToString());
        Assert.Equal(nameof(InvalidOperationException), response.ExceptionType);
    }

    [Fact]
    public async Task Dispatch_propagates_courier_exception_message_and_status()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new Boom(true));

        Assert.Equal(409, response.StatusCode);
        Assert.Equal("Out of stock", response.Error);
    }

    [Fact]
    public async Task Dispatch_reports_missing_handlers_as_an_error_response()
    {
        var (services, _) = TestHost.Build();

        var response = await services.Courier().Dispatch(new NoHandler());

        Assert.Equal(500, response.StatusCode);
        Assert.Contains(nameof(NoHandler), response.Error);
    }

    [Fact]
    public async Task Dispatch_rethrows_cancellation_requested_by_the_caller()
    {
        var (services, _) = TestHost.Build(cfg => cfg.EnableTimeouts = false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.Courier().Dispatch(new Slow(1_000), cts.Token));
    }
}
