using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using HuaweiCloud.GaussDBTypes;
using NUnit.Framework;
using static HuaweiCloud.GaussDB.Tests.TestUtil;

namespace HuaweiCloud.GaussDB.Tests.Types;

// Since this test suite manipulates TimeZone, it is incompatible with multiplexing
public class DateTimeTests : TestBase
{
    #region Date

    /*[Test]
    public Task Date_as_DateOnly()
        => AssertType(new DateOnly(2020, 10, 1), "2020-10-01", "date", GaussDBDbType.Date, DbType.Date);*/

    [Test]
    public Task Date_as_DateTime()
        => AssertType(new DateTime(2020, 10, 1), "2020-10-01", "date", GaussDBDbType.Date, DbType.Date, isDefault: false);

    [Test]
    public Task Date_as_DateTime_with_date_and_time_before_2000()
        => AssertTypeWrite(new DateTime(1980, 10, 1, 11, 0, 0), "1980-10-01", "date", GaussDBDbType.Date, DbType.Date, isDefault: false);

    //todo: 不支持不包含时区的时间戳
    // Internal PostgreSQL representation (days since 2020-01-01), for out-of-range values.
    /*[Test]
    public Task Date_as_int()
        => AssertType(7579, "2020-10-01", "date", GaussDBDbType.Date, DbType.Date, isDefault: false);*/

    [Test]
    public Task Daterange_as_GaussDBRange_of_DateOnly()
        => AssertType(
            new GaussDBRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false),
            "[2002-03-04,2002-03-06)",
            "daterange",
            GaussDBDbType.DateRange,
            skipArrayCheck: true); // GaussDBRange<T>[] is mapped to multirange by default, not array; test separately

    [Test]
    public Task Daterange_array_as_GaussDBRange_of_DateOnly_array()
        => AssertType(
            new[]
            {
                new GaussDBRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new GaussDBRange<DateOnly>(new(2002, 3, 8), true, new(2002, 3, 9), false)
            },
            """{"[2002-03-04,2002-03-06)","[2002-03-08,2002-03-09)"}""",
            "daterange[]",
            GaussDBDbType.DateRange | GaussDBDbType.Array,
            isDefaultForWriting: false);

    [Test]
    public Task Daterange_as_GaussDBRange_of_DateTime()
        => AssertType(
            new GaussDBRange<DateTime>(new(2002, 3, 4), true, new(2002, 3, 6), false),
            "[2002-03-04,2002-03-06)",
            "daterange",
            GaussDBDbType.DateRange,
            isDefault: false);

    [Test]
    public async Task Datemultirange_as_array_of_GaussDBRange_of_DateOnly()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new GaussDBRange<DateOnly>(new(2002, 3, 8), true, new(2002, 3, 11), false)
            },
            "{[2002-03-04,2002-03-06),[2002-03-08,2002-03-11)}",
            "datemultirange",
            GaussDBDbType.DateMultirange);
    }

    [Test]
    public async Task Datemultirange_as_array_of_GaussDBRange_of_DateTime()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<DateTime>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new GaussDBRange<DateTime>(new(2002, 3, 8), true, new(2002, 3, 11), false)
            },
            "{[2002-03-04,2002-03-06),[2002-03-08,2002-03-11)}",
            "datemultirange",
            GaussDBDbType.DateMultirange,
            isDefault: false);
    }

    #endregion

    #region Time

    [Test]
    public Task Time_as_TimeOnly()
        => AssertType(
            new TimeOnly(10, 45, 34, 500),
            "10:45:34.5",
            "time without time zone",
            GaussDBDbType.Time,
            DbType.Time);

    [Test]
    public Task Time_as_TimeSpan()
        => AssertType(
            new TimeSpan(0, 10, 45, 34, 500),
            "10:45:34.5",
            "time without time zone",
            GaussDBDbType.Time,
            DbType.Time,
            isDefault: false);

    #endregion

    #region Time with timezone

    static readonly TestCaseData[] TimeTzValues =
    [
        new TestCaseData(new DateTimeOffset(1, 1, 2, 13, 3, 45, 510, TimeSpan.FromHours(2)), "13:03:45.51+02")
            .SetName("Timezone"),
        new TestCaseData(new DateTimeOffset(1, 1, 2, 1, 0, 45, 510, TimeSpan.FromHours(-3)), "01:00:45.51-03")
            .SetName("Negative_timezone"),
        new TestCaseData(new DateTimeOffset(1212720130000, TimeSpan.Zero), "09:41:12.013+00")
            .SetName("Utc"),
        new TestCaseData(new DateTimeOffset(1, 1, 2, 1, 0, 0, new TimeSpan(0, 2, 0, 0)), "01:00:00+02")
            .SetName("Before_utc_zero")
    ];

    [Test, TestCaseSource(nameof(TimeTzValues))]
    public Task TimeTz_as_DateTimeOffset(DateTimeOffset time, string sqlLiteral)
        => AssertType(time, sqlLiteral, "time with time zone", GaussDBDbType.TimeTz, isDefault: false);

    #endregion

    #region Timestamp

    static readonly TestCaseData[] TimestampValues =
    [
        new TestCaseData(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Unspecified), "1998-04-12 13:26:38")
            .SetName("Timestamp_pre2000"),
        new TestCaseData(new DateTime(2015, 1, 27, 8, 45, 12, 345, DateTimeKind.Unspecified), "2015-01-27 08:45:12.345")
            .SetName("Timestamp_post2000"),
        new TestCaseData(new DateTime(2013, 7, 25, 0, 0, 0, DateTimeKind.Unspecified), "2013-07-25 00:00:00")
            .SetName("Timestamp_date_only")
    ];

    [Test, TestCaseSource(nameof(TimestampValues))]
    public async Task Timestamp_as_DateTime(DateTime dateTime, string sqlLiteral)
    {
        await AssertType(dateTime, sqlLiteral, "timestamp without time zone", GaussDBDbType.Timestamp, DbType.DateTime2,
            // Explicitly check kind as well.
            comparer: (actual, expected) => actual.Kind == expected.Kind && actual.Equals(expected));

        await AssertType(
            new List<DateTime> { dateTime, dateTime }, $$"""{"{{sqlLiteral}}","{{sqlLiteral}}"}""", "timestamp without time zone[]", GaussDBDbType.Timestamp | GaussDBDbType.Array,
            isDefaultForReading: false);
    }

    [Test]
    public Task Timestamp_cannot_write_utc_DateTime()
        => AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc), "timestamp without time zone");

    [Test]
    public Task Timestamp_as_long()
        => AssertType(
            -54297202000000,
            "1998-04-12 13:26:38",
            "timestamp without time zone",
            GaussDBDbType.Timestamp,
            DbType.DateTime2,
            isDefault: false);

    [Test]
    public Task Timestamp_cannot_use_as_DateTimeOffset()
        => AssertTypeUnsupported(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 13:26:38",
            "timestamp without time zone");

    [Test]
    public Task Tsrange_as_GaussDBRange_of_DateTime()
        => AssertType(
            new GaussDBRange<DateTime>(
                new(1998, 4, 12, 13, 26, 38, DateTimeKind.Local),
                new(1998, 4, 12, 15, 26, 38, DateTimeKind.Local)),
            @"[""1998-04-12 13:26:38"",""1998-04-12 15:26:38""]",
            "tsrange",
            GaussDBDbType.TimestampRange,
            skipArrayCheck: true); // GaussDBRange<T>[] is mapped to multirange by default, not array; test separately

    [Test]
    public Task Tsrange_array_as_GaussDBRange_of_DateTime_array()
        => AssertType(
            new[]
            {
                new GaussDBRange<DateTime>(
                    new(1998, 4, 12, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 12, 15, 26, 38, DateTimeKind.Local)),
                new GaussDBRange<DateTime>(
                    new(1998, 4, 13, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 13, 15, 26, 38, DateTimeKind.Local)),
            },
            """{"[\"1998-04-12 13:26:38\",\"1998-04-12 15:26:38\"]","[\"1998-04-13 13:26:38\",\"1998-04-13 15:26:38\"]"}""",
            "tsrange[]",
            GaussDBDbType.TimestampRange | GaussDBDbType.Array,
            isDefault: false);

    [Test]
    public async Task Tsmultirange_as_array_of_GaussDBRange_of_DateTime()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<DateTime>(
                    new(1998, 4, 12, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 12, 15, 26, 38, DateTimeKind.Local)),
                new GaussDBRange<DateTime>(
                    new(1998, 4, 13, 13, 26, 38, DateTimeKind.Local),
                    new(1998, 4, 13, 15, 26, 38, DateTimeKind.Local)),
            },
            @"{[""1998-04-12 13:26:38"",""1998-04-12 15:26:38""],[""1998-04-13 13:26:38"",""1998-04-13 15:26:38""]}",
            "tsmultirange",
            GaussDBDbType.TimestampMultirange);
    }

    #endregion

    #region Timestamp with timezone

    // Note that the below text representations are local (according to TimeZone, which is set to Europe/Berlin in this test class),
    // because that's how PG does timestamptz *text* representation.
    static readonly TestCaseData[] TimestampTzWriteValues =
    [
        new TestCaseData(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc), "1998-04-12 15:26:38+02")
            .SetName("Timestamptz_write_pre2000"),
        new TestCaseData(new DateTime(2015, 1, 27, 8, 45, 12, 345, DateTimeKind.Utc), "2015-01-27 09:45:12.345+01")
            .SetName("Timestamptz_write_post2000"),
        new TestCaseData(new DateTime(2013, 7, 25, 0, 0, 0, DateTimeKind.Utc), "2013-07-25 02:00:00+02")
            .SetName("Timestamptz_write_date_only")
    ];

    [Test, TestCaseSource(nameof(TimestampTzWriteValues))]
    public async Task Timestamptz_as_DateTime(DateTime dateTime, string sqlLiteral)
    {
        await AssertType(dateTime, sqlLiteral, "timestamp with time zone", GaussDBDbType.TimestampTz, DbType.DateTime,
            // Explicitly check kind as well.
            comparer: (actual, expected) => actual.Kind == expected.Kind && actual.Equals(expected));

        await AssertType(
            new List<DateTime> { dateTime, dateTime }, $$"""{"{{sqlLiteral}}","{{sqlLiteral}}"}""", "timestamp with time zone[]", GaussDBDbType.TimestampTz | GaussDBDbType.Array,
            isDefaultForReading: false);

    }

    [Test]
    public async Task Timestamptz_infinity_as_DateTime()
    {
        await AssertType(DateTime.MinValue, "-infinity", "timestamp with time zone", GaussDBDbType.TimestampTz, DbType.DateTime,
            isDefault: false);
        await AssertType(DateTime.MaxValue, "infinity", "timestamp with time zone", GaussDBDbType.TimestampTz, DbType.DateTime,
            isDefault: false);
    }

    [Test]
    public async Task Timestamptz_cannot_write_non_utc_DateTime()
    {
        await AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Unspecified), "timestamp with time zone");
        await AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Local), "timestamp with time zone");
    }

    [Test]
    public async Task Timestamptz_as_DateTimeOffset_utc()
    {
        var dateTimeOffset = await AssertType(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            GaussDBDbType.TimestampTz,
            DbType.DateTime,
            isDefaultForReading: false);

        Assert.That(dateTimeOffset.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public Task Timestamptz_as_DateTimeOffset_utc_with_DbType_DateTimeOffset()
        => AssertTypeWrite(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            GaussDBDbType.TimestampTz,
            DbType.DateTimeOffset,
            inferredDbType: DbType.DateTime,
            isDefault: false);

    [Test]
    public Task Timestamptz_cannot_write_non_utc_DateTimeOffset()
        => AssertTypeUnsupportedWrite<DateTimeOffset, ArgumentException>(new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.FromHours(2)));

    [Test]
    public Task Timestamptz_as_long()
        => AssertType(
            -54297202000000,
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            GaussDBDbType.TimestampTz,
            DbType.DateTime,
            isDefault: false);

    [Test]
    public async Task Timestamptz_array_as_DateTimeOffset_array()
    {
        var dateTimeOffsets = await AssertType(
            new[]
            {
                new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
                new DateTimeOffset(1999, 4, 12, 13, 26, 38, TimeSpan.Zero)
            },
            """{"1998-04-12 15:26:38+02","1999-04-12 15:26:38+02"}""",
            "timestamp with time zone[]",
            GaussDBDbType.TimestampTz | GaussDBDbType.Array,
            isDefaultForReading: false);

        Assert.That(dateTimeOffsets[0].Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(dateTimeOffsets[1].Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public Task Tstzrange_as_GaussDBRange_of_DateTime()
        => AssertType(
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            @"[""1998-04-12 15:26:38+02"",""1998-04-12 17:26:38+02""]",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            skipArrayCheck: true); // GaussDBRange<T>[] is mapped to multirange by default, not array; test separately

    [Test]
    public Task Tstzrange_array_as_GaussDBRange_of_DateTime_array()
        => AssertType(
            new[]
            {
                new GaussDBRange<DateTime>(
                    new(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                    new(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
                new GaussDBRange<DateTime>(
                    new(1998, 4, 13, 13, 26, 38, DateTimeKind.Utc),
                    new(1998, 4, 13, 15, 26, 38, DateTimeKind.Utc)),
            },
            """{"[\"1998-04-12 15:26:38+02\",\"1998-04-12 17:26:38+02\"]","[\"1998-04-13 15:26:38+02\",\"1998-04-13 17:26:38+02\"]"}""",
            "tstzrange[]",
            GaussDBDbType.TimestampTzRange | GaussDBDbType.Array,
            isDefault: false);

    [Test]
    public async Task Tstzmultirange_as_array_of_GaussDBRange_of_DateTime()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<DateTime>(
                    new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                    new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
                new GaussDBRange<DateTime>(
                    new DateTime(1998, 4, 13, 13, 26, 38, DateTimeKind.Utc),
                    new DateTime(1998, 4, 13, 15, 26, 38, DateTimeKind.Utc)),
            },
            @"{[""1998-04-12 15:26:38+02"",""1998-04-12 17:26:38+02""],[""1998-04-13 15:26:38+02"",""1998-04-13 17:26:38+02""]}",
            "tstzmultirange",
            GaussDBDbType.TimestampTzMultirange);
    }

    [Test]
    public Task Cannot_mix_DateTime_Kinds_in_array()
        => AssertTypeUnsupportedWrite<DateTime[], ArgumentException>([
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Local)
        ]);


    [Test]
    public Task Cannot_mix_DateTime_Kinds_in_range()
        => AssertTypeUnsupportedWrite<GaussDBRange<DateTime>, ArgumentException>(new GaussDBRange<DateTime>(
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Local)));

    [Test]
    public async Task Cannot_mix_DateTime_Kinds_in_multirange()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertTypeUnsupportedWrite<GaussDBRange<DateTime>[], ArgumentException>([
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                new DateTime(1998, 4, 12, 15, 26, 38, DateTimeKind.Utc)),
            new GaussDBRange<DateTime>(
                new DateTime(1998, 4, 13, 13, 26, 38, DateTimeKind.Local),
                new DateTime(1998, 4, 13, 15, 26, 38, DateTimeKind.Local))
        ]);
    }

    [Test]
    public void GaussDBParameterDbType_is_value_dependent_datetime_or_datetime2()
    {
        var localtimestamp = new GaussDBParameter { Value = DateTime.Now };
        var unspecifiedtimestamp = new GaussDBParameter { Value = new DateTime() };
        Assert.AreEqual(DbType.DateTime2, localtimestamp.DbType);
        Assert.AreEqual(DbType.DateTime2, unspecifiedtimestamp.DbType);

        // We don't support any DateTimeOffset other than offset 0 which maps to timestamptz,
        // we might add an exception for offset == DateTimeOffset.Now.Offset (local offset) mapping to timestamp at some point.
        // var dtotimestamp = new GaussDBParameter { Value = DateTimeOffset.Now };
        // Assert.AreEqual(DbType.DateTime2, dtotimestamp.DbType);

        var timestamptz = new GaussDBParameter { Value = DateTime.UtcNow };
        var dtotimestamptz = new GaussDBParameter { Value = DateTimeOffset.UtcNow };
        Assert.AreEqual(DbType.DateTime, timestamptz.DbType);
        Assert.AreEqual(DbType.DateTime, dtotimestamptz.DbType);
    }

    [Test]
    public void GaussDBParameterGaussDBDbType_is_value_dependent_timestamp_or_timestamptz()
    {
        var localtimestamp = new GaussDBParameter { Value = DateTime.Now };
        var unspecifiedtimestamp = new GaussDBParameter { Value = new DateTime() };
        Assert.AreEqual(GaussDBDbType.Timestamp, localtimestamp.GaussDBDbType);
        Assert.AreEqual(GaussDBDbType.Timestamp, unspecifiedtimestamp.GaussDBDbType);

        var timestamptz = new GaussDBParameter { Value = DateTime.UtcNow };
        var dtotimestamptz = new GaussDBParameter { Value = DateTimeOffset.UtcNow };
        Assert.AreEqual(GaussDBDbType.TimestampTz, timestamptz.GaussDBDbType);
        Assert.AreEqual(GaussDBDbType.TimestampTz, dtotimestamptz.GaussDBDbType);
    }

    [Test]
    public async Task Array_of_nullable_timestamptz()
        => await AssertType(
            new DateTime?[]
            {
                new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
                null
            },
            @"{""1998-04-12 15:26:38+02"",NULL}",
            "timestamp with time zone[]",
            GaussDBDbType.TimestampTz | GaussDBDbType.Array,
            isDefault: false);

    #endregion

    #region Interval

    static readonly TestCaseData[] IntervalValues =
    [
        new TestCaseData(new TimeSpan(0, 2, 3, 4, 5), "02:03:04.005")
            .SetName("Interval_time_only"),
        new TestCaseData(new TimeSpan(1, 2, 3, 4, 5), "1 day 02:03:04.005")
            .SetName("Interval_with_day"),
        new TestCaseData(new TimeSpan(61, 2, 3, 4, 5), "61 days 02:03:04.005")
            .SetName("Interval_with_many_days"),
        new TestCaseData(new TimeSpan(new TimeSpan(2, 3, 4).Ticks + 10), "02:03:04.000001")
            .SetName("Interval_with_microsecond")
    ];

    [Test, TestCaseSource(nameof(IntervalValues))]
    public Task Interval_as_TimeSpan(TimeSpan timeSpan, string sqlLiteral)
        => AssertType(timeSpan, sqlLiteral, "interval", GaussDBDbType.Interval);

    [Test]
    public Task Interval_write_as_TimeSpan_truncates_ticks()
        => AssertTypeWrite(
            new TimeSpan(new TimeSpan(2, 3, 4).Ticks + 1),
            "02:03:04",
            "interval",
            GaussDBDbType.Interval);

    [Test]
    public Task Interval_as_GaussDBInterval()
        => AssertType(
            new GaussDBInterval(2, 15, 7384005000),
            "2 mons 15 days 02:03:04.005", "interval",
            GaussDBDbType.Interval,
            isDefaultForReading: false);

    [Test]
    public Task Interval_with_months_cannot_read_as_TimeSpan()
        => AssertTypeUnsupportedRead<TimeSpan, InvalidCastException>("1 month 2 days", "interval");

    #endregion

    protected override async ValueTask<GaussDBConnection> OpenConnectionAsync()
    {
        var conn = await base.OpenConnectionAsync();
        await conn.ExecuteNonQueryAsync("SET TimeZone='Europe/Berlin'");
        return conn;
    }

    protected override GaussDBConnection OpenConnection()
        => throw new NotSupportedException();
}
