namespace DotCourier.Tests.Generics;

// --- handler inheriting from a generic abstract base -------------------------
public sealed record Rename(string Name) : ICommand<string>;

public abstract class AuditedHandler<TCommand, TResult>(CallLog log) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> Handle(TCommand command, CancellationToken ct)
    {
        log.Add("audit " + typeof(TCommand).Name);
        return await Execute(command, ct);
    }

    protected abstract Task<TResult> Execute(TCommand command, CancellationToken ct);
}

public sealed class RenameHandler(CallLog log) : AuditedHandler<Rename, string>(log)
{
    protected override Task<string> Execute(Rename command, CancellationToken ct) => Task.FromResult("renamed to " + command.Name);
}

// --- handler through a custom sub-interface -----------------------------------
public interface IStringHandler<TRequest> : IRequestHandler<TRequest, string>
    where TRequest : IRequest<string>;

public sealed record Shout(string Text) : IRequest<string>;

public sealed class ShoutHandler : IStringHandler<Shout>
{
    public Task<string> Handle(Shout request, CancellationToken ct) => Task.FromResult(request.Text.ToUpperInvariant());
}

// --- generic command, one open generic handler --------------------------------
public sealed record Create<T>(T Entity) : ICommand<T>;

public sealed class CreateHandler<T>(CallLog log) : ICommandHandler<Create<T>, T>
{
    public Task<T> Handle(Create<T> command, CancellationToken ct)
    {
        log.Add("create " + typeof(T).Name);
        return Task.FromResult(command.Entity);
    }
}

// --- generic void command ------------------------------------------------------
public sealed record Delete<T>(int Id) : ICommand;

public sealed class DeleteHandler<T>(CallLog log) : ICommandHandler<Delete<T>>
{
    public Task Handle(Delete<T> command, CancellationToken ct)
    {
        log.Add($"delete {typeof(T).Name} {command.Id}");
        return Task.CompletedTask;
    }
}

// --- constrained open handler ----------------------------------------------------
public interface IArchivable;

public sealed record Invoice : IArchivable;

public sealed record Archive<T>(T Item) : ICommand<string>;

public sealed class ArchiveHandler<T> : ICommandHandler<Archive<T>, string>
    where T : IArchivable
{
    public Task<string> Handle(Archive<T> command, CancellationToken ct) => Task.FromResult("archived " + typeof(T).Name);
}

// --- two open handlers fit the same request: ambiguity ----------------------------
public sealed record Twin<T>(T Value) : IQuery<string>;

public sealed class TwinHandlerA<T> : IQueryHandler<Twin<T>, string>
{
    public Task<string> Handle(Twin<T> query, CancellationToken ct) => Task.FromResult("A");
}

public sealed class TwinHandlerB<T> : IQueryHandler<Twin<T>, string>
{
    public Task<string> Handle(Twin<T> query, CancellationToken ct) => Task.FromResult("B");
}

// --- open generic notification handler (constrained, so other tests are unaffected) ---
public interface IAudited;

public sealed record OrderShipped(int OrderId) : INotification, IAudited;

public sealed record CacheWarmed : INotification;

public sealed class AuditNotifications<T>(CallLog log) : INotificationHandler<T>
    where T : INotification, IAudited
{
    public Task Handle(T notification, CancellationToken ct)
    {
        log.Add("audited " + typeof(T).Name);
        return Task.CompletedTask;
    }
}

// --- open generic validator for every request that carries an id ----------------------
public interface IHasId
{
    int Id { get; }
}

public sealed record LoadCustomer(int Id) : IQuery<string>, IHasId;

public sealed class LoadCustomerHandler : IQueryHandler<LoadCustomer, string>
{
    public Task<string> Handle(LoadCustomer query, CancellationToken ct) => Task.FromResult("customer " + query.Id);
}

public sealed class PositiveIdValidator<T> : IRequestValidator<T>
    where T : IHasId
{
    public ValueTask<IEnumerable<ValidationError>> ValidateAsync(T request, CancellationToken ct) =>
        ValueTask.FromResult<IEnumerable<ValidationError>>(request.Id > 0
            ? []
            : [new ValidationError(nameof(IHasId.Id), "Id must be positive", "ID-001")]);
}
