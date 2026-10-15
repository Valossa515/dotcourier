using DotCourier.EntityFrameworkCore;
using DotCourier.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DotCourier.Tests.EntityFrameworkCore;

public sealed class Item
{
    public int Id { get; set; }

    public required string Name { get; set; }
}

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>().HasIndex(i => i.Name).IsUnique();
        modelBuilder.AddCourierOutbox();
    }
}

/// <summary>A SQLite in-memory database that lives as long as the open connection.</summary>
internal sealed class TestDatabase : IDisposable
{
    // Shared cache lets SQLite honour ReadUncommitted; the unique name keeps parallel tests apart.
    private readonly SqliteConnection _connection = new($"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    public TestDatabase()
    {
        _connection.Open();
        using var db = new ShopDb(new DbContextOptionsBuilder<ShopDb>().UseSqlite(_connection).Options);
        db.Database.EnsureCreated();
    }

    public void Register(IServiceCollection services) => services.AddDbContext<ShopDb>(o => o.UseSqlite(_connection));

    public string[] ItemNames()
    {
        using var db = new ShopDb(new DbContextOptionsBuilder<ShopDb>().UseSqlite(_connection).Options);
        return db.Items.OrderBy(i => i.Id).Select(i => i.Name).ToArray();
    }

    public OutboxMessage[] OutboxRows()
    {
        using var db = new ShopDb(new DbContextOptionsBuilder<ShopDb>().UseSqlite(_connection).Options);
        return db.Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.CreatedAt).ToArray();
    }

    public void AddOutboxRow(OutboxMessage message)
    {
        using var db = new ShopDb(new DbContextOptionsBuilder<ShopDb>().UseSqlite(_connection).Options);
        db.Set<OutboxMessage>().Add(message);
        db.SaveChanges();
    }

    public SqliteConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}

internal static class Db
{
    // Test handlers resolve these lazily: every test host scans this assembly, but only the EF tests register ShopDb.
    public static ShopDb Shop(this IServiceProvider services) => services.GetRequiredService<ShopDb>();

    public static ICourierTransaction Transaction(this IServiceProvider services) => services.GetRequiredService<ICourierTransaction>();

    public static async Task AddItem(this IServiceProvider services, string name, CancellationToken ct)
    {
        var db = services.Shop();
        db.Items.Add(new Item { Name = name });
        await db.SaveChangesAsync(ct);
    }

    public static string Describe(this IServiceProvider services) =>
        services.Shop().Database.CurrentTransaction is null ? "no transaction" : "in transaction";
}

// --- commands ------------------------------------------------------------------------

public sealed record AddItems(string[] Names, string? FailWith = null) : ICommand<int>;

public sealed class AddItemsHandler(IServiceProvider services, CallLog log) : ICommandHandler<AddItems, int>
{
    public async Task<int> Handle(AddItems command, CancellationToken ct)
    {
        log.Add(services.Describe());
        foreach (var name in command.Names)
        {
            await services.AddItem(name, ct);
        }

        await services.Transaction().OnCommittedAsync(_ =>
        {
            log.Add("after commit: " + string.Join(",", command.Names));
            return Task.CompletedTask;
        }, ct);

        return command.FailWith is null ? command.Names.Length : throw new InvalidOperationException(command.FailWith);
    }
}

public sealed record AddItemOrFail(string Name, bool Fail) : ICommand<Response<int>>;

public sealed class AddItemOrFailHandler(IServiceProvider services, CallLog log) : ICommandHandler<AddItemOrFail, Response<int>>
{
    public async Task<Response<int>> Handle(AddItemOrFail command, CancellationToken ct)
    {
        await services.AddItem(command.Name, ct);
        await services.Transaction().OnCommittedAsync(_ =>
        {
            log.Add("after commit");
            return Task.CompletedTask;
        }, ct);
        return command.Fail ? Response.Conflict("name taken", code: "ITEM-409") : Response.Created(1);
    }
}

/// <summary>Adds <c>Outer</c>, then sends <see cref="AddItems"/> from inside its own handler.</summary>
public sealed record AddWithNested(string Outer, string Inner, bool FailAfterInner) : ICommand<string>;

public sealed class AddWithNestedHandler(IServiceProvider services, ISender sender, CallLog log) : ICommandHandler<AddWithNested, string>
{
    public async Task<string> Handle(AddWithNested command, CancellationToken ct)
    {
        await services.AddItem(command.Outer, ct);
        var outerTransaction = services.Shop().Database.CurrentTransaction;
        await sender.Send(new AddItems([command.Inner]), ct);
        log.Add(ReferenceEquals(outerTransaction, services.Shop().Database.CurrentTransaction) ? "same transaction" : "other transaction");
        return command.FailAfterInner ? throw new InvalidOperationException("outer failed") : "ok";
    }
}

[NotTransactional]
public sealed record AddItemOutsideTransaction(string Name) : ICommand;

public sealed class AddItemOutsideTransactionHandler(IServiceProvider services, CallLog log) : ICommandHandler<AddItemOutsideTransaction>
{
    public async Task Handle(AddItemOutsideTransaction command, CancellationToken ct)
    {
        log.Add(services.Describe());
        await services.AddItem(command.Name, ct);
        throw new InvalidOperationException("after save");
    }
}

/// <summary>Tracks an entity without saving it and fails on the first attempt; succeeds on the next.</summary>
public sealed record AddItemFlaky(string Name) : ICommand<int>;

public sealed class AddItemFlakyHandler(IServiceProvider services, CallLog log) : ICommandHandler<AddItemFlaky, int>
{
    public async Task<int> Handle(AddItemFlaky command, CancellationToken ct)
    {
        var db = services.Shop();
        log.Add($"tracked {db.ChangeTracker.Entries().Count()}");
        db.Items.Add(new Item { Name = command.Name + log.Entries.Length });
        if (log.Entries.Length == 1)
        {
            throw new TimeoutException("transient failure");
        }

        return await db.SaveChangesAsync(ct);
    }
}

public sealed record IsolationProbe : ICommand<string>;

public sealed class IsolationProbeHandler(IServiceProvider services) : ICommandHandler<IsolationProbe, string>
{
    public Task<string> Handle(IsolationProbe command, CancellationToken ct) =>
        Task.FromResult(services.Shop().Database.CurrentTransaction!.GetDbTransaction().IsolationLevel.ToString());
}

public sealed record FailingAfterCommit : ICommand<string>;

public sealed class FailingAfterCommitHandler(IServiceProvider services, CallLog log) : ICommandHandler<FailingAfterCommit, string>
{
    public async Task<string> Handle(FailingAfterCommit command, CancellationToken ct)
    {
        await services.AddItem("kept", ct);
        await services.Transaction().OnCommittedAsync(_ => throw new InvalidOperationException("smtp down"), ct);
        await services.Transaction().OnCommittedAsync(_ =>
        {
            log.Add("second action");
            return Task.CompletedTask;
        }, ct);
        return "done";
    }
}

// --- non-command requests ------------------------------------------------------------

public sealed record CountItems : IQuery<int>;

public sealed class CountItemsHandler(IServiceProvider services, CallLog log) : IQueryHandler<CountItems, int>
{
    public async Task<int> Handle(CountItems query, CancellationToken ct)
    {
        log.Add(services.Describe());
        await services.Transaction().OnCommittedAsync(_ =>
        {
            log.Add("ran immediately");
            return Task.CompletedTask;
        }, ct);
        return await services.Shop().Items.CountAsync(ct);
    }
}

/// <summary>A MediatR-style write modelled as a plain request.</summary>
public sealed record RenameItem(string Name) : IRequest<string>;

public sealed class RenameItemHandler(IServiceProvider services) : IRequestHandler<RenameItem, string>
{
    public Task<string> Handle(RenameItem request, CancellationToken ct) => Task.FromResult(services.Describe());
}

[Transactional]
public sealed record ImportItem(string Name) : IRequest<string>;

public sealed class ImportItemHandler(IServiceProvider services) : IRequestHandler<ImportItem, string>
{
    public Task<string> Handle(ImportItem request, CancellationToken ct) => Task.FromResult(services.Describe());
}
