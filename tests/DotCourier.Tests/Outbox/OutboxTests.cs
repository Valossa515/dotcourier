using DotCourier.Outbox;
using DotCourier.Tests.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotCourier.Tests.Outbox;

public sealed class OutboxTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly ManualTime _time = new();

    public void Dispose() => _database.Dispose();

    private (ServiceProvider Services, CallLog Log) Build(
        Action<CourierOutboxOptions>? outbox = null,
        Action<IServiceCollection>? services = null,
        CapturingLoggerProvider? logs = null,
        bool manualTime = true) =>
        TestHost.Build(
            cfg => cfg.AddEntityFrameworkTransactions<ShopDb>().AddOutbox<ShopDb>(outbox),
            logs,
            s =>
            {
                _database.Register(s);
                if (manualTime)
                {
                    s.AddSingleton<TimeProvider>(_time);
                }

                services?.Invoke(s);
            });

    private static Task<int> ProcessOnce(ServiceProvider services) =>
        services.GetRequiredService<OutboxProcessor<ShopDb>>().ProcessOnceAsync(CancellationToken.None);

    private static async Task<T> InScope<T>(ServiceProvider services, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_committed_command_stores_its_message_as_pending()
    {
        var (services, log) = Build();

        await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem("pen")));

        var message = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal("DotCourier.Tests.Outbox.ItemShipped, DotCourier.Tests", message.EventType);
        Assert.Equal("""{"name":"pen"}""", message.Payload);
        Assert.Equal(_time.Now.UtcDateTime, message.CreatedAt);
        Assert.Empty(log.Entries); // nothing is delivered inside the command
    }

    [Fact]
    public async Task A_rolled_back_command_leaves_no_message()
    {
        var (services, _) = Build();

        var response = await InScope(services, sp => sp.GetRequiredService<ICourier>().Dispatch(new ShipItem("pen", FailAfterSave: true)));

        Assert.False(response.IsSuccess);
        Assert.Empty(_database.ItemNames());
        Assert.Empty(_database.OutboxRows());
    }

    [Fact]
    public async Task Add_waits_for_SaveChanges_and_PublishAsync_saves()
    {
        var (services, _) = Build();
        await using var scope = services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();

        outbox.Add(new ItemShipped("added"));
        Assert.Empty(_database.OutboxRows());

        await outbox.PublishAsync(new ItemShipped("published"));
        Assert.Equal(2, _database.OutboxRows().Length);
    }

    [Fact]
    public async Task The_processor_delivers_pending_messages_to_the_handlers()
    {
        var (services, log) = Build();
        await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem("pen")));
        _time.Now = _time.Now.AddSeconds(3);

        Assert.Equal(1, await ProcessOnce(services));

        Assert.Equal(["shipped pen"], log.Entries);
        var message = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Processed, message.Status);
        Assert.Equal(_time.Now.UtcDateTime, message.ProcessedAt);

        Assert.Equal(0, await ProcessOnce(services)); // delivered once
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task Generic_notifications_round_trip()
    {
        var (services, log) = Build();
        await InScope(services, async sp =>
        {
            await sp.GetRequiredService<IOutbox>().PublishAsync(new Envelope<ItemShipped>(new ItemShipped("cup"), 2));
            return 0;
        });

        await ProcessOnce(services);

        Assert.Equal(["envelope cup v2"], log.Entries);
        Assert.DoesNotContain("Version=", Assert.Single(_database.OutboxRows()).EventType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_delivery_is_retried_until_MaxAttempts_then_marked_Failed()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = Build(o => o.MaxAttempts = 3, logs: logs);
        await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem("poison-pill")));

        await ProcessOnce(services);
        var afterFirst = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Pending, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);
        Assert.Equal(
            "NotificationHandlerException: Notification handler 'ItemShippedHandler' failed for 'ItemShipped' ---> InvalidOperationException: cannot ship poison-pill",
            afterFirst.LastError);

        await ProcessOnce(services);
        await ProcessOnce(services);
        var failed = Assert.Single(_database.OutboxRows());
        Assert.Equal(OutboxStatus.Failed, failed.Status);
        Assert.Equal(3, failed.Attempts);
        Assert.Equal(0, await ProcessOnce(services)); // never tried again

        Assert.Equal(2, logs.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("attempt", StringComparison.Ordinal)));
        Assert.Single(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("marked Failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_notification_types_are_restored_from_the_database()
    {
        var (services, log) = Build(o => o.MaxAttempts = 1);
        _database.AddOutboxRow(OutboxMessage.Create("System.IO.FileInfo, System.Runtime", """{"originalPath":"x"}""", _time.Now.UtcDateTime));
        _database.AddOutboxRow(OutboxMessage.Create("No.Such.Type, Nowhere", "{}", _time.Now.UtcDateTime));

        await ProcessOnce(services);

        var rows = _database.OutboxRows();
        Assert.All(rows, r => Assert.Equal(OutboxStatus.Failed, r.Status));
        Assert.Contains(rows, r => r.LastError!.Contains("is not an INotification", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.LastError!.Contains("was not found", StringComparison.Ordinal));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Abandoned_messages_are_delivered_again_after_the_processing_timeout()
    {
        var (services, log) = Build(o => o.ProcessingTimeout = TimeSpan.FromMinutes(1));
        await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem("pen")));
        await MarkProcessing(services); // as if an instance claimed it and crashed

        _time.Now = _time.Now.AddSeconds(30);
        Assert.Equal(0, await ProcessOnce(services)); // still within the timeout

        _time.Now = _time.Now.AddSeconds(31);
        Assert.Equal(1, await ProcessOnce(services));
        Assert.Equal(["shipped pen"], log.Entries);
        Assert.Equal(OutboxStatus.Processed, Assert.Single(_database.OutboxRows()).Status);
    }

    [Fact]
    public async Task A_message_claimed_by_another_instance_is_skipped()
    {
        var (services, _) = Build();
        await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem("pen")));

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
        var stale = await db.Set<OutboxMessage>().SingleAsync(); // read as Pending
        await ProcessOnce(services); // another instance delivers it meanwhile

        var claimed = await OutboxProcessor<ShopDb>.TryUpdate(db, stale, m => m.Status = OutboxStatus.Processing, CancellationToken.None);

        Assert.False(claimed);
        Assert.Equal(OutboxStatus.Processed, Assert.Single(_database.OutboxRows()).Status);
    }

    [Fact]
    public async Task A_cycle_takes_at_most_BatchSize_messages_oldest_first()
    {
        var (services, log) = Build(o => o.BatchSize = 2);
        foreach (var name in new[] { "a", "b", "c" })
        {
            await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem(name)));
            _time.Now = _time.Now.AddSeconds(1);
        }

        Assert.Equal(2, await ProcessOnce(services));
        Assert.Equal(["shipped a", "shipped b"], log.Entries);
        Assert.Equal(1, await ProcessOnce(services));
    }

    [Fact]
    public async Task A_custom_dispatcher_replaces_in_process_delivery()
    {
        var (services, log) = Build(services: s => s.AddScoped<IOutboxDispatcher, RecordingDispatcher>());
        await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem("pen")));

        await ProcessOnce(services);

        Assert.Equal(["dispatched DotCourier.Tests.Outbox.ItemShipped, DotCourier.Tests {\"name\":\"pen\"}"], log.Entries);
    }

    [Fact]
    public async Task The_processor_explains_a_missing_outbox_mapping()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(new CallLog())
            .AddDbContext<UnmappedDb>(o => o.UseSqlite(_database.Connection))
            .AddCourier(cfg => cfg.RegisterServicesFromAssemblyContaining<CallLog>().AddOutbox<UnmappedDb>())
            .BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<OutboxProcessor<UnmappedDb>>().ProcessOnceAsync(CancellationToken.None));

        Assert.Contains("modelBuilder.AddCourierOutbox()", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_running_processor_wakes_up_on_commit_instead_of_waiting_for_the_poll()
    {
        var (services, log) = Build(o => o.PollInterval = TimeSpan.FromMinutes(10), manualTime: false);
        var hosted = services.GetServices<IHostedService>().Single();
        await hosted.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(100); // let the first (empty) cycle finish and start waiting
            await InScope(services, sp => sp.GetRequiredService<ICourier>().Send(new ShipItem("in command")));
            await WaitFor(log, "shipped in command");

            await InScope(services, async sp =>
            {
                await sp.GetRequiredService<IOutbox>().PublishAsync(new ItemShipped("outside"));
                return 0;
            });
            await WaitFor(log, "shipped outside");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void RunProcessor_false_registers_no_background_service()
    {
        var (services, _) = Build(o => o.RunProcessor = false);

        Assert.Empty(services.GetServices<IHostedService>());
        Assert.NotNull(services.CreateScope().ServiceProvider.GetRequiredService<IOutbox>());
    }

    [Fact]
    public void Invalid_options_are_rejected_at_startup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.BatchSize = 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.MaxAttempts = 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(o => o.PollInterval = TimeSpan.Zero));
    }

    private async Task MarkProcessing(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
        var message = await db.Set<OutboxMessage>().SingleAsync();
        message.Status = OutboxStatus.Processing;
        message.UpdatedAt = _time.Now.UtcDateTime;
        await db.SaveChangesAsync();
    }

    private static async Task WaitFor(CallLog log, string entry)
    {
        for (var i = 0; i < 100 && !log.Entries.Contains(entry); i++)
        {
            await Task.Delay(50);
        }

        Assert.Contains(entry, log.Entries);
    }
}

public sealed class UnmappedDb(DbContextOptions<UnmappedDb> options) : DbContext(options);
