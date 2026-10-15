namespace DotCourier.Tests;

public class NotificationTests
{
    [Fact]
    public async Task Sequential_runs_every_handler_and_swallows_failures()
    {
        var (services, log) = TestHost.Build();

        await services.Courier().Publish(new ItemCreated("fail"));

        Assert.Equal(["first", "failing", "last"], log.Entries);
    }

    [Fact]
    public async Task StopOnFirstError_stops_and_reports_the_failing_handler()
    {
        var (services, log) = TestHost.Build(cfg => cfg.NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstError);

        var ex = await Assert.ThrowsAsync<NotificationHandlerException>(() => services.Courier().Publish(new ItemCreated("fail")));

        Assert.Equal(typeof(FailingItemCreatedHandler), ex.HandlerType);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(["first", "failing"], log.Entries);
    }

    [Fact]
    public async Task SequentialThenThrow_runs_every_handler_then_reports_the_failure()
    {
        var (services, log) = TestHost.Build(cfg => cfg.NotificationPublishStrategy = NotificationPublishStrategy.SequentialThenThrow);

        var ex = await Assert.ThrowsAsync<NotificationHandlerException>(() => services.Courier().Publish(new ItemCreated("fail")));

        Assert.Equal(typeof(FailingItemCreatedHandler), ex.HandlerType);
        Assert.Equal(["first", "failing", "last"], log.Entries);
    }

    [Fact]
    public async Task SequentialThenThrow_aggregates_several_failures()
    {
        var (services, log) = TestHost.Build(cfg => cfg.NotificationPublishStrategy = NotificationPublishStrategy.SequentialThenThrow);

        var ex = await Assert.ThrowsAsync<AggregateException>(() => services.Courier().Publish(new ItemCreated("fail twice")));

        Assert.Equal(
            [typeof(FailingItemCreatedHandler), typeof(LastItemCreatedHandler)],
            ex.InnerExceptions.Cast<NotificationHandlerException>().Select(e => e.HandlerType));
        Assert.Equal(["first", "failing", "last"], log.Entries);
    }

    [Fact]
    public async Task A_strategy_can_be_chosen_per_call()
    {
        var (services, log) = TestHost.Build(); // configured: Sequential, which swallows failures
        var courier = Assert.IsType<Courier>(services.Courier());

        await Assert.ThrowsAsync<NotificationHandlerException>(() => courier.Publish(new ItemCreated("fail"), NotificationPublishStrategy.SequentialThenThrow));
        await courier.Publish(new ItemCreated("fail")); // the configured strategy is unchanged

        Assert.Equal(["first", "failing", "last", "first", "failing", "last"], log.Entries);
    }

    [Fact]
    public async Task ParallelWhenAll_runs_every_handler()
    {
        var (services, log) = TestHost.Build(cfg => cfg.NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll);

        await services.Courier().Publish(new ItemCreated("fail"));

        Assert.Equal(["failing", "first", "last"], log.Entries.Order());
    }

    [Fact]
    public async Task Publish_by_object_uses_runtime_type()
    {
        var (services, log) = TestHost.Build();

        await services.Courier().Publish((object)new ItemCreated("ok"));

        Assert.Equal(["first", "second", "last"], log.Entries);
    }

    [Fact]
    public async Task Publish_without_handlers_is_a_no_op()
    {
        var (services, _) = TestHost.Build();

        await services.Courier().Publish(new NobodyListens());
    }

    [Fact]
    public async Task Publish_rejects_objects_that_are_not_notifications()
    {
        var (services, _) = TestHost.Build();

        await Assert.ThrowsAsync<ArgumentException>(() => services.Courier().Publish(new object()));
    }
}
