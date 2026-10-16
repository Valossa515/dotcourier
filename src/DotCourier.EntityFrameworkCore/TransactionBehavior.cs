using System.Reflection;
using DotCourier.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier.EntityFrameworkCore;

/// <summary>
/// Runs commands (and <see cref="TransactionalAttribute"/> requests) in a database transaction that
/// commits on success and rolls back when the handler throws or returns an error response.
/// </summary>
/// <remarks>
/// <para>A request dispatched while a transaction is already open on the same <see cref="DbContext"/>
/// (e.g. a command sent from inside another command's handler) joins it instead of opening a second one.</para>
/// <para>The work runs through the context's execution strategy, so providers configured with
/// <c>EnableRetryOnFailure</c> work. A rollback clears the change tracker (like JPA detaching the
/// persistence context), so a retry does not re-save the failed attempt's entities and nothing keeps
/// treating rolled-back rows as persisted.</para>
/// </remarks>
internal sealed partial class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly bool OptedOut = typeof(TRequest).GetCustomAttribute<NotTransactionalAttribute>(inherit: true) is not null;
    private static readonly bool OptedIn = typeof(TRequest).GetCustomAttribute<TransactionalAttribute>(inherit: true) is not null
        || typeof(ICommand<TResponse>).IsAssignableFrom(typeof(TRequest));
    private static readonly string RequestName = CourierDiagnostics.GetRequestName(typeof(TRequest));

    private readonly IServiceProvider _services;
    private readonly TransactionSettings _settings;
    private readonly CourierTransactionScope _scope;
    private readonly ILogger _logger;

    public TransactionBehavior(
        IServiceProvider services,
        TransactionSettings settings,
        CourierTransactionScope scope,
        ILogger<TransactionBehavior<TRequest, TResponse>>? logger = null)
    {
        _services = services;
        _settings = settings;
        _scope = scope;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (OptedOut || !(OptedIn || _settings.Options.AllRequests))
        {
            return next(cancellationToken);
        }

        var db = (DbContext)_services.GetRequiredService(_settings.DbContextType);
        if (db.Database.CurrentTransaction is not null)
        {
            return next(cancellationToken);
        }

        return Run(db, next, cancellationToken);
    }

    private async Task<TResponse> Run(DbContext db, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Every failed attempt clears the change tracker, so an execution-strategy retry starts clean.
        var strategy = db.Database.CreateExecutionStrategy();
        var (response, committed) = await strategy.ExecuteAsync(
            (db, next),
            (state, ct) => RunInTransaction(state.db, state.next, ct),
            cancellationToken).ConfigureAwait(false);

        if (committed)
        {
            await _scope.CompleteAsync(RequestName, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<(TResponse Response, bool Committed)> RunInTransaction(DbContext db, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var isolation = _settings.Options.IsolationLevel;
        await using var transaction = (isolation is { } level
            ? await db.Database.BeginTransactionAsync(level, cancellationToken).ConfigureAwait(false)
            : await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
        _scope.Begin();

        TResponse response;
        try
        {
            response = await next(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await Rollback(db, transaction).ConfigureAwait(false);
            LogRolledBack(RequestName, "exception");
            throw;
        }

        if (response is IResponseStatus { IsSuccess: false })
        {
            await Rollback(db, transaction).ConfigureAwait(false);
            LogRolledBack(RequestName, "error response");
            return (response, false);
        }

        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _scope.Discard();
            db.ChangeTracker.Clear();
            throw;
        }

        LogCommitted(RequestName);
        return (response, true);
    }

    private async Task Rollback(DbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        _scope.Discard();
        try
        {
            // Not the caller's token: a cancelled request must still release its locks.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // Entities saved in the transaction still look persisted to EF; forget them so they are not reused.
            db.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Courier] Transaction committed for {RequestType}")]
    private partial void LogCommitted(string requestType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[Courier] Transaction rolled back for {RequestType} ({Reason})")]
    private partial void LogRolledBack(string requestType, string reason);
}
