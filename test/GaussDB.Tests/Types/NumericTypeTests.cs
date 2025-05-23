using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using HuaweiCloud.GaussDBTypes;
using NUnit.Framework;
using static HuaweiCloud.GaussDB.Tests.TestUtil;

namespace HuaweiCloud.GaussDB.Tests.Types;

/// <summary>
/// Tests on PostgreSQL numeric types
/// </summary>
/// <summary>
/// https://www.postgresql.org/docs/current/static/datatype-numeric.html
/// </summary>
public class NumericTypeTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Test]
    public async Task Int16()
    {
        await AssertType((short)8, "8", "smallint", GaussDBDbType.Smallint, DbType.Int16);
        // Clr byte/sbyte maps to 'int2' as there is no byte type in PostgreSQL, byte[] maps to bytea however.
        await AssertType((byte)8, "8", "smallint", GaussDBDbType.Smallint, DbType.Int16, isDefaultForReading: false, skipArrayCheck: true);
        await AssertType((sbyte)8, "8", "smallint", GaussDBDbType.Smallint, DbType.Int16, isDefaultForReading: false);

        await AssertType(8,       "8", "smallint", GaussDBDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8L,      "8", "smallint", GaussDBDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8F,      "8", "smallint", GaussDBDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8D,      "8", "smallint", GaussDBDbType.Smallint, DbType.Int16, isDefault: false);
        await AssertType(8M,      "8", "smallint", GaussDBDbType.Smallint, DbType.Int16, isDefault: false);
    }

    [Test]
    public async Task Int32()
    {
        await AssertType(8, "8", "integer", GaussDBDbType.Integer, DbType.Int32);

        await AssertType((short)8, "8", "integer", GaussDBDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8L,       "8", "integer", GaussDBDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType((byte)8,  "8", "integer", GaussDBDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8F,       "8", "integer", GaussDBDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8D,       "8", "integer", GaussDBDbType.Integer, DbType.Int32, isDefault: false);
        await AssertType(8M,       "8", "integer", GaussDBDbType.Integer, DbType.Int32, isDefault: false);
    }

    //todo: 08P01:Insufficient data left in message
    /*[Test, Description("Tests some types which are aliased to UInt32")]
    //[TestCase("oid", GaussDBDbType.Oid, TestName="OID")]
    [TestCase("xid", GaussDBDbType.Xid, TestName="XID")]
    [TestCase("cid", GaussDBDbType.Cid, TestName="CID")]
    public Task UInt32(string pgTypeName, GaussDBDbType gaussdbDbType)
        => AssertType(8u, "8", pgTypeName, gaussdbDbType, isDefaultForWriting: false);*/

    [Test]
    [TestCase("xid8", GaussDBDbType.Xid8, TestName="XID8")]
    public async Task UInt64(string pgTypeName, GaussDBDbType gaussdbDbType)
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "13.0", "The xid8 type was introduced in PostgreSQL 13");

        await AssertType(8ul, "8", pgTypeName, gaussdbDbType, isDefaultForWriting: false);
    }

    [Test]
    public async Task Int64()
    {
        await AssertType(8L, "8", "bigint", GaussDBDbType.Bigint, DbType.Int64);

        await AssertType((short)8, "8", "bigint", GaussDBDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8,        "8", "bigint", GaussDBDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType((byte)8,  "8", "bigint", GaussDBDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8F,       "8", "bigint", GaussDBDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8D,       "8", "bigint", GaussDBDbType.Bigint, DbType.Int64, isDefault: false);
        await AssertType(8M,       "8", "bigint", GaussDBDbType.Bigint, DbType.Int64, isDefault: false);
    }

    [Test]
    [TestCase(4.123456789012345, "4.123456789012345", TestName = "Double")]
    [TestCase(double.NaN, "NaN", TestName = "Double_NaN")]
    [TestCase(double.PositiveInfinity, "Infinity", TestName = "Double_PositiveInfinity")]
    [TestCase(double.NegativeInfinity, "-Infinity", TestName = "Double_NegativeInfinity")]
    public async Task Double(double value, string sqlLiteral)
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "12.0");

        await AssertType(value, sqlLiteral, "double precision", GaussDBDbType.Double, DbType.Double);
    }

    [Test]
    [TestCase(0.123456F, "0.123456", TestName = "Float")]
    [TestCase(float.NaN, "NaN", TestName = "Float_NaN")]
    [TestCase(float.PositiveInfinity, "Infinity", TestName = "Float_PositiveInfinity")]
    [TestCase(float.NegativeInfinity, "-Infinity", TestName = "Float_NegativeInfinity")]
    public Task Float(float value, string sqlLiteral)
        => AssertType(value, sqlLiteral, "real", GaussDBDbType.Real, DbType.Single);

    [Test]
    [TestCase(short.MaxValue + 1, "smallint")]
    [TestCase(int.MaxValue + 1L, "integer")]
    [TestCase(long.MaxValue + 1D, "bigint")]
    public Task Write_overflow<T>(T value, string pgTypeName)
        => AssertTypeUnsupportedWrite<T, OverflowException>(value, pgTypeName);

    [Test]
    [TestCase((short)0, short.MaxValue + 1D, "int")]
    [TestCase(0, int.MaxValue + 1D, "bigint")]
    [TestCase(0L, long.MaxValue + 1D, "decimal")]
    public Task Read_overflow<T>(T _, double value, string pgTypeName)
        => AssertTypeUnsupportedRead<T, OverflowException>(value.ToString(CultureInfo.InvariantCulture), pgTypeName);
}
