using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using HuaweiCloud.GaussDB.Properties;
using HuaweiCloud.GaussDBTypes;
using NUnit.Framework;
using static HuaweiCloud.GaussDB.Tests.TestUtil;

namespace HuaweiCloud.GaussDB.Tests.Types;

public class MultirangeTests : TestBase
{
    static readonly TestCaseData[] MultirangeTestCases =
    [
        // int4multirange
        new TestCaseData(
                new GaussDBRange<int>[]
                {
                    new(3, true, false, 7, false, false),
                    new(9, true, false, 0, false, true)
                },
                "{[3,7),[9,)}", "int4multirange", GaussDBDbType.IntegerMultirange, true, true, default(GaussDBRange<int>))
            .SetName("Int"),

        // int8multirange
        new TestCaseData(
                new GaussDBRange<long>[]
                {
                    new(3, true, false, 7, false, false),
                    new(9, true, false, 0, false, true)
                },
                "{[3,7),[9,)}", "int8multirange", GaussDBDbType.BigIntMultirange, true, true, default(GaussDBRange<long>))
            .SetName("Long"),

        // nummultirange
        // numeric is non-discrete so doesn't undergo normalization, use that to test bound scenarios which otherwise get normalized
        new TestCaseData(
                new GaussDBRange<decimal>[]
                {
                    new(3, true, false, 7, true, false),
                    new(9, false, false, 0, false, true)
                },
                "{[3,7],(9,)}", "nummultirange", GaussDBDbType.NumericMultirange, true, true, default(GaussDBRange<decimal>))
            .SetName("Decimal"),

        // daterange
        new TestCaseData(
                new GaussDBRange<DateOnly>[]
                {
                    new(new(2020, 1, 1), true, false, new(2020, 1, 5), false, false),
                    new(new(2020, 1, 10), true, false, default, false, true)
                },
                "{[2020-01-01,2020-01-05),[2020-01-10,)}", "datemultirange", GaussDBDbType.DateMultirange, true, false, default(GaussDBRange<DateOnly>))
            .SetName("DateTime DateMultirange"),

        // tsmultirange
        new TestCaseData(
                new GaussDBRange<DateTime>[]
                {
                    new(new(2020, 1, 1), true, false, new(2020, 1, 5), false, false),
                    new(new(2020, 1, 10), true, false, default, false, true)
                },
                """{["2020-01-01 00:00:00","2020-01-05 00:00:00"),["2020-01-10 00:00:00",)}""", "tsmultirange", GaussDBDbType.TimestampMultirange, true, true, default(GaussDBRange<DateTime>))
            .SetName("DateTime TimestampMultirange"),

        // tstzmultirange
        new TestCaseData(
                new GaussDBRange<DateTime>[]
                {
                    new(new(2020, 1, 1, 0, 0, 0, kind: DateTimeKind.Utc), true, false, new(2020, 1, 5, 0, 0, 0, kind: DateTimeKind.Utc), false, false),
                    new(new(2020, 1, 10, 0, 0, 0, kind: DateTimeKind.Utc), true, false, default, false, true)
                },
                """{["2020-01-01 01:00:00+01","2020-01-05 01:00:00+01"),["2020-01-10 01:00:00+01",)}""", "tstzmultirange", GaussDBDbType.TimestampTzMultirange, true, true, default(GaussDBRange<DateTime>))
            .SetName("DateTime TimestampTzMultirange"),

        new TestCaseData(
                new GaussDBRange<DateOnly>[]
                {
                    new(new(2020, 1, 1), true, false, new(2020, 1, 5), false, false),
                    new(new(2020, 1, 10), true, false, default, false, true)
                },
                "{[2020-01-01,2020-01-05),[2020-01-10,)}", "datemultirange", GaussDBDbType.DateMultirange, false, false, default(GaussDBRange<DateOnly>))
            .SetName("DateOnly")
    ];

    [Test, TestCaseSource(nameof(MultirangeTestCases))]
    public Task Multirange_as_array<T, TRange>(
        T multirangeAsArray, string sqlLiteral, string pgTypeName, GaussDBDbType? gaussdbDbType, bool isDefaultForReading, bool isDefaultForWriting, TRange _)
        => AssertType(multirangeAsArray, sqlLiteral, pgTypeName, gaussdbDbType, isDefaultForReading: isDefaultForReading,
            isDefaultForWriting: isDefaultForWriting);

    [Test, TestCaseSource(nameof(MultirangeTestCases))]
    public Task Multirange_as_list<T, TRange>(
        T multirangeAsArray, string sqlLiteral, string pgTypeName, GaussDBDbType? gaussdbDbType, bool isDefaultForReading, bool isDefaultForWriting, TRange _)
        where T : IList<TRange>
        => AssertType(
            new List<TRange>(multirangeAsArray),
            sqlLiteral, pgTypeName, gaussdbDbType, isDefaultForReading: false, isDefaultForWriting: isDefaultForWriting);

    [Test]
    [NonParallelizable]
    public async Task Unmapped_multirange_with_mapped_subtype()
    {
        await using var dataSource = CreateDataSource(b => b.EnableUnmappedTypes().ConnectionStringBuilder.MaxPoolSize = 1);
        await using var conn = await dataSource.OpenConnectionAsync();

        var typeName = await GetTempTypeName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE TYPE {typeName} AS RANGE(subtype=text)");
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        conn.ReloadTypes();
        Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));

        var value = new[] {new GaussDBRange<char[]>(
            new string('a', conn.Settings.WriteBufferSize + 10).ToCharArray(),
            new string('z', conn.Settings.WriteBufferSize + 10).ToCharArray()
        )};

        await using var cmd = new GaussDBCommand("SELECT @p", conn);
        cmd.Parameters.Add(new GaussDBParameter { DataTypeName = typeName + "_multirange", ParameterName = "p", Value = value });
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        await reader.ReadAsync();

        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(GaussDBRange<string>[])));
        var result = reader.GetFieldValue<GaussDBRange<char[]>[]>(0);
        Assert.That(result, Is.EqualTo(value).Using<GaussDBRange<char[]>[]>((actual, expected) =>
            actual[0].LowerBound!.SequenceEqual(expected[0].LowerBound!) && actual[0].UpperBound!.SequenceEqual(expected[0].UpperBound!)));
    }

    [Test]
    public async Task Unmapped_multirange_supported_only_with_EnableUnmappedTypes()
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        var rangeType = await GetTempTypeName(connection);
        var multirangeTypeName = rangeType + "_multirange";
        await connection.ExecuteNonQueryAsync($"CREATE TYPE {rangeType} AS RANGE(subtype=text)");
        await Task.Yield(); // TODO: fix multiplexing deadlock bug
        await connection.ReloadTypesAsync();

        var errorMessage = string.Format(
            GaussDBStrings.UnmappedRangesNotEnabled,
            nameof(GaussDBSlimDataSourceBuilder.EnableUnmappedTypes),
            nameof(GaussDBDataSourceBuilder));

        var exception = await AssertTypeUnsupportedWrite(
            new GaussDBRange<string>[]
            {
                new("bar", "foo"),
                new("moo", "zoo"),
            },
            multirangeTypeName);
        Assert.IsInstanceOf<NotSupportedException>(exception.InnerException);
        Assert.That(exception.InnerException!.Message, Is.EqualTo(errorMessage));

        exception = await AssertTypeUnsupportedRead("""{["bar","foo"],["moo","zoo"]}""",
            multirangeTypeName);
        Assert.IsInstanceOf<NotSupportedException>(exception.InnerException);
        Assert.That(exception.InnerException!.Message, Is.EqualTo(errorMessage));

        exception = await AssertTypeUnsupportedRead<GaussDBRange<string>>(
            """{["bar","foo"],["moo","zoo"]}""",
            multirangeTypeName);
        Assert.IsInstanceOf<NotSupportedException>(exception.InnerException);
        Assert.That(exception.InnerException!.Message, Is.EqualTo(errorMessage));
    }

    protected override GaussDBDataSource DataSource { get; }

    public MultirangeTests() => DataSource = CreateDataSource(builder =>
        {
            builder.ConnectionStringBuilder.Timezone = "Europe/Berlin";
        });

    [OneTimeSetUp]
    public async Task Setup()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");
    }

    [OneTimeTearDown]
    public void TearDown() => DataSource.Dispose();
}
