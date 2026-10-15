using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCourier.EntityFrameworkCore;

/// <summary>Options for <c>cfg.AddEntityFrameworkTransactions&lt;TDbContext&gt;(...)</c>.</summary>
public sealed class CourierTransactionOptions
{
    /// <summary>Isolation level of the transactions. Default <c>null</c>: the database provider's default.</summary>
    public IsolationLevel? IsolationLevel { get; set; }

    /// <summary>
    /// Wrap every request, not only commands and <see cref="TransactionalAttribute"/> requests. Useful
    /// when the application models writes as plain <see cref="IRequest{TResponse}"/> (MediatR style).
    /// <see cref="NotTransactionalAttribute"/> still opts out. Default <c>false</c>.
    /// </summary>
    public bool AllRequests { get; set; }
}

/// <summary>
/// The transaction DotCourier opened for the current command, if any. Inject it to run side effects
/// (e-mails, messages, cache writes) only once the data they describe is committed.
/// </summary>
public interface ICourierTransaction
{
    /// <summary>Whether a command transaction opened by DotCourier is in progress in this scope.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Runs <paramref name="action"/> after the current transaction commits, and never if it rolls
    /// back. Without an active transaction the action runs immediately. A failing action is logged and
    /// does not undo the commit.
    /// </summary>
    Task OnCommittedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}

/// <summary>Per-scope state of the DotCourier transaction: whether one is open and what to run after it commits.</summary>
internal sealed partial class CourierTransactionScope : ICourierTransaction
{
    private readonly List<Func<CancellationToken, Task>> _afterCommit = [];
    private readonly ILogger _logger;

    public CourierTransactionScope(ILogger<CourierTransactionScope>? logger = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    public bool IsActive { get; private set; }

    public Task OnCommittedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsActive)
        {
            return action(cancellationToken);
        }

        _afterCommit.Add(action);
        return Task.CompletedTask;
    }

    internal void Begin()
    {
        IsActive = true;
        _afterCommit.Clear();
    }

    internal void Discard()
    {
        IsActive = false;
        _afterCommit.Clear();
    }

    /// <summary>Runs the queued actions in order; the data is already committed, so failures are only logged.</summary>
    internal async Task CompleteAsync(string requestName, CancellationToken cancellationToken)
    {
        IsActive = false;
        var actions = _afterCommit.ToArray();
        _afterCommit.Clear();
        foreach (var action in actions)
        {
            try
            {
                await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogAfterCommitFailed(ex, requestName);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "[Courier] An after-commit action of {RequestType} failed; the transaction was already committed")]
    private partial void LogAfterCommitFailed(Exception exception, string requestType);
}

internal sealed record TransactionSettings(Type DbContextType, CourierTransactionOptions Options);
