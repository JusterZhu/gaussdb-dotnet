using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HuaweiCloud.GaussDB.BackendMessages;
using HuaweiCloud.GaussDB.Internal;

namespace HuaweiCloud.GaussDB.Replication.PgOutput;

sealed class TupleEnumerator : IAsyncEnumerator<ReplicationValue>
{
    readonly ReplicationTuple _tupleEnumerable;
    readonly GaussDBReadBuffer _readBuffer;
    readonly ReplicationValue _value;

    ushort _numColumns;
    int _pos;
    RowDescriptionMessage _rowDescription = null!;
    CancellationToken _cancellationToken;

    internal TupleEnumerator(ReplicationTuple tupleEnumerable, GaussDBConnector connector)
    {
        _tupleEnumerable = tupleEnumerable;
        _readBuffer = connector.ReadBuffer;
        _value = new(connector);
    }

    internal void Reset(ushort numColumns, RowDescriptionMessage rowDescription, CancellationToken cancellationToken)
    {
        _pos = -1;
        _numColumns = numColumns;
        _rowDescription = rowDescription;
        _cancellationToken = cancellationToken;
    }

    public ValueTask<bool> MoveNextAsync()
    {
        if (_tupleEnumerable.State != RowState.Reading)
            throw new ObjectDisposedException(null);

        return MoveNextCore();

        async ValueTask<bool> MoveNextCore()
        {
            // Consume the previous column
            if (_pos != -1)
                await _value.Consume(_cancellationToken).ConfigureAwait(false);

            if (_pos + 1 == _numColumns)
                return false;
            _pos++;

            // Read the next column
            await _readBuffer.Ensure(1, async: true).ConfigureAwait(false);
            var kind = (TupleDataKind)_readBuffer.ReadByte();
            int len;
            switch (kind)
            {
            case TupleDataKind.Null:
            case TupleDataKind.UnchangedToastedValue:
                len = 0;
                break;
            case TupleDataKind.TextValue:
            case TupleDataKind.BinaryValue:
                await _readBuffer.Ensure(4, async: true).ConfigureAwait(false);
                len = _readBuffer.ReadInt32();
                break;
            default:
                throw new ArgumentOutOfRangeException();
            }

            _value.Reset(kind, len, _rowDescription[_pos]);

            return true;
        }
    }

    public ReplicationValue Current => _tupleEnumerable.State switch
    {
        RowState.NotRead => throw new ObjectDisposedException(null),
        RowState.Reading => _value,
        RowState.Consumed => throw new ObjectDisposedException(null),
        _ => throw new ArgumentOutOfRangeException()
    };

    public async ValueTask DisposeAsync()
    {
        if (_tupleEnumerable.State == RowState.Reading)
            while (await MoveNextAsync().ConfigureAwait(false)) { /* Do nothing, just iterate the enumerator */ }

        _tupleEnumerable.State = RowState.Consumed;
    }
}
