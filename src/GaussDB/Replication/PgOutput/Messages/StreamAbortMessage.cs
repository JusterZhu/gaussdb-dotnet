using HuaweiCloud.GaussDBTypes;
using System;

namespace HuaweiCloud.GaussDB.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol stream abort message for Logical Streaming Replication Protocol versions 2-3
/// </summary>
public class StreamAbortMessage : TransactionControlMessage
{
    /// <summary>
    /// Xid of the subtransaction (will be same as xid of the transaction for top-level transactions).
    /// </summary>
    public uint SubtransactionXid { get; private set; }

    internal StreamAbortMessage() {}

    internal StreamAbortMessage Populate(GaussDBLogSequenceNumber walStart, GaussDBLogSequenceNumber walEnd, DateTime serverClock,
        uint transactionXid, uint subtransactionXid)
    {
        base.Populate(walStart, walEnd, serverClock, transactionXid);
        SubtransactionXid = subtransactionXid;
        return this;
    }
}

/// <summary>
/// Logical Replication Protocol stream abort message for Logical Streaming Replication Protocol versions 4+
/// </summary>
public sealed class ParallelStreamAbortMessage : StreamAbortMessage
{
    /// <summary>
    /// The LSN of the abort.
    /// </summary>
    public GaussDBLogSequenceNumber AbortLsn { get; private set; }

    /// <summary>
    /// Abort timestamp of the transaction.
    /// </summary>
    public DateTime AbortTimestamp { get; private set; }

    internal ParallelStreamAbortMessage() {}

    internal ParallelStreamAbortMessage Populate(GaussDBLogSequenceNumber walStart, GaussDBLogSequenceNumber walEnd, DateTime serverClock,
        uint transactionXid, uint subtransactionXid, GaussDBLogSequenceNumber abortLsn, DateTime abortTimestamp)
    {
        base.Populate(walStart, walEnd, serverClock, transactionXid, subtransactionXid);
        AbortLsn = abortLsn;
        AbortTimestamp = abortTimestamp;
        return this;
    }
}
