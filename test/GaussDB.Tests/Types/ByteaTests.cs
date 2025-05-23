using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using HuaweiCloud.GaussDBTypes;
using NUnit.Framework;

namespace HuaweiCloud.GaussDB.Tests.Types;

/// <summary>
/// Tests on the PostgreSQL bytea type
/// </summary>
/// <summary>
/// https://www.postgresql.org/docs/current/static/datatype-binary.html
/// </summary>
public class ByteaTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Test]
    [TestCase(new byte[] { 1, 2, 3, 4, 5 }, "\\x0102030405", TestName = "Bytea")]
    [TestCase(new byte[] { }, "\\x", TestName = "Bytea_empty")]
    public async Task Bytea(byte[] byteArray, string sqlLiteral)
    {
        if (byteArray.Length != 0)
        {
            await AssertType(byteArray, sqlLiteral, "bytea", GaussDBDbType.Bytea, DbType.Binary);
        }
    }

    [Test]
    public async Task Bytea_long()
    {
        await using var conn = await OpenConnectionAsync();
        var array = new byte[conn.Settings.WriteBufferSize + 100];
        var sqlLiteral = "\\x" + new string('1', (conn.Settings.WriteBufferSize + 100) * 2);
        for (var i = 0; i < array.Length; i++)
            array[i] = 17;

        await Bytea(array, sqlLiteral);
    }

    [Test]
    public Task AsMemory()
        => AssertType(
            new Memory<byte>([1, 2, 3]), "\\x010203", "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false,
            comparer: (left, right) => left.Span.SequenceEqual(right.Span));

    [Test]
    public Task AsReadOnlyMemory()
        => AssertType(
            new ReadOnlyMemory<byte>([1, 2, 3]), "\\x010203", "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false,
            comparer: (left, right) => left.Span.SequenceEqual(right.Span));

    [Test]
    public Task AsArraySegment()
        => AssertType(
            new ArraySegment<byte>([1, 2, 3]), "\\x010203", "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false);

    [Test]
    public Task Write_as_MemoryStream()
        => AssertTypeWrite(
            () => new MemoryStream([1, 2, 3]), "\\x010203", "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false);

    [Test]
    public Task Write_as_MemoryStream_truncated()
    {
        var msFactory = () =>
        {
            var ms = new MemoryStream([1, 2, 3, 4]);
            ms.ReadByte();
            return ms;
        };

        return AssertTypeWrite(
            msFactory, "\\x020304", "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false);
    }

    [Test]
    public Task Write_as_MemoryStream_exposableArray()
    {
        var msFactory = () =>
        {
            var ms = new MemoryStream(20);
            ms.WriteByte(1);
            ms.WriteByte(2);
            ms.WriteByte(3);
            ms.WriteByte(4);
            ms.Position = 1;
            return ms;
        };

        return AssertTypeWrite(
            msFactory, "\\x020304", "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false);
    }

    [Test]
    public async Task Write_as_MemoryStream_long()
    {
        var rnd = new Random(1);
        var bytes = new byte[8192 * 4];
        rnd.NextBytes(bytes);
        var expectedSql = "\\x" + ToHex(bytes);

        await AssertTypeWrite(
            () => new MemoryStream(bytes), expectedSql, "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false);
    }

    [Test]
    public async Task Write_as_FileStream()
    {
        var filePath = Path.GetTempFileName();
        var fsList = new List<FileStream>();
        try
        {
            await File.WriteAllBytesAsync(filePath, [1, 2, 3]);

            await AssertTypeWrite(
                () => FileStreamFactory(filePath, fsList), "\\x010203", "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false);
        }
        finally
        {
            foreach (var fs in fsList)
                await fs.DisposeAsync();

            try
            {
                File.Delete(filePath);
            }
            catch {}
        }

        FileStream FileStreamFactory(string filePath, List<FileStream> fsList)
        {
            var fs = File.OpenRead(filePath);
            fsList.Add(fs);
            return fs;
        }
    }

    [Test]
    public async Task Write_as_FileStream_long()
    {
        var filePath = Path.GetTempFileName();
        var fsList = new List<FileStream>();
        var rnd = new Random(1);
        try
        {
            var bytes = new byte[8192 * 4];
            rnd.NextBytes(bytes);
            await File.WriteAllBytesAsync(filePath, bytes);
            var expectedSql = "\\x" + ToHex(bytes);

            await AssertTypeWrite(
                () => FileStreamFactory(filePath, fsList), expectedSql, "bytea", GaussDBDbType.Bytea, DbType.Binary, isDefault: false);
        }
        finally
        {
            foreach (var fs in fsList)
                await fs.DisposeAsync();

            try
            {
                File.Delete(filePath);
            }
            catch {}
        }

        FileStream FileStreamFactory(string filePath, List<FileStream> fsList)
        {
            var fs = File.OpenRead(filePath);
            fsList.Add(fs);
            return fs;
        }
    }

    static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var c = new char[bytes.Length * 2];

        byte b;

        for (int bx = 0, cx = 0; bx < bytes.Length; ++bx, ++cx)
        {
            b = (byte)(bytes[bx] >> 4);
            c[cx] = (char)(b > 9 ? b - 10 + 'a' : b + '0');

            b = (byte)(bytes[bx] & 0x0F);
            c[++cx] = (char)(b > 9 ? b - 10 + 'a' : b + '0');
        }

        return new string(c);
    }

    [Test, Description("Tests that bytea array values are truncated when the GaussDBParameter's Size is set")]
    public async Task Truncate_array()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new GaussDBCommand("SELECT @p", conn);
        byte[] data = [1, 2, 3, 4, 5, 6];
        var p = new GaussDBParameter("p", data) { Size = 4 };
        cmd.Parameters.Add(p);
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(p.Value, Is.EqualTo(new byte[] { 1, 2, 3, 4 }), "Truncated parameter value should be persisted on the parameter per DbParameter.Size docs");

        // GaussDBParameter.Size needs to persist when value is changed
        byte[] data2 = [11, 12, 13, 14, 15, 16];
        p.Value = data2;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(new byte[] { 11, 12, 13, 14 }));

        // GaussDBParameter.Size larger than the value size should mean the value size, as well as 0 and -1
        p.Value = data2;
        p.Size = data2.Length + 10;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));
        p.Size = 0;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));
        p.Size = -1;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));

        Assert.That(() => p.Size = -2, Throws.Exception.TypeOf<ArgumentException>());
    }

    [Test, Description("Tests that bytea stream values are truncated when the GaussDBParameter's Size is set")]
    public async Task Truncate_stream()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new GaussDBCommand("SELECT @p", conn);
        byte[] data = [1, 2, 3, 4, 5, 6];
        var p = new GaussDBParameter("p", new MemoryStream(data)) { Size = 4 };
        cmd.Parameters.Add(p);
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

        // GaussDBParameter.Size needs to persist when value is changed
        byte[] data2 = [11, 12, 13, 14, 15, 16];
        p.Value = new MemoryStream(data2);
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(new byte[] { 11, 12, 13, 14 }));

        // Handle with offset
        var data2ms = new MemoryStream(data2);
        data2ms.ReadByte();
        p.Value = data2ms;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(new byte[] { 12, 13, 14, 15 }));

        p.Size = 0;
        p.Value = new MemoryStream(data2);
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));
        p.Size = -1;
        p.Value = new MemoryStream(data2);
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));

        Assert.That(() => p.Size = -2, Throws.Exception.TypeOf<ArgumentException>());

        p.Value = new MemoryStream(data2);
        p.Size = data2.Length + 10;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data2));
    }

    [Test]
    public async Task Write_as_NonSeekable_stream()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new GaussDBCommand("SELECT @p", conn);
        byte[] data = [1, 2, 3, 4, 5, 6];
        var p = new GaussDBParameter("p", new NonSeekableStream(data)) { Size = 4 };
        cmd.Parameters.Add(p);
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

        var streamWithOffset = new NonSeekableStream(data);
        streamWithOffset.ReadByte();
        p.Value = streamWithOffset;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(new byte[] { 2, 3, 4, 5 }));

        p.Value = new NonSeekableStream(data);
        p.Size = 0;
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(data));
        Assert.That(conn.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public async Task Array_of_bytea()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new GaussDBCommand("SELECT :p1", conn);
        var bytes = new byte[] { 1, 2, 3, 4, 5, 34, 39, 48, 49, 50, 51, 52, 92, 127, 128, 255, 254, 253, 252, 251 };
        var inVal = new[] { bytes, bytes };
        cmd.Parameters.AddWithValue("p1", GaussDBDbType.Bytea | GaussDBDbType.Array, inVal);
        var retVal = (byte[][]?)await cmd.ExecuteScalarAsync();
        Assert.AreEqual(inVal.Length, retVal!.Length);
        Assert.AreEqual(inVal[0], retVal[0]);
        Assert.AreEqual(inVal[1], retVal[1]);
    }

    sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
