using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using HuaweiCloud.GaussDB.Internal;

namespace HuaweiCloud.GaussDB;

/// <summary>
/// Represents a transaction to be made in a PostgreSQL database. This class cannot be inherited.
/// </summary>
public sealed class GaussDBTransaction : DbTransaction
{
    #region Fields and Properties

    /// <summary>
    /// Specifies the <see cref="GaussDBConnection"/> object associated with the transaction.
    /// </summary>
    /// <value>The <see cref="GaussDBConnection"/> object associated with the transaction.</value>
    public new GaussDBConnection? Connection
    {
        get
        {
            CheckDisposed();
            return _connector?.Connection;
        }
    }

    // Note that with ambient transactions, it's possible for a transaction to be pending after its connection
    // is already closed. So we capture the connector and perform everything directly on it.
    GaussDBConnector _connector;

    /// <summary>
    /// Specifies the <see cref="GaussDBConnection"/> object associated with the transaction.
    /// </summary>
    /// <value>The <see cref="GaussDBConnection"/> object associated with the transaction.</value>
    protected override DbConnection? DbConnection => Connection;

    /// <summary>
    /// If true, the transaction has been committed/rolled back, but not disposed.
    /// </summary>
    internal bool IsCompleted => _connector is null || _connector.TransactionStatus == TransactionStatus.Idle;

    internal bool IsDisposed;

    Exception? _disposeReason;

    /// <summary>
    /// Specifies the isolation level for this transaction.
    /// </summary>
    /// <value>The isolation level for this transaction. The default is <see cref="System.Data.IsolationLevel.ReadCommitted"/>.</value>
    public override IsolationLevel IsolationLevel
    {
        get
        {
            CheckReady();
            return _isolationLevel;
        }
    }
    IsolationLevel _isolationLevel;

    readonly ILogger _transactionLogger;

    const IsolationLevel DefaultIsolationLevel = IsolationLevel.ReadCommitted;

    #endregion

    #region Initialization

    internal GaussDBTransaction(GaussDBConnector connector)
    {
        _connector = connector;
        _transactionLogger = connector.TransactionLogger;
    }

    internal void Init(IsolationLevel isolationLevel = DefaultIsolationLevel)
    {
        Debug.Assert(isolationLevel != IsolationLevel.Chaos);

        if (!_connector.DatabaseInfo.SupportsTransactions)
            return;

        switch (isolationLevel)
        {
        case IsolationLevel.RepeatableRead:
        case IsolationLevel.Snapshot:
            _connector.PrependInternalMessage(PregeneratedMessages.BeginTransRepeatableRead, 2);
            break;
        case IsolationLevel.Serializable:
            _connector.PrependInternalMessage(PregeneratedMessages.BeginTransSerializable, 2);
            break;
        case IsolationLevel.ReadUncommitted:
            // PG doesn't really support ReadUncommitted, it's the same as ReadCommitted. But we still
            // send as if.
            _connector.PrependInternalMessage(PregeneratedMessages.BeginTransReadUncommitted, 2);
            break;
        case IsolationLevel.ReadCommitted:
            _connector.PrependInternalMessage(PregeneratedMessages.BeginTransReadCommitted, 2);
            break;
        case IsolationLevel.Unspecified:
            isolationLevel = DefaultIsolationLevel;
            goto case DefaultIsolationLevel;
        default:
            throw new NotSupportedException("Isolation level not supported: " + isolationLevel);
        }

        _connector.TransactionStatus = TransactionStatus.Pending;
        _isolationLevel = isolationLevel;
        IsDisposed = false;

        LogMessages.StartedTransaction(_transactionLogger, isolationLevel, _connector.Id);
    }

    #endregion

    #region Commit

    /// <summary>
    /// Commits the database transaction.
    /// </summary>
    public override void Commit() => Commit(false).GetAwaiter().GetResult();

    async Task Commit(bool async, CancellationToken cancellationToken = default)
    {
        CheckReady();

        if (!_connector.DatabaseInfo.SupportsTransactions)
            return;

        using (_connector.StartUserAction(cancellationToken))
        {
            await _connector.ExecuteInternalCommand(PregeneratedMessages.CommitTransaction, async, cancellationToken).ConfigureAwait(false);
            LogMessages.CommittedTransaction(_transactionLogger, _connector.Id);
        }
    }

    /// <summary>
    /// Commits the database transaction.
    /// </summary>
    /// <param name="cancellationToken">
    /// An optional token to cancel the asynchronous operation. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    public override Task CommitAsync(CancellationToken cancellationToken = default)
        => Commit(async: true, cancellationToken);

    #endregion

    #region Rollback

    /// <summary>
    /// Rolls back a transaction from a pending state.
    /// </summary>
    public override void Rollback() => Rollback(false).GetAwaiter().GetResult();

    async Task Rollback(bool async, CancellationToken cancellationToken = default)
    {
        CheckReady();

        if (!_connector.DatabaseInfo.SupportsTransactions)
            return;

        using (_connector.StartUserAction(cancellationToken))
        {
            await _connector.Rollback(async, cancellationToken).ConfigureAwait(false);
            LogMessages.RolledBackTransaction(_transactionLogger, _connector.Id);
        }
    }

    /// <summary>
    /// Rolls back a transaction from a pending state.
    /// </summary>
    /// <param name="cancellationToken">
    /// An optional token to cancel the asynchronous operation. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    public override Task RollbackAsync(CancellationToken cancellationToken = default)
        => Rollback(async: true, cancellationToken);

    #endregion

    #region Savepoints

    /// <summary>
    /// Creates a transaction save point.
    /// </summary>
    /// <param name="name">The name of the savepoint.</param>
    /// <remarks>
    /// This method does not cause a database roundtrip to be made. The savepoint creation statement will instead be sent along with
    /// the next command.
    /// </remarks>
    public override void Save(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CheckReady();
        if (!_connector.DatabaseInfo.SupportsTransactions)
            return;

        // Note that creating a savepoint doesn't actually send anything to the backend (only prepends), so strictly speaking we don't
        // have to start a user action. However, we do this for consistency as if we did (for the checks and exceptions)
        using var _ = _connector.StartUserAction();

        LogMessages.CreatingSavepoint(_transactionLogger, name, _connector.Id);

        if (RequiresQuoting(name))
            name = $"\"{name.Replace("\"", "\"\"")}\"";

        // Note: savepoint names are PostgreSQL identifiers, and so limited by default to 63 characters.
        // Since we are prepending, we assume below that the statement will always fit in the buffer.
        _connector.WriteQuery("SAVEPOINT " + name, async: false).GetAwaiter().GetResult();

        _connector.PendingPrependedResponses += 2;
    }

    /// <summary>
    /// Creates a transaction save point.
    /// </summary>
    /// <param name="name">The name of the savepoint.</param>
    /// <param name="cancellationToken">
    /// An optional token to cancel the asynchronous operation. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    /// <remarks>
    /// This method does not cause a database roundtrip to be made, and will therefore always complete synchronously.
    /// The savepoint creation statement will instead be sent along with the next command.
    /// </remarks>
    public override Task SaveAsync(string name, CancellationToken cancellationToken = default)
    {
        Save(name);
        return Task.CompletedTask;
    }

    async Task Rollback(bool async, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CheckReady();
        if (!_connector.DatabaseInfo.SupportsTransactions)
            return;
        using (_connector.StartUserAction(cancellationToken))
        {
            var quotedName = RequiresQuoting(name) ? $"\"{name.Replace("\"", "\"\"")}\"" : name;
            await _connector.ExecuteInternalCommand($"ROLLBACK TO SAVEPOINT {quotedName}", async, cancellationToken).ConfigureAwait(false);
            LogMessages.RolledBackToSavepoint(_transactionLogger, name, _connector.Id);
        }
    }

    /// <summary>
    /// Rolls back a transaction from a pending savepoint state.
    /// </summary>
    /// <param name="name">The name of the savepoint.</param>
    public override void Rollback(string name)
        => Rollback(async: false, name).GetAwaiter().GetResult();

    /// <summary>
    /// Rolls back a transaction from a pending savepoint state.
    /// </summary>
    /// <param name="name">The name of the savepoint.</param>
    /// <param name="cancellationToken">
    /// An optional token to cancel the asynchronous operation. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    public override Task RollbackAsync(string name, CancellationToken cancellationToken = default)
        => Rollback(async: true, name, cancellationToken);

    async Task Release(bool async, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        CheckReady();
        if (!_connector.DatabaseInfo.SupportsTransactions)
            return;
        using (_connector.StartUserAction(cancellationToken))
        {
            var quotedName = RequiresQuoting(name) ? $"\"{name.Replace("\"", "\"\"")}\"" : name;
            await _connector.ExecuteInternalCommand($"RELEASE SAVEPOINT {quotedName}", async, cancellationToken).ConfigureAwait(false);
            LogMessages.ReleasedSavepoint(_transactionLogger, name, _connector.Id);
        }
    }

    /// <summary>
    /// Releases a transaction from a pending savepoint state.
    /// </summary>
    /// <param name="name">The name of the savepoint.</param>
    public override void Release(string name)
        => Release(async: false, name).GetAwaiter().GetResult();

    /// <summary>
    /// Releases a transaction from a pending savepoint state.
    /// </summary>
    /// <param name="name">The name of the savepoint.</param>
    /// <param name="cancellationToken">
    /// An optional token to cancel the asynchronous operation. The default value is <see cref="CancellationToken.None"/>.
    /// </param>
    public override Task ReleaseAsync(string name, CancellationToken cancellationToken = default)
        => Release(async: false, name, cancellationToken);

    /// <summary>
    /// Indicates whether this transaction supports database savepoints.
    /// </summary>
    public override bool SupportsSavepoints => _connector.DatabaseInfo.SupportsTransactions;

    #endregion

    #region Dispose

    /// <summary>
    /// Disposes the transaction, rolling it back if it is still pending.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (disposing)
        {
            if (!IsCompleted)
            {
                try
                {
                    _connector.CloseOngoingOperations(async: false).GetAwaiter().GetResult();
                    Rollback();
                }
                catch
                {
                    Debug.Assert(_connector.IsBroken);
                }
            }

            IsDisposed = true;
            _connector?.Connection?.EndBindingScope(ConnectorBindingScope.Transaction);
        }
    }

    /// <summary>
    /// Disposes the transaction, rolling it back if it is still pending.
    /// </summary>
    public override ValueTask DisposeAsync()
    {
        if (!IsDisposed)
        {
            if (!IsCompleted)
            {
                return DisposeAsyncInternal();
            }

            IsDisposed = true;
            _connector?.Connection?.EndBindingScope(ConnectorBindingScope.Transaction);
        }
        return default;

        async ValueTask DisposeAsyncInternal()
        {
            // We're disposing, so no cancellation token
            try
            {
                await _connector.CloseOngoingOperations(async: true).ConfigureAwait(false);
                await Rollback(async: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.Assert(_connector.IsBroken);
                LogMessages.ExceptionDuringTransactionDispose(_transactionLogger, _connector.Id, ex);
            }

            IsDisposed = true;
            _connector?.Connection?.EndBindingScope(ConnectorBindingScope.Transaction);
        }
    }

    /// <summary>
    /// Disposes the transaction, without rolling back. Used only in special circumstances, e.g. when
    /// the connection is broken.
    /// </summary>
    internal void DisposeImmediately(Exception? disposeReason)
    {
        IsDisposed = true;
        _disposeReason = disposeReason;
    }

    #endregion

    #region Checks

    void CheckReady()
    {
        CheckDisposed();
        if (IsCompleted)
            ThrowHelper.ThrowInvalidOperationException("This GaussDBTransaction has completed; it is no longer usable.");
    }

    void CheckDisposed()
    {
        if (IsDisposed)
            ThrowHelper.ThrowObjectDisposedException(nameof(GaussDBTransaction), _disposeReason);
    }

    static bool RequiresQuoting(string identifier)
    {
        Debug.Assert(identifier.Length > 0);

        var first = identifier[0];
        if (first != '_' && !char.IsLower(first))
            return true;

        foreach (var c in identifier.AsSpan(1))
            if (c != '_' && c != '$' && !char.IsLower(c) && !char.IsDigit(c))
                return true;

        return false;
    }

    #endregion

    #region Misc

    /// <summary>
    /// Unbinds transaction from the connector.
    /// Should be called before the connector is returned to the pool.
    /// </summary>
    internal void UnbindIfNecessary()
    {
        // We're closing the connection, but transaction is not yet disposed
        // We have to unbind the transaction from the connector, otherwise there could be a concurrency issues
        // See #3306
        if (!IsDisposed)
        {
            if (_connector.UnboundTransaction is { IsDisposed: true } previousTransaction)
            {
                previousTransaction._connector = _connector;
                _connector.Transaction = previousTransaction;
            }
            else
                _connector.Transaction = null;

            _connector.UnboundTransaction = this;
            _connector = null!;
        }
    }

    #endregion
}
