using System;
using System.Threading;
using System.Threading.Tasks;
using HuaweiCloud.GaussDB.Internal;
using HuaweiCloud.GaussDBTypes;

namespace HuaweiCloud.GaussDB.Replication.PgOutput.Messages;

/// <summary>
/// Logical Replication Protocol update message for tables with REPLICA IDENTITY set to USING INDEX.
/// </summary>
public sealed class IndexUpdateMessage : UpdateMessage
{
    readonly ReplicationTuple _key;
    readonly SecondRowTupleEnumerable _newRow;

    /// <summary>
    /// Columns representing the key.
    /// </summary>
    public ReplicationTuple Key => _key;

    /// <summary>
    /// Columns representing the new row.
    /// </summary>
    public override ReplicationTuple NewRow => _newRow;

    internal IndexUpdateMessage(GaussDBConnector connector)
    {
        _key = new(connector);
        _newRow = new(connector, _key);
    }

    internal UpdateMessage Populate(
        GaussDBLogSequenceNumber walStart, GaussDBLogSequenceNumber walEnd, DateTime serverClock, uint? transactionXid,
        RelationMessage relation, ushort numColumns)
    {
        base.Populate(walStart, walEnd, serverClock, transactionXid, relation);

        _key.Reset(numColumns, relation.RowDescription);
        _newRow.Reset(numColumns, relation.RowDescription);

        return this;
    }

    internal Task Consume(CancellationToken cancellationToken)
        => _newRow.Consume(cancellationToken);
}
