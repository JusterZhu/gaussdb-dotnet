/*using System;
using System.Data;
using System.Threading.Tasks;
using NodaTime;
using HuaweiCloud.GaussDB.NodaTime.Properties;
using HuaweiCloud.GaussDB.Tests;
using HuaweiCloud.GaussDBTypes;
using NUnit.Framework;
using static HuaweiCloud.GaussDB.Tests.TestUtil;

// ReSharper disable AccessToModifiedClosure
// ReSharper disable AccessToDisposedClosure

namespace HuaweiCloud.GaussDB.PluginTests;

public class NodaTimeTests : MultiplexingTestBase, IDisposable
{
    #region Timestamp without time zone

    static readonly TestCaseData[] TimestampValues =
    [
        new TestCaseData(new LocalDateTime(1998, 4, 12, 13, 26, 38, 789), "1998-04-12 13:26:38.789")
            .SetName("Timestamp_pre2000"),
        new TestCaseData(new LocalDateTime(2015, 1, 27, 8, 45, 12, 345), "2015-01-27 08:45:12.345")
            .SetName("Timestamp_post2000"),
        new TestCaseData(new LocalDateTime(1999, 12, 31, 23, 59, 59, 999).PlusNanoseconds(456000), "1999-12-31 23:59:59.999456")
            .SetName("Timestamp_with_microseconds")
    ];

    [Test, TestCaseSource(nameof(TimestampValues))]
    public Task Timestamp_as_LocalDateTime(LocalDateTime localDateTime, string sqlLiteral)
        => AssertType(localDateTime, sqlLiteral, "timestamp without time zone", GaussDBDbType.Timestamp, DbType.DateTime2,
            isGaussDBDbTypeInferredFromClrType: false);

    [Test]
    public Task Timestamp_as_unspecified_DateTime()
        => AssertType(
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Unspecified),
            "1998-04-12 13:26:38",
            "timestamp without time zone",
            GaussDBDbType.Timestamp,
            DbType.DateTime2,
            isDefaultForReading: false);

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
    public Task Timestamp_cannot_use_as_Instant()
        => AssertTypeUnsupported(
            new LocalDateTime(1998, 4, 12, 13, 26, 38, 789).InUtc().ToInstant(),
            "1998-04-12 13:26:38.789",
            "timestamp without time zone");

    [Test]
    public Task Timestamp_cannot_use_as_ZonedDateTime()
        => AssertTypeUnsupported(
            new LocalDateTime(1998, 4, 12, 13, 26, 38, 789).InUtc(),
            "1998-04-12 13:26:38.789",
            "timestamp without time zone");

    [Test]
    public Task Timestamp_cannot_use_as_OffsetDateTime()
        => AssertTypeUnsupported(
            new LocalDateTime(1998, 4, 12, 13, 26, 38, 789).WithOffset(Offset.FromHours(2)),
            "1998-04-12 13:26:38.789",
            "timestamp without time zone");

    [Test]
    public Task Timestamp_cannot_use_as_DateTimeOffset()
        => AssertTypeUnsupported(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 13:26:38",
            "timestamp without time zone");

    [Test]
    public Task Timestamp_cannot_write_utc_DateTime()
        => AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc), "timestamp without time zone");

    [Test]
    public async Task Tsrange_as_GaussDBRange_of_LocalDateTime()
    {
        await AssertType(
            new GaussDBRange<LocalDateTime>(
                new(1998, 4, 12, 13, 26, 38),
                new(1998, 4, 12, 15, 26, 38)),
            """["1998-04-12 13:26:38","1998-04-12 15:26:38"]""",
            "tsrange",
            GaussDBDbType.TimestampRange,
            isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true); // GaussDBRange<T>[] is mapped to multirange by default, not array; test separately

        await AssertType(
            new [] { new GaussDBRange<LocalDateTime>(
                new(1998, 4, 12, 13, 26, 38),
                new(1998, 4, 12, 15, 26, 38)), },
            """{"[\"1998-04-12 13:26:38\",\"1998-04-12 15:26:38\"]"}""",
            "tsrange[]",
            GaussDBDbType.TimestampRange | GaussDBDbType.Array,
            isDefault: false, skipArrayCheck: true);

        await using var conn = await OpenConnectionAsync();
        if (conn.PostgreSqlVersion < new Version(14, 0))
            return;

        await AssertType(
            new [] { new GaussDBRange<LocalDateTime>(
                new(1998, 4, 12, 13, 26, 38),
                new(1998, 4, 12, 15, 26, 38)), },
            """{["1998-04-12 13:26:38","1998-04-12 15:26:38"]}""",
            "tsmultirange",
            GaussDBDbType.TimestampMultirange, isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true);
    }

    [Test]
    public async Task Tsmultirange_as_array_of_GaussDBRange_of_LocalDateTime()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<LocalDateTime>(
                    new(1998, 4, 12, 13, 26, 38),
                    new(1998, 4, 12, 15, 26, 38)),
                new GaussDBRange<LocalDateTime>(
                    new(1998, 4, 13, 13, 26, 38),
                    new(1998, 4, 13, 15, 26, 38)),
            },
            """{["1998-04-12 13:26:38","1998-04-12 15:26:38"],["1998-04-13 13:26:38","1998-04-13 15:26:38"]}""",
            "tsmultirange",
            GaussDBDbType.TimestampMultirange,
            isGaussDBDbTypeInferredFromClrType: false);
    }

    #endregion Timestamp without time zone

    #region Timestamp with time zone

    static readonly TestCaseData[] TimestamptzValues =
    [
        new TestCaseData(new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(), "1998-04-12 15:26:38+02")
            .SetName("Timestamptz_pre2000"),
        new TestCaseData(new LocalDateTime(2015, 1, 27, 8, 45, 12, 345).InUtc().ToInstant(), "2015-01-27 09:45:12.345+01")
            .SetName("Timestamptz_post2000"),
        new TestCaseData(new LocalDateTime(2013, 7, 25, 0, 0, 0).InUtc().ToInstant(), "2013-07-25 02:00:00+02")
            .SetName("Timestamptz_write_date_only"),
        new TestCaseData(new LocalDateTime(1999, 12, 31, 23, 59, 59, 999).PlusNanoseconds(456000).InUtc().ToInstant(), "2000-01-01 00:59:59.999456+01")
            .SetName("Timestamptz_with_microseconds")
    ];

    [Test, TestCaseSource(nameof(TimestamptzValues))]
    public Task Timestamptz_as_Instant(Instant instant, string sqlLiteral)
        => AssertType(instant, sqlLiteral, "timestamp with time zone", GaussDBDbType.TimestampTz, DbType.DateTime,
            isGaussDBDbTypeInferredFromClrType: false);

    [Test]
    public Task Timestamptz_as_ZonedDateTime()
        => AssertType(
            new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc(),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            GaussDBDbType.TimestampTz,
            DbType.DateTime,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false);

    [Test]
    public Task Timestamptz_as_OffsetDateTime()
        => AssertType(
            new LocalDateTime(1998, 4, 12, 13, 26, 38).WithOffset(Offset.Zero),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            GaussDBDbType.TimestampTz,
            DbType.DateTime,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false);

    [Test]
    public Task Timestamptz_as_utc_DateTime()
        => AssertType(
            new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Utc),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            GaussDBDbType.TimestampTz,
            DbType.DateTime,
            isDefaultForReading: false);

    [Test]
    public Task Timestamptz_as_DateTimeOffset()
        => AssertType(
            new DateTimeOffset(1998, 4, 12, 13, 26, 38, TimeSpan.Zero),
            "1998-04-12 15:26:38+02",
            "timestamp with time zone",
            GaussDBDbType.TimestampTz,
            DbType.DateTime,
            isDefaultForReading: false);

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
    public Task Timestamptz_cannot_use_as_LocalDateTime()
        => AssertTypeUnsupported(new LocalDateTime(1998, 4, 12, 13, 26, 38), "1998-04-12 13:26:38Z", "timestamp with time zone");

    [Test]
    public async Task Timestamptz_cannot_write_non_utc_ZonedDateTime()
        => await AssertTypeUnsupportedWrite<ZonedDateTime, ArgumentException>(
            new LocalDateTime().InUtc().ToInstant().InZone(DateTimeZoneProviders.Tzdb["Europe/Berlin"]),
            "timestamp with time zone");

    [Test]
    public async Task Timestamptz_cannot_write_non_utc_OffsetDateTime()
        => await AssertTypeUnsupportedWrite<OffsetDateTime, ArgumentException>(new LocalDateTime().WithOffset(Offset.FromHours(2)), "timestamp with time zone");

    [Test]
    public async Task Timestamptz_cannot_write_non_utc_DateTime()
    {
        await AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Unspecified), "timestamp with time zone");
        await AssertTypeUnsupportedWrite<DateTime, ArgumentException>(new DateTime(1998, 4, 12, 13, 26, 38, DateTimeKind.Local), "timestamp with time zone");
    }

    [Test]
    public async Task Tstzrange_as_Interval()
    {
         await AssertType(
            new Interval(
                new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()),
            """["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02")""",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true); // GaussDBRange<T>[] is mapped to multirange by default, not array; test separately

         await AssertType(
            new [] { new Interval(
                new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()), },
            """{"[\"1998-04-12 15:26:38+02\",\"1998-04-12 17:26:38+02\")"}""",
            "tstzrange[]",
            GaussDBDbType.TimestampTzRange | GaussDBDbType.Array,
            isDefault: false, skipArrayCheck: true);

         await using var conn = await OpenConnectionAsync();
         if (conn.PostgreSqlVersion < new Version(14, 0))
             return;

         await AssertType(
             new [] { new Interval(
                 new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                 new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()), },
             """{["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02")}""",
             "tstzmultirange",
             GaussDBDbType.TimestampTzMultirange, isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true);
    }

    [Test]
    public Task Tstzrange_with_no_end_as_Interval()
        => AssertType(
            new Interval(new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(), null),
            """["1998-04-12 15:26:38+02",)""",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true);

    [Test]
    public Task Tstzrange_with_no_start_as_Interval()
        => AssertType(
            new Interval(null, new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant()),
            """(,"1998-04-12 15:26:38+02")""",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true);

    [Test]
    public Task Tstzrange_with_no_start_or_end_as_Interval()
        => AssertType(
            new Interval(null, null),
            """(,)""",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true);

    [Test]
    public Task Tstzrange_as_GaussDBRange_of_Instant()
        => AssertType(
            new GaussDBRange<Instant>(
                new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()),
            """["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02"]""",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false, skipArrayCheck: true);

    [Test]
    public Task Tstzrange_as_GaussDBRange_of_ZonedDateTime()
        => AssertType(
            new GaussDBRange<ZonedDateTime>(
                new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc(),
                new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc()),
            """["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02"]""",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false, skipArrayCheck: true);

    [Test]
    public Task Tstzrange_as_GaussDBRange_of_OffsetDateTime()
        => AssertType(
            new GaussDBRange<OffsetDateTime>(
                new LocalDateTime(1998, 4, 12, 13, 26, 38).WithOffset(Offset.Zero),
                new LocalDateTime(1998, 4, 12, 15, 26, 38).WithOffset(Offset.Zero)),
            """["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02"]""",
            "tstzrange",
            GaussDBDbType.TimestampTzRange,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false, skipArrayCheck: true);

    [Test]
    public async Task Tstzmultirange_as_array_of_Interval()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new Interval(
                    new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()),
                new Interval(
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 13, 15, 26, 38).InUtc().ToInstant()),
            },
            """{["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02"),["1998-04-13 15:26:38+02","1998-04-13 17:26:38+02")}""",
            "tstzmultirange",
            GaussDBDbType.TimestampTzMultirange,
            isGaussDBDbTypeInferredFromClrType: false);
    }

    [Test]
    public async Task Tstzmultirange_as_array_of_GaussDBRange_of_Instant()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<Instant>(
                    new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()),
                new GaussDBRange<Instant>(
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 13, 15, 26, 38).InUtc().ToInstant()),
            },
            """{["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02"],["1998-04-13 15:26:38+02","1998-04-13 17:26:38+02"]}""",
            "tstzmultirange",
            GaussDBDbType.TimestampTzMultirange,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false);
    }

    [Test]
    public async Task Tstzmultirange_as_array_of_GaussDBRange_of_ZonedDateTime()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<ZonedDateTime>(
                    new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc(),
                    new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc()),
                new GaussDBRange<ZonedDateTime>(
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).InUtc(),
                    new LocalDateTime(1998, 4, 13, 15, 26, 38).InUtc()),
            },
            """{["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02"],["1998-04-13 15:26:38+02","1998-04-13 17:26:38+02"]}""",
            "tstzmultirange",
            GaussDBDbType.TimestampTzMultirange,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false);
    }

    [Test]
    public async Task Tstzmultirange_as_array_of_GaussDBRange_of_OffsetDateTime()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<OffsetDateTime>(
                    new LocalDateTime(1998, 4, 12, 13, 26, 38).WithOffset(Offset.Zero),
                    new LocalDateTime(1998, 4, 12, 15, 26, 38).WithOffset(Offset.Zero)),
                new GaussDBRange<OffsetDateTime>(
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).WithOffset(Offset.Zero),
                    new LocalDateTime(1998, 4, 13, 15, 26, 38).WithOffset(Offset.Zero)),
            },
            """{["1998-04-12 15:26:38+02","1998-04-12 17:26:38+02"],["1998-04-13 15:26:38+02","1998-04-13 17:26:38+02"]}""",
            "tstzmultirange",
            GaussDBDbType.TimestampTzMultirange,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false);
    }

    [Test]
    public async Task Tstzrange_array_as_array_of_Interval()
    {
        await using var conn = await OpenConnectionAsync();

        await AssertType<Array>(
            new[]
            {
                new Interval(
                    new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()),
                new Interval(
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 13, 15, 26, 38).InUtc().ToInstant()),
                new Interval(
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).InUtc().ToInstant(),
                    null),
                new Interval(
                    null,
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).InUtc().ToInstant()),
                new Interval(
                    null,
                    null)
            },
            """{"[\"1998-04-12 15:26:38+02\",\"1998-04-12 17:26:38+02\")","[\"1998-04-13 15:26:38+02\",\"1998-04-13 17:26:38+02\")","[\"1998-04-13 15:26:38+02\",)","(,\"1998-04-13 15:26:38+02\")","(,)"}""",
            "tstzrange[]",
            GaussDBDbType.TimestampTzRange | GaussDBDbType.Array,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForWriting: false);
    }

    [Test]
    public async Task Tstzrange_array_as_array_of_GaussDBRange_of_Instant()
    {
        await using var conn = await OpenConnectionAsync();

        await AssertType(
            new[]
            {
                new GaussDBRange<Instant>(
                    new LocalDateTime(1998, 4, 12, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 12, 15, 26, 38).InUtc().ToInstant()),
                new GaussDBRange<Instant>(
                    new LocalDateTime(1998, 4, 13, 13, 26, 38).InUtc().ToInstant(),
                    new LocalDateTime(1998, 4, 13, 15, 26, 38).InUtc().ToInstant()),
            },
            """{"[\"1998-04-12 15:26:38+02\",\"1998-04-12 17:26:38+02\"]","[\"1998-04-13 15:26:38+02\",\"1998-04-13 17:26:38+02\"]"}""",
            "tstzrange[]",
            GaussDBDbType.TimestampTzRange | GaussDBDbType.Array,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefault: false);
    }

    #endregion Timestamp with time zone

    #region Date

    [Test]
    public Task Date_as_LocalDate()
        => AssertType(new LocalDate(2020, 10, 1), "2020-10-01", "date", GaussDBDbType.Date, DbType.Date,
            isGaussDBDbTypeInferredFromClrType: false);

    [Test]
    public Task Date_as_DateTime()
        => AssertType(new DateTime(2020, 10, 1), "2020-10-01", "date", GaussDBDbType.Date, DbType.Date, isDefault: false);

    [Test]
    public Task Date_as_int()
        => AssertType(7579, "2020-10-01", "date", GaussDBDbType.Date, DbType.Date, isDefault: false);

    [Test]
    public async Task Daterange_as_DateInterval()
    {
        await AssertType(
            new DateInterval(new(2002, 3, 4), new(2002, 3, 6)),
            "[2002-03-04,2002-03-07)",
            "daterange",
            GaussDBDbType.DateRange,
            isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true); // DateInterval<T>[] is mapped to multirange by default, not array; test separately

        await AssertType(
            new [] {new DateInterval(new(2002, 3, 4), new(2002, 3, 6))},
            """{"[2002-03-04,2002-03-07)"}""",
            "daterange[]",
            GaussDBDbType.DateRange | GaussDBDbType.Array,
            isDefault: false, skipArrayCheck: true);

        await using var conn = await OpenConnectionAsync();
        if (conn.PostgreSqlVersion < new Version(14, 0))
            return;

        await AssertType(
            new [] {new DateInterval(new(2002, 3, 4), new(2002, 3, 6))},
            """{[2002-03-04,2002-03-07)}""",
            "datemultirange",
            GaussDBDbType.DateMultirange, isGaussDBDbTypeInferredFromClrType: false, skipArrayCheck: true);
    }

    [Test]
    public async Task Daterange_as_GaussDBRange_of_LocalDate()
    {
         await AssertType(
            new GaussDBRange<LocalDate>(new(2002, 3, 4), true, new(2002, 3, 6), false),
            "[2002-03-04,2002-03-06)",
            "daterange",
            GaussDBDbType.DateRange,
            isGaussDBDbTypeInferredFromClrType: false,
            isDefaultForReading: false, skipArrayCheck: true); // GaussDBRange<T>[] is mapped to multirange by default, not array; test separately

         await AssertType(
             new [] { new GaussDBRange<LocalDate>(new(2002, 3, 4), true, new(2002, 3, 6), false) },
             """{"[2002-03-04,2002-03-06)"}""",
             "daterange[]",
             GaussDBDbType.DateRange | GaussDBDbType.Array,
             isDefault: false, skipArrayCheck: true);

         await using var conn = await OpenConnectionAsync();
         if (conn.PostgreSqlVersion < new Version(14, 0))
             return;

         await AssertType(
             new [] { new GaussDBRange<LocalDate>(new(2002, 3, 4), true, new(2002, 3, 6), false) },
             """{[2002-03-04,2002-03-06)}""",
             "datemultirange",
             GaussDBDbType.DateMultirange, isDefault: false, skipArrayCheck: true);
    }

    [Test]
    public async Task Datemultirange_as_array_of_DateInterval()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new DateInterval(new(2002, 3, 4), new(2002, 3, 5)),
                new DateInterval(new(2002, 3, 8), new(2002, 3, 10))
            },
            "{[2002-03-04,2002-03-06),[2002-03-08,2002-03-11)}",
            "datemultirange",
            GaussDBDbType.DateMultirange,
            isGaussDBDbTypeInferredFromClrType: false);
    }

    [Test]
    public async Task Datemultirange_as_array_of_GaussDBRange_of_LocalDate()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "14.0", "Multirange types were introduced in PostgreSQL 14");

        await AssertType(
            new[]
            {
                new GaussDBRange<LocalDate>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new GaussDBRange<LocalDate>(new(2002, 3, 8), true, new(2002, 3, 11), false)
            },
            "{[2002-03-04,2002-03-06),[2002-03-08,2002-03-11)}",
            "datemultirange",
            GaussDBDbType.DateMultirange,
            isDefaultForReading: false,
            isGaussDBDbTypeInferredFromClrType: false);
    }

    [Test]
    public Task Date_as_DateOnly()
        => AssertType(new DateOnly(2020, 10, 1), "2020-10-01", "date", GaussDBDbType.Date, DbType.Date, isDefaultForReading: false);

    [Test]
    public async Task Daterange_as_GaussDBRange_of_DateOnly()
    {
        await AssertType(
            new GaussDBRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false),
            "[2002-03-04,2002-03-06)",
            "daterange",
            GaussDBDbType.DateRange,
            isDefaultForReading: false, skipArrayCheck: true);

        await AssertType(
            new [] { new GaussDBRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false) },
            """{"[2002-03-04,2002-03-06)"}""",
            "daterange[]",
            GaussDBDbType.DateRange | GaussDBDbType.Array,
            isDefault: false, skipArrayCheck: true);

        await using var conn = await OpenConnectionAsync();
        if (conn.PostgreSqlVersion < new Version(14, 0))
            return;

        await AssertType(
            new [] { new GaussDBRange<DateOnly>(new(2002, 3, 4), true, new(2002, 3, 6), false) },
            """{[2002-03-04,2002-03-06)}""",
            "datemultirange",
            GaussDBDbType.DateMultirange, isDefault: false, skipArrayCheck: true);
    }

    [Test]
    public async Task Daterange_array_as_array_of_DateInterval()
    {
        await using var conn = await OpenConnectionAsync();

        await AssertType<Array>(
            new[]
            {
                new DateInterval(new(2002, 3, 4), new(2002, 3, 5)),
                new DateInterval(new(2002, 3, 8), new(2002, 3, 10))
            },
            """{"[2002-03-04,2002-03-06)","[2002-03-08,2002-03-11)"}""",
            "daterange[]",
            GaussDBDbType.DateRange | GaussDBDbType.Array,
            isDefaultForWriting: false);
    }

    [Test]
    public async Task Daterange_array_as_array_of_GaussDBRange_of_LocalDate()
    {
        await using var conn = await OpenConnectionAsync();

        await AssertType(
            new[]
            {
                new GaussDBRange<LocalDate>(new(2002, 3, 4), true, new(2002, 3, 6), false),
                new GaussDBRange<LocalDate>(new(2002, 3, 8), true, new(2002, 3, 11), false)
            },
            """{"[2002-03-04,2002-03-06)","[2002-03-08,2002-03-11)"}""",
            "daterange[]",
            GaussDBDbType.DateRange | GaussDBDbType.Array,
            isDefault: false);
    }

    #endregion Date

    #region Time

    [Test]
    public Task Time_as_LocalTime()
        => AssertType(new LocalTime(10, 45, 34, 500), "10:45:34.5", "time without time zone", GaussDBDbType.Time, DbType.Time,
            isGaussDBDbTypeInferredFromClrType: false);

    [Test]
    public Task Time_as_TimeSpan()
        => AssertType(
            new TimeSpan(0, 10, 45, 34, 500),
            "10:45:34.5",
            "time without time zone",
            GaussDBDbType.Time,
            DbType.Time,
            isDefault: false);

    [Test]
    public Task Time_as_TimeOnly()
        => AssertType(
            new TimeOnly(10, 45, 34, 500),
            "10:45:34.5",
            "time without time zone",
            GaussDBDbType.Time,
            DbType.Time,
            isDefaultForReading: false);

    #endregion Time

    #region Time with time zone

    [Test]
    public Task TimeTz_as_OffsetTime()
        => AssertType(
            new OffsetTime(new LocalTime(1, 2, 3, 4).PlusNanoseconds(5000), Offset.FromHoursAndMinutes(3, 30) + Offset.FromSeconds(5)),
            "01:02:03.004005+03:30:05",
            "time with time zone",
            GaussDBDbType.TimeTz,
            isGaussDBDbTypeInferredFromClrType: false);

    [Test]
    public async Task TimeTz_as_DateTimeOffset()
    {
        await AssertTypeRead(
            "13:03:45.51+02",
            "time with time zone",
            new DateTimeOffset(1, 1, 2, 13, 3, 45, 510, TimeSpan.FromHours(2)), isDefault: false);

        await AssertTypeWrite(
            new DateTimeOffset(1, 1, 1, 13, 3, 45, 510, TimeSpan.FromHours(2)),
            "13:03:45.51+02",
            "time with time zone",
            GaussDBDbType.TimeTz,
            isDefault: false);
    }

    #endregion Time with time zone

    #region Interval

    [Test]
    public Task Interval_as_Period()
        => AssertType(
            new PeriodBuilder
            {
                Years = 1,
                Months = 2,
                Weeks = 3,
                Days = 4,
                Hours = 5,
                Minutes = 6,
                Seconds = 7,
                Milliseconds = 8,
                Nanoseconds = 9000
            }.Build().Normalize(),
            "1 year 2 mons 25 days 05:06:07.008009",
            "interval",
            GaussDBDbType.Interval,
            isGaussDBDbTypeInferredFromClrType: false);

    [Test]
    public Task Interval_as_Duration()
        => AssertType(
            Duration.FromDays(5) + Duration.FromMinutes(4) + Duration.FromSeconds(3) + Duration.FromMilliseconds(2) +
            Duration.FromNanoseconds(1000),
            "5 days 00:04:03.002001",
            "interval",
            GaussDBDbType.Interval,
            isDefaultForReading: false,
            isGaussDBDbTypeInferredFromClrType: false);

    [Test]
    public async Task Interval_as_Duration_with_months_fails()
    {
        var exception = await AssertTypeUnsupportedRead<Duration, InvalidCastException>("2 months", "interval");
        Assert.That(exception.Message, Is.EqualTo(GaussDBNodaTimeStrings.CannotReadIntervalWithMonthsAsDuration));
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/3438")]
    public async Task Bug3438()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new GaussDBCommand("SELECT @p1, @p2", conn);

        var expected = Duration.FromSeconds(2148);

        cmd.Parameters.Add(new GaussDBParameter("p1", GaussDBDbType.Interval) { Value = expected });
        cmd.Parameters.AddWithValue("p2", expected);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        for (var i = 0; i < 2; i++)
        {
            Assert.That(reader.GetFieldType(i), Is.EqualTo(typeof(Period)));
        }
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/5867")]
    public async Task Normalize_period_on_write()
    {
        var value = Period.FromTicks(-3675048768766);
        var expected = value.Normalize();
        var expectedAfterRoundtripBuilder = expected.ToBuilder();
        // Postgres doesn't support nanoseconds, trim them to microseconds
        expectedAfterRoundtripBuilder.Nanoseconds -= expected.Nanoseconds % 1000;
        var expectedAfterRoundtrip = expectedAfterRoundtripBuilder.Build();

        await using var conn = await OpenConnectionAsync();
        await using var cmd = new GaussDBCommand("SELECT $1, $2", conn);
        cmd.Parameters.AddWithValue(value);
        cmd.Parameters.AddWithValue(expected);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        var dbValue = reader.GetFieldValue<Period>(0);
        var dbExpected = reader.GetFieldValue<Period>(1);

        Assert.That(dbValue, Is.EqualTo(dbExpected));
        Assert.That(dbValue, Is.EqualTo(expectedAfterRoundtrip));
    }

    [Test]
    public async Task Period_write_throw_on_overflow()
    {
        var periodBuilder = new PeriodBuilder
        {
            Years = int.MaxValue
        };
        var ex = await AssertTypeUnsupportedWrite<Period, ArgumentException>(periodBuilder.Build(), "interval");
        Assert.That(ex.Message, Is.EqualTo(GaussDBNodaTimeStrings.CannotWritePeriodDueToOverflow));
        Assert.That(ex.InnerException, Is.TypeOf<OverflowException>());
    }

    #endregion Interval

    #region Support

    protected override GaussDBDataSource DataSource { get; }

    public NodaTimeTests(MultiplexingMode multiplexingMode)
        : base(multiplexingMode)
    {
        var builder = CreateDataSourceBuilder();
        builder.UseNodaTime();
        builder.ConnectionStringBuilder.Options = "-c TimeZone=Europe/Berlin";
        DataSource = builder.Build();
    }

    public void Dispose()
        => DataSource.Dispose();

    #endregion Support
}
*/
