using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HuaweiCloud.GaussDB.BackendMessages;
using HuaweiCloud.GaussDB.Internal;
using HuaweiCloud.GaussDB.Internal.Postgres;
using NUnit.Framework;

namespace HuaweiCloud.GaussDB.Tests.Support;

class PgServerMock : IDisposable
{
    static uint BoolOid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Bool).Value;
    static uint Int4Oid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Int4).Value;
    static uint TextOid => PostgresMinimalDatabaseInfo.DefaultTypeCatalog.GetOid(DataTypeNames.Text).Value;

    static readonly Encoding Encoding = GaussDBWriteBuffer.UTF8Encoding;

    readonly NetworkStream _stream;
    readonly GaussDBReadBuffer _readBuffer;
    readonly GaussDBWriteBuffer _writeBuffer;
    bool _disposed;

    const int BackendSecret = 12345;
    internal int ProcessId { get; }

    internal GaussDBReadBuffer ReadBuffer => _readBuffer;
    internal GaussDBWriteBuffer WriteBuffer => _writeBuffer;

    internal PgServerMock(
        NetworkStream stream,
        GaussDBReadBuffer readBuffer,
        GaussDBWriteBuffer writeBuffer,
        int processId)
    {
        ProcessId = processId;
        _stream = stream;
        _readBuffer = readBuffer;
        _writeBuffer = writeBuffer;
        writeBuffer.MessageLengthValidation = false;
    }

    internal async Task Startup(MockState state)
    {
        // Read and skip the startup message
        await SkipMessage();

        WriteAuthenticateOk();
        var parameters = new Dictionary<string, string>
        {
            { "server_version", "14" },
            { "server_encoding", "UTF8" },
            { "client_encoding", "UTF8" },
            { "application_name", "Mock" },
            { "is_superuser", "on" },
            { "session_authorization", "foo" },
            { "DateStyle", "ISO, MDY" },
            { "IntervalStyle", "postgres" },
            { "TimeZone", "UTC" },
            { "integer_datetimes", "on" },
            { "standard_conforming_strings", "on" }
        };
        // While PostgreSQL 14 always sends default_transaction_read_only and in_hot_standby, we only send them if requested
        // To minimize potential issues for tests not requiring multiple hosts
        if (state != MockState.MultipleHostsDisabled)
        {
            parameters["default_transaction_read_only"] = state == MockState.Primary ? "off" : "on";
            parameters["in_hot_standby"] = state == MockState.Standby ? "on" : "off";
        }
        WriteParameterStatuses(parameters);
        WriteBackendKeyData(ProcessId, BackendSecret);
        WriteReadyForQuery();
        await FlushAsync();
    }

    internal async Task FailedStartup(string errorCode)
    {
        // Read and skip the startup message
        await SkipMessage();
        WriteErrorResponse(errorCode);
        await FlushAsync();
    }

    internal Task SendMockState(MockState state)
    {
        var isStandby = state == MockState.Standby;
        var transactionReadOnly = state == MockState.Standby || state == MockState.PrimaryReadOnly
            ? "on"
            : "off";

        return WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(BoolOid))
            .WriteDataRow(BitConverter.GetBytes(isStandby))
            .WriteCommandComplete()
            .WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(TextOid))
            .WriteDataRow(Encoding.ASCII.GetBytes(transactionReadOnly))
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();
    }

    internal async Task SkipMessage()
    {
        await _readBuffer.EnsureAsync(4);
        var len = _readBuffer.ReadInt32();
        await _readBuffer.EnsureAsync(len - 4);
        _readBuffer.Skip(len - 4);
    }

    internal async Task ExpectMessage(byte expectedCode)
    {
        CheckDisposed();

        await _readBuffer.EnsureAsync(5);
        var actualCode = _readBuffer.ReadByte();
        Assert.That(actualCode, Is.EqualTo(expectedCode),
            $"Expected message of type '{(char)expectedCode}' but got '{(char)actualCode}'");
        var len = _readBuffer.ReadInt32();
        _readBuffer.Skip(len - 4);
    }

    internal Task ExpectExtendedQuery()
        => ExpectMessages(
            FrontendMessageCode.Parse,
            FrontendMessageCode.Bind,
            FrontendMessageCode.Describe,
            FrontendMessageCode.Execute,
            FrontendMessageCode.Sync);

    internal async Task ExpectMessages(params byte[] expectedCodes)
    {
        foreach (var expectedCode in expectedCodes)
            await ExpectMessage(expectedCode);
    }

    internal async Task ExpectSimpleQuery(string expectedSql)
    {
        CheckDisposed();

        await _readBuffer.EnsureAsync(5);
        var actualCode = _readBuffer.ReadByte();
        Assert.That(actualCode, Is.EqualTo(FrontendMessageCode.Query), $"Expected message of type Query but got '{(char)actualCode}'");
        _ = _readBuffer.ReadInt32();
        var actualSql = _readBuffer.ReadNullTerminatedString();
        Assert.That(actualSql, Is.EqualTo(expectedSql));
    }

    internal Task WaitForData() => _readBuffer.EnsureAsync(1).AsTask();

    internal Task FlushAsync()
    {
        CheckDisposed();
        return _writeBuffer.Flush(async: true);
    }

    internal Task WriteScalarResponseAndFlush(int value)
        => WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(Int4Oid))
            .WriteDataRow(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(value)))
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();

    internal Task WriteScalarResponseAndFlush(bool value)
        => WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(BoolOid))
            .WriteDataRow(BitConverter.GetBytes(value))
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();

    internal Task WriteScalarResponseAndFlush(string value)
        => WriteParseComplete()
            .WriteBindComplete()
            .WriteRowDescription(new FieldDescription(TextOid))
            .WriteDataRow(Encoding.ASCII.GetBytes(value))
            .WriteCommandComplete()
            .WriteReadyForQuery()
            .FlushAsync();

    internal void Close() => _stream.Close();

    #region Low-level message writing

    internal PgServerMock WriteParseComplete()
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.ParseComplete);
        _writeBuffer.WriteInt32(4);
        return this;
    }

    internal PgServerMock WriteBindComplete()
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.BindComplete);
        _writeBuffer.WriteInt32(4);
        return this;
    }

    internal PgServerMock WriteRowDescription(params FieldDescription[] fields)
    {
        CheckDisposed();

        _writeBuffer.WriteByte((byte)BackendMessageCode.RowDescription);
        _writeBuffer.WriteInt32(4 + 2 + fields.Sum(f => Encoding.GetByteCount(f.Name) + 1 + 18));
        _writeBuffer.WriteInt16((short)fields.Length);

        foreach (var field in fields)
        {
            _writeBuffer.WriteNullTerminatedString(field.Name);
            _writeBuffer.WriteUInt32(field.TableOID);
            _writeBuffer.WriteInt16(field.ColumnAttributeNumber);
            _writeBuffer.WriteUInt32(field.TypeOID);
            _writeBuffer.WriteInt16(field.TypeSize);
            _writeBuffer.WriteInt32(field.TypeModifier);
            _writeBuffer.WriteInt16(field.DataFormat.ToFormatCode());
        }

        return this;
    }

    internal PgServerMock WriteParameterDescription(params FieldDescription[] fields)
    {
        CheckDisposed();

        _writeBuffer.WriteByte((byte)BackendMessageCode.ParameterDescription);
        _writeBuffer.WriteInt32(1 + 4 + 2 + fields.Length * 4);
        _writeBuffer.WriteUInt16((ushort)fields.Length);

        foreach (var field in fields)
            _writeBuffer.WriteUInt32(field.TypeOID);

        return this;
    }

    internal PgServerMock WriteNoData()
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.NoData);
        _writeBuffer.WriteInt32(4);
        return this;
    }

    internal PgServerMock WriteEmptyQueryResponse()
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.EmptyQueryResponse);
        _writeBuffer.WriteInt32(4);
        return this;
    }

    internal PgServerMock WriteDataRow(params byte[][] columnValues)
    {
        CheckDisposed();

        _writeBuffer.WriteByte((byte)BackendMessageCode.DataRow);
        _writeBuffer.WriteInt32(4 + 2 + columnValues.Sum(v => 4 + v.Length));
        _writeBuffer.WriteInt16((short)columnValues.Length);

        foreach (var field in columnValues)
        {
            _writeBuffer.WriteInt32(field.Length);
            _writeBuffer.WriteBytes(field);
        }

        return this;
    }

    /// <summary>
    /// Writes the bytes to the buffer and flushes <b>only</b> when the buffer is full
    /// </summary>
    internal async Task WriteDataRowWithFlush(params byte[][] columnValues)
    {
        CheckDisposed();

        _writeBuffer.WriteByte((byte)BackendMessageCode.DataRow);
        _writeBuffer.WriteInt32(4 + 2 + columnValues.Sum(v => 4 + v.Length));
        _writeBuffer.WriteInt16((short)columnValues.Length);

        foreach (var field in columnValues)
        {
            _writeBuffer.WriteInt32(field.Length);
            await _writeBuffer.WriteBytesRaw(field, true);
        }
    }

    internal PgServerMock WriteCommandComplete(string tag = "")
    {
        CheckDisposed();

        _writeBuffer.WriteByte((byte)BackendMessageCode.CommandComplete);
        _writeBuffer.WriteInt32(4 + Encoding.GetByteCount(tag) + 1);
        _writeBuffer.WriteNullTerminatedString(tag);
        return this;
    }

    internal PgServerMock WriteReadyForQuery(TransactionStatus transactionStatus = TransactionStatus.Idle)
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.ReadyForQuery);
        _writeBuffer.WriteInt32(4 + 1);
        _writeBuffer.WriteByte((byte)transactionStatus);
        return this;
    }

    internal PgServerMock WriteAuthenticateOk()
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.AuthenticationRequest);
        _writeBuffer.WriteInt32(4 + 4);
        _writeBuffer.WriteInt32(0);
        return this;
    }

    internal PgServerMock WriteParameterStatuses(Dictionary<string, string> parameters)
    {
        foreach (var kv in parameters)
            WriteParameterStatus(kv.Key, kv.Value);
        return this;
    }

    internal PgServerMock WriteParameterStatus(string name, string value)
    {
        CheckDisposed();

        _writeBuffer.WriteByte((byte)BackendMessageCode.ParameterStatus);
        _writeBuffer.WriteInt32(4 + Encoding.GetByteCount(name) + 1 + Encoding.GetByteCount(value) + 1);
        _writeBuffer.WriteNullTerminatedString(name);
        _writeBuffer.WriteNullTerminatedString(value);

        return this;
    }

    internal PgServerMock WriteBackendKeyData(int processId, int secret)
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.BackendKeyData);
        _writeBuffer.WriteInt32(4 + 4 + 4);
        _writeBuffer.WriteInt32(processId);
        _writeBuffer.WriteInt32(secret);
        return this;
    }

    internal PgServerMock WriteCancellationResponse()
        => WriteErrorResponse(PostgresErrorCodes.QueryCanceled, "Cancellation", "Query cancelled");

    internal PgServerMock WriteCopyInResponse(bool isBinary = false)
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.CopyInResponse);
        _writeBuffer.WriteInt32(5);
        _writeBuffer.WriteByte(isBinary ? (byte)1 : (byte)0);
        _writeBuffer.WriteInt16(1);
        _writeBuffer.WriteInt16(0);
        return this;
    }

    internal PgServerMock WriteErrorResponse(string code)
        => WriteErrorResponse(code, "ERROR", "MOCK ERROR MESSAGE");

    internal PgServerMock WriteErrorResponse(string code, string severity, string message)
    {
        CheckDisposed();
        _writeBuffer.WriteByte((byte)BackendMessageCode.ErrorResponse);
        _writeBuffer.WriteInt32(
            4 +
            1 + Encoding.GetByteCount(code) +
            1 + Encoding.GetByteCount(severity) +
            1 + Encoding.GetByteCount(message) +
            1);
        _writeBuffer.WriteByte((byte)ErrorOrNoticeMessage.ErrorFieldTypeCode.Code);
        _writeBuffer.WriteNullTerminatedString(code);
        _writeBuffer.WriteByte((byte)ErrorOrNoticeMessage.ErrorFieldTypeCode.Severity);
        _writeBuffer.WriteNullTerminatedString(severity);
        _writeBuffer.WriteByte((byte)ErrorOrNoticeMessage.ErrorFieldTypeCode.Message);
        _writeBuffer.WriteNullTerminatedString(message);
        _writeBuffer.WriteByte((byte)ErrorOrNoticeMessage.ErrorFieldTypeCode.Done);
        return this;
    }

    #endregion Low-level message writing

    void CheckDisposed()
    {
        if (_stream is null)
            throw new ObjectDisposedException(nameof(PgServerMock));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _readBuffer.Dispose();
        _writeBuffer.Dispose();
        _stream.Dispose();

        _disposed = true;
    }
}
