using System.Data;
using DotCourier.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCourier.Tests.EntityFrameworkCore;

public sealed class TransactionTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private (ServiceProvider Services, CallLog Log) Build(
        Action<CourierTransactionOptions>? transactions = null,
        Action<CourierConfiguration>? configure = null,
        CapturingLoggerProvider? logs = null) =>
        TestHost.Build(
            cfg =>
            {
                cfg.AddEntityFrameworkTransactions<ShopDb>(transactions);
                configure?.Invoke(cfg);
            },
            logs,
            _database.Register);

    /// <summary>Each call gets its own DI scope, like an HTTP request.</summary>
    private static async Task<T> InScope<T>(ServiceProvider services, Func<ICourier, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICourier>());
    }

    [Fact]
    public async Task Commands_commit_when_the_handler_succeeds()
    {
        var (services, log) = Build();

        var count = await InScope(services, c => c.Send(new AddItems(["pen", "ink"])));

        Assert.Equal(2, count);
        Assert.Equal(["pen", "ink"], _database.ItemNames());
        Assert.Equal(["in transaction", "after commit: pen,ink"], log.Entries);
    }

    [Fact]
    public async Task An_exception_rolls_back_everything_the_handler_saved()
    {
        var (services, log) = Build();

        var response = await InScope(services, c => c.Dispatch(new AddItems(["pen", "ink"], FailWith: "boom")));

        Assert.False(response.IsSuccess);
        Assert.Equal(500, response.StatusCode);
        Assert.Empty(_database.ItemNames());
        Assert.DoesNotContain(log.Entries, e => e.StartsWith("after commit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Send_still_throws_the_handler_exception_after_rolling_back()
    {
        var (services, _) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(services, c => c.Send(new AddItems(["pen"], FailWith: "boom"))));

        Assert.Empty(_database.ItemNames());
    }

    [Fact]
    public async Task An_error_response_rolls_back_without_an_exception()
    {
        var (services, log) = Build();

        var response = await InScope(services, c => c.Send(new AddItemOrFail("pen", Fail: true)));

        Assert.Equal(409, response.StatusCode);
        Assert.Equal("ITEM-409", response.ErrorCode);
        Assert.Empty(_database.ItemNames());
        Assert.Empty(log.Entries);

        var created = await InScope(services, c => c.Send(new AddItemOrFail("pen", Fail: false)));
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(["pen"], _database.ItemNames());
        Assert.Equal(["after commit"], log.Entries);
    }

    [Fact]
    public async Task Queries_are_not_wrapped_and_after_commit_actions_run_immediately()
    {
        var (services, log) = Build();

        var count = await InScope(services, c => c.Send(new CountItems()));

        Assert.Equal(0, count);
        Assert.Equal(["no transaction", "ran immediately"], log.Entries);
    }

    [Fact]
    public async Task NotTransactional_commands_keep_what_they_saved()
    {
        var (services, log) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => InScope(services, async c =>
        {
            await c.Send(new AddItemOutsideTransaction("pen"));
            return 0;
        }));

        Assert.Equal(["no transaction"], log.Entries);
        Assert.Equal(["pen"], _database.ItemNames());
    }

    [Fact]
    public async Task Plain_requests_are_wrapped_only_when_marked_or_with_AllRequests()
    {
        var (services, _) = Build();
        Assert.Equal("no transaction", await InScope(services, c => c.Send(new RenameItem("x"))));
        Assert.Equal("in transaction", await InScope(services, c => c.Send(new ImportItem("x"))));

    }

    [Fact]
    public async Task AllRequests_wraps_every_request_except_NotTransactional_ones()
    {
        var (services, log) = Build(o => o.AllRequests = true);

        Assert.Equal("in transaction", await InScope(services, c => c.Send(new RenameItem("x"))));
        await InScope(services, c => c.Send(new CountItems()));
        await InScope(services, c => c.Dispatch(new AddItemOutsideTransaction("pen")));

        Assert.Equal(["in transaction", "ran immediately", "no transaction"], log.Entries);
    }

    [Fact]
    public async Task A_nested_command_joins_the_outer_transaction()
    {
        var (services, log) = Build();

        var response = await InScope(services, c => c.Dispatch(new AddWithNested("outer", "inner", FailAfterInner: true)));

        Assert.False(response.IsSuccess);
        Assert.Empty(_database.ItemNames());
        Assert.Contains("same transaction", log.Entries);
        Assert.DoesNotContain(log.Entries, e => e.StartsWith("after commit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_nested_command_after_commit_action_waits_for_the_outer_commit()
    {
        var (services, log) = Build();

        await InScope(services, c => c.Send(new AddWithNested("outer", "inner", FailAfterInner: false)));

        Assert.Equal(["outer", "inner"], _database.ItemNames());
        Assert.Equal(["in transaction", "same transaction", "after commit: inner"], log.Entries);
    }

    [Fact]
    public async Task A_retry_starts_from_a_clean_change_tracker()
    {
        var (services, log) = Build(configure: cfg => cfg.AddResilience(o =>
        {
            o.Retry.Enabled = true;
            o.Retry.MaxAttempts = 3;
            o.Retry.Delay = TimeSpan.Zero;
            o.Retry.UseJitter = false;
        }));

        var saved = await InScope(services, c => c.Send(new AddItemFlaky("pen")));

        Assert.Equal(1, saved);
        Assert.Equal(["tracked 0", "tracked 0"], log.Entries);
        Assert.Equal(["pen2"], _database.ItemNames());
    }

    [Fact]
    public async Task The_isolation_level_is_applied()
    {
        var (defaults, _) = Build();
        Assert.Equal(nameof(IsolationLevel.Serializable), await InScope(defaults, c => c.Send(new IsolationProbe())));

        var (configured, _) = Build(o => o.IsolationLevel = IsolationLevel.ReadUncommitted);
        Assert.Equal(nameof(IsolationLevel.ReadUncommitted), await InScope(configured, c => c.Send(new IsolationProbe())));
    }

    [Fact]
    public async Task A_failing_after_commit_action_is_logged_and_does_not_undo_the_commit()
    {
        var logs = new CapturingLoggerProvider();
        var (services, log) = Build(logs: logs);

        var result = await InScope(services, c => c.Send(new FailingAfterCommit()));

        Assert.Equal("done", result);
        Assert.Equal(["kept"], _database.ItemNames());
        Assert.Equal(["second action"], log.Entries);
        var error = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("FailingAfterCommit", error.Message, StringComparison.Ordinal);
        Assert.Equal("smtp down", error.Exception?.Message);
    }

    [Fact]
    public async Task Commits_and_rollbacks_are_logged()
    {
        var logs = new CapturingLoggerProvider();
        var (services, _) = Build(logs: logs);

        await InScope(services, c => c.Send(new AddItems(["pen"])));
        await InScope(services, c => c.Dispatch(new AddItems(["ink"], FailWith: "boom")));
        await InScope(services, c => c.Send(new AddItemOrFail("cap", Fail: true)));

        var messages = logs.Entries.Where(e => e.Category.Contains("TransactionBehavior", StringComparison.Ordinal)).Select(e => e.Message).ToArray();
        Assert.Equal(
            [
                "[Courier] Transaction committed for AddItems",
                "[Courier] Transaction rolled back for AddItems (exception)",
                "[Courier] Transaction rolled back for AddItemOrFail (error response)",
            ],
            messages);
    }

    [Fact]
    public async Task A_rollback_leaves_no_open_transaction_behind_in_the_scope()
    {
        var (services, _) = Build();
        await using var scope = services.CreateAsyncScope();
        var courier = scope.ServiceProvider.GetRequiredService<ICourier>();
        var transaction = scope.ServiceProvider.GetRequiredService<ICourierTransaction>();

        await courier.Dispatch(new AddItems(["pen"], FailWith: "boom"));
        var ran = false;
        await transaction.OnCommittedAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.False(transaction.IsActive);
        Assert.True(ran);
        Assert.Equal(1, await courier.Send(new AddItems(["ink"])));
        Assert.Equal(["ink"], _database.ItemNames());
    }

    [Fact]
    public async Task OnCommittedAsync_outside_any_request_runs_immediately()
    {
        var (services, _) = Build();
        await using var scope = services.CreateAsyncScope();
        var ran = false;

        await scope.ServiceProvider.GetRequiredService<ICourierTransaction>().OnCommittedAsync(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.True(ran);
        Assert.False(scope.ServiceProvider.GetRequiredService<ICourierTransaction>().IsActive);
    }
}
