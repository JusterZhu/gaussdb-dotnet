using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using static HuaweiCloud.GaussDB.Tests.TestUtil;

namespace HuaweiCloud.GaussDB.Tests;

[TestFixture(MultiplexingMode.NonMultiplexing, CommandBehavior.Default)]
[TestFixture(MultiplexingMode.Multiplexing, CommandBehavior.Default)]
[TestFixture(MultiplexingMode.NonMultiplexing, CommandBehavior.SequentialAccess)]
[TestFixture(MultiplexingMode.Multiplexing, CommandBehavior.SequentialAccess)]
public class BatchTests : MultiplexingTestBase
{
    #region Parameters

    [Test]
    public async Task Named_parameters()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT @p") { Parameters = { new("p", 8) } },
                new("SELECT @p1, @p2") { Parameters = { new("p1", 9), new("p2", 10) } }
            }
        };

        await using var reader = await batch.ExecuteReaderAsync(Behavior);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader[0], Is.EqualTo(8));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader[0], Is.EqualTo(9));
        Assert.That(reader[1], Is.EqualTo(10));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.False);
    }

    [Test]
    public async Task Positional_parameters()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT $1") { Parameters = { new() { Value = 8 } } },
                new("SELECT $1, $2") { Parameters = { new() { Value = 9 }, new() { Value = 10 } } }
            }
        };

        await using var reader = await batch.ExecuteReaderAsync(Behavior);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader[0], Is.EqualTo(8));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.True);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(reader[0], Is.EqualTo(9));
        Assert.That(reader[1], Is.EqualTo(10));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.False);
    }

    #endregion Parameters

    #region GaussDBBatchCommand

    [Test]
    public async Task RecordsAffected_and_Rows()
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES ('a'), ('b')"),
                new($"UPDATE {table} SET name='c' WHERE name='b'"),
                new($"UPDATE {table} SET name='d' WHERE name='doesnt_exist'"),
                new($"SELECT name FROM {table}"),
                new($"DELETE FROM {table}")
            }
        };
        await using var reader = await batch.ExecuteReaderAsync(Behavior);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        var command = batch.BatchCommands[0];
        Assert.That(command.RecordsAffected, Is.EqualTo(2));
        Assert.That(command.Rows, Is.EqualTo(2));

        command = batch.BatchCommands[1];
        Assert.That(command.RecordsAffected, Is.EqualTo(1));
        Assert.That(command.Rows, Is.EqualTo(1));

        command = batch.BatchCommands[2];
        Assert.That(command.RecordsAffected, Is.EqualTo(0));
        Assert.That(command.Rows, Is.EqualTo(0));

        command = batch.BatchCommands[3];
        Assert.That(command.RecordsAffected, Is.EqualTo(-1));
        Assert.That(command.Rows, Is.EqualTo(2));

        command = batch.BatchCommands[4];
        Assert.That(command.RecordsAffected, Is.EqualTo(2));
        Assert.That(command.Rows, Is.EqualTo(2));
    }

    [Test]
    public async Task Merge_RecordsAffected_and_Rows()
    {
        await using var conn = await OpenConnectionAsync();

        MinimumPgVersion(conn, "15.0", "MERGE statement was introduced in PostgreSQL 15");

        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES ('a'), ('b')"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN MATCHED THEN UPDATE SET name = 'c'"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN NOT MATCHED THEN INSERT (name) VALUES ('b')"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN MATCHED THEN DELETE"),
                new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN NOT MATCHED THEN DO NOTHING")
            }
        };
        await using var reader = await batch.ExecuteReaderAsync(Behavior);

        // Consume MERGE result set to parse the CommandComplete
        await reader.CloseAsync();

        var command = batch.BatchCommands[0];
        Assert.That(command.StatementType, Is.EqualTo(StatementType.Insert));
        Assert.That(command.RecordsAffected, Is.EqualTo(2));
        Assert.That(command.Rows, Is.EqualTo(2));

        command = batch.BatchCommands[1];
        Assert.That(command.StatementType, Is.EqualTo(StatementType.Merge));
        Assert.That(command.RecordsAffected, Is.EqualTo(1));
        Assert.That(command.Rows, Is.EqualTo(1));

        command = batch.BatchCommands[2];
        Assert.That(command.StatementType, Is.EqualTo(StatementType.Merge));
        Assert.That(command.RecordsAffected, Is.EqualTo(1));
        Assert.That(command.Rows, Is.EqualTo(1));

        command = batch.BatchCommands[3];
        Assert.That(command.StatementType, Is.EqualTo(StatementType.Merge));
        Assert.That(command.RecordsAffected, Is.EqualTo(1));
        Assert.That(command.Rows, Is.EqualTo(1));

        command = batch.BatchCommands[4];
        Assert.That(command.StatementType, Is.EqualTo(StatementType.Merge));
        Assert.That(command.RecordsAffected, Is.EqualTo(0));
        Assert.That(command.Rows, Is.EqualTo(0));
    }

    [Test]
    public async Task StatementTypes()
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES ('a'), ('b')"),
                new($"UPDATE {table} SET name='c' WHERE name='b'"),
                new($"UPDATE {table} SET name='d' WHERE name='doesnt_exist'"),
                new("BEGIN"),
                new($"SELECT name FROM {table}"),
                new($"DELETE FROM {table}"),
                new("COMMIT")
            }
        };

        await using var reader = await batch.ExecuteReaderAsync(Behavior);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        Assert.That(batch.BatchCommands[0].StatementType, Is.EqualTo(StatementType.Insert));
        Assert.That(batch.BatchCommands[1].StatementType, Is.EqualTo(StatementType.Update));
        Assert.That(batch.BatchCommands[2].StatementType, Is.EqualTo(StatementType.Update));
        Assert.That(batch.BatchCommands[3].StatementType, Is.EqualTo(StatementType.Other));
        Assert.That(batch.BatchCommands[4].StatementType, Is.EqualTo(StatementType.Select));
        Assert.That(batch.BatchCommands[5].StatementType, Is.EqualTo(StatementType.Delete));
        Assert.That(batch.BatchCommands[6].StatementType, Is.EqualTo(StatementType.Other));
    }

    [Test]
    public async Task StatementType_Call()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "11.0", "Stored procedures are supported starting with PG 11");

        var sproc = await GetTempProcedureName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE PROCEDURE {sproc}() LANGUAGE sql AS ''");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new($"CALL {sproc}()") }
        };

        await using var reader = await batch.ExecuteReaderAsync(Behavior);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        Assert.That(batch.BatchCommands[0].StatementType, Is.EqualTo(StatementType.Call));
    }

    [Test]
    public async Task CommandType_StoredProcedure()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "11.0", "Stored procedures are supported starting with PG 11");

        var sproc = await GetTempProcedureName(conn);
        await conn.ExecuteNonQueryAsync($"CREATE PROCEDURE {sproc}() LANGUAGE sql AS ''");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new($"{sproc}") {CommandType = CommandType.StoredProcedure} }
        };

        await using var reader = await batch.ExecuteReaderAsync(Behavior);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        Assert.That(batch.BatchCommands[0].StatementType, Is.EqualTo(StatementType.Call));
    }


    [Test]
    public async Task StatementType_Merge()
    {
        await using var conn = await OpenConnectionAsync();
        MinimumPgVersion(conn, "15.0", "Stored procedures are supported starting with PG 11");

        var table = await CreateTempTable(conn, "name TEXT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new($"MERGE INTO {table} S USING (SELECT 'b' as name) T ON T.name = S.name WHEN NOT MATCHED THEN DO NOTHING") }
        };

        await using var reader = await batch.ExecuteReaderAsync(Behavior);

        // Consume SELECT result set to parse the CommandComplete
        await reader.CloseAsync();

        Assert.That(batch.BatchCommands[0].StatementType, Is.EqualTo(StatementType.Merge));
    }

    [Test]
    public async Task StatementOID()
    {
        using var conn = await OpenConnectionAsync();

        MaximumPgVersionExclusive(conn, "12.0",
            "Support for 'CREATE TABLE ... WITH OIDS' has been removed in 12.0. See https://www.postgresql.org/docs/12/release-12.html#id-1.11.6.5.4");

        var table = await GetTempTableName(conn);
        //todo: 不支持 "CREATE TABLE {table} (name TEXT) WITH OIDS"
        //await conn.ExecuteNonQueryAsync($"CREATE TABLE {table} (name TEXT) WITH OIDS");
        await conn.ExecuteNonQueryAsync($"CREATE TABLE {table} (name TEXT)");
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (name) VALUES (@p1)") { Parameters = { new("p1", "foo") } },
                new($"UPDATE {table} SET name='b' WHERE name=@p2") { Parameters = { new("p2", "bar") } }
            }
        };

        await batch.ExecuteNonQueryAsync();

        //Assert.That(batch.BatchCommands[0].OID, Is.Not.EqualTo(0));
        Assert.That(batch.BatchCommands[1].OID, Is.EqualTo(0));
    }

    [Test]
    public void CanCreateParameter() => Assert.True(new GaussDBBatchCommand().CanCreateParameter);

    [Test]
    public void CreateParameter() => Assert.NotNull(new GaussDBBatchCommand().CreateParameter());

    #endregion GaussDBBatchCommand

    #region Command behaviors

    [Test]
    public async Task SingleResult()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };
        var reader = await batch.ExecuteReaderAsync(CommandBehavior.SingleResult | Behavior);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.NextResult(), Is.False);
    }

    [Test]
    public async Task SingleRow()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };

        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SingleRow | Behavior);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.NextResult(), Is.False);
    }

    [Test]
    public async Task SchemaOnly_GetFieldType()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 'foo'") }
        };

        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SchemaOnly | Behavior);
        Assert.That(reader.GetFieldType(0), Is.SameAs(typeof(int)));
        Assert.That(await reader.NextResultAsync(), Is.True);
        Assert.That(reader.GetFieldType(0), Is.SameAs(typeof(string)));
        Assert.That(await reader.NextResultAsync(), Is.False);
    }

    [Test]
    public async Task SchemaOnly_returns_no_data()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 'foo'") }
        };

        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SchemaOnly | Behavior);
        Assert.That(reader.Read(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.True);
        Assert.That(reader.Read(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.False);
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/693")]
    public async Task CloseConnection()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new("SELECT 1"), new("SELECT 2") }
        };

        await using (var reader = await batch.ExecuteReaderAsync(CommandBehavior.CloseConnection | Behavior))
            while (reader.Read()) {}
        Assert.That(conn.State, Is.EqualTo(ConnectionState.Closed));
    }

    #endregion Command behaviors

    #region Error barriers

    [Test]
    public async Task Batch_with_error_at_start([Values] bool withErrorBarriers)
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (8)")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        var exception = Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior))!;
        Assert.That(exception.BatchCommand, Is.SameAs(batch.BatchCommands[0]));

        Assert.That(await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}"), withErrorBarriers
            ? Is.EqualTo(1)
            : Is.EqualTo(0));
    }

    [Test]
    public async Task Batch_with_error_at_end([Values] bool withErrorBarriers)
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        var exception = Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior))!;
        Assert.That(exception.BatchCommand, Is.SameAs(batch.BatchCommands[1]));

        Assert.That(await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}"), withErrorBarriers
            ? Is.EqualTo(1)
            : Is.EqualTo(0));
    }

    [Test]
    public async Task Batch_with_multiple_errors([Values] bool withErrorBarriers)
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (10)")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        if (withErrorBarriers)
        {
            // A Sync is inserted after each command, so all commands are executed and all exceptions are thrown as an AggregateException
            var exception = Assert.ThrowsAsync<GaussDBException>(async () => await batch.ExecuteReaderAsync(Behavior))!;
            var aggregateException = (AggregateException)exception.InnerException!;
            Assert.That(((PostgresException)aggregateException.InnerExceptions[0]).BatchCommand, Is.SameAs(batch.BatchCommands[1]));
            Assert.That(((PostgresException)aggregateException.InnerExceptions[1]).BatchCommand, Is.SameAs(batch.BatchCommands[3]));

            Assert.That(await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}"), Is.EqualTo(3));
        }
        else
        {
            // PG skips all commands after the first error; an exception is only raised for the first one, and the entire batch is
            // rolled back (implicit transaction).
            var exception = Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior))!;
            Assert.That(exception.BatchCommand, Is.SameAs(batch.BatchCommands[1]));

            Assert.That(await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}"), Is.EqualTo(0));
        }

        Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
    }

    [Test]
    public async Task Batch_close_dispose_reader_with_multiple_errors([Values] bool withErrorBarriers, [Values] bool dispose)
    {
        // Create a temp pool since we dispose the reader (and check the state afterwards) and it can be reused by another connection
        await using var dataSource = CreateDataSource();
        await using var conn = await dataSource.OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT NULL WHERE 1=0"),
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (10)")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        await using (var reader = await batch.ExecuteReaderAsync(Behavior))
        {
            if (withErrorBarriers)
            {
                // A Sync is inserted after each command, so all commands are executed and all exceptions are thrown as an AggregateException
                var exception = Assert.ThrowsAsync<GaussDBException>(async () =>
                {
                    if (dispose)
                        await reader.DisposeAsync();
                    else
                        await reader.CloseAsync();
                })!;
                var aggregateException = (AggregateException)exception.InnerException!;
                Assert.That(((PostgresException)aggregateException.InnerExceptions[0]).BatchCommand, Is.SameAs(batch.BatchCommands[2]));
                Assert.That(((PostgresException)aggregateException.InnerExceptions[1]).BatchCommand, Is.SameAs(batch.BatchCommands[4]));
            }
            else
            {
                // PG skips all commands after the first error; an exception is only raised for the first one, and the entire batch is
                // rolled back (implicit transaction).
                var exception = Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    if (dispose)
                        await reader.DisposeAsync();
                    else
                        await reader.CloseAsync();
                })!;

                Assert.That(exception.BatchCommand, Is.SameAs(batch.BatchCommands[2]));
            }

            Assert.That(reader.State, Is.EqualTo(dispose ? ReaderState.Disposed : ReaderState.Closed));
        }

        Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
    }

    [Test]
    public async Task Batch_with_result_sets_and_error([Values] bool withErrorBarriers)
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("SELECT 1"),
                new("INVALID SQL"),
                new($"INSERT INTO {table} (id) VALUES (9)"),
                new("SELECT 2")
            },
            EnableErrorBarriers = withErrorBarriers
        };

        await using (var reader = await batch.ExecuteReaderAsync(Behavior))
        {
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader[0], Is.EqualTo(1));
            Assert.That(await reader.ReadAsync(), Is.False);

            Assert.That(async () => await reader.NextResultAsync(), Throws.Exception.TypeOf<PostgresException>());

            Assert.That(reader.State, Is.EqualTo(ReaderState.Consumed));
            Assert.That(await reader.ReadAsync(), Is.False);
            Assert.That(await reader.NextResultAsync(), Is.False);
        }

        Assert.That(await conn.ExecuteScalarAsync($"SELECT count(*) FROM {table}"), withErrorBarriers
            ? Is.EqualTo(2)
            : Is.EqualTo(0));
    }

    [Test]
    public async Task Error_with_AppendErrorBarrier()
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new("INVALID SQL") { AppendErrorBarrier = true },
                new($"INSERT INTO {table} (id) VALUES (9)")
            }
        };

        // A Sync is placed after the 2nd command (INVALID SQL), so the 1st command is rolled back but not the 3rd.
        var exception = Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior))!;
        Assert.That(exception.BatchCommand, Is.SameAs(batch.BatchCommands[1]));

        Assert.That(await conn.ExecuteScalarAsync($"SELECT id FROM {table} ORDER BY id"), Is.EqualTo(9));
    }

    [Test]
    public async Task AppendErrorBarrier_on_last_command([Values] bool enabled)
    {
        await using var conn = await OpenConnectionAsync();
        var table = await CreateTempTable(conn, "id INT");

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new($"INSERT INTO {table} (id) VALUES (8)"),
                new($"INSERT INTO {table} (id) VALUES (9)") { AppendErrorBarrier = enabled }
            },
            EnableErrorBarriers = true
        };

        Assert.That(await batch.ExecuteNonQueryAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task Error_barriers_with_SchemaOnly()
    {
        await using var conn = await OpenConnectionAsync();

        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands =
            {
                new("SELECT 1"),
                new("SELECT 'foo'")
            },
            EnableErrorBarriers = true
        };

        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SchemaOnly | Behavior);

        var columnSchema = await reader.GetColumnSchemaAsync();
        Assert.That(columnSchema[0].DataType, Is.SameAs(typeof(int)));

        Assert.That(await reader.NextResultAsync(), Is.True);
        columnSchema = await reader.GetColumnSchemaAsync();
        Assert.That(columnSchema[0].DataType, Is.SameAs(typeof(string)));
    }

    #endregion Error barriers

    #region Miscellaneous

    [Test]
    public async Task Single_batch_command()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new("SELECT 8") }
        };

        await using var reader = await batch.ExecuteReaderAsync(Behavior);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(1));
        Assert.That(reader[0], Is.EqualTo(8));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.False);
    }

    [Test]
    public async Task Empty_batch()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn);
        await using var reader = await batch.ExecuteReaderAsync(Behavior);

        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(await reader.NextResultAsync(), Is.False);
    }

    [Test]
    public async Task Semicolon_is_not_allowed()
    {
        await using var conn = await OpenConnectionAsync();
        await using var batch = new GaussDBBatch(conn)
        {
            BatchCommands = { new("SELECT 1; SELECT 2") }
        };

        Assert.That(() => batch.ExecuteReaderAsync(Behavior), Throws.Exception.TypeOf<NotSupportedException>());
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/967")]
    public async Task GaussDBException_references_BatchCommand_with_single_command()
    {
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'");

        // We use GaussDBConnection.CreateBatch to test that the batch isn't recycled when referenced in an exception
        var batch = conn.CreateBatch();
        batch.BatchCommands.Add(new($"SELECT {function}()"));

        var e = Assert.ThrowsAsync<PostgresException>(async () => await batch.ExecuteReaderAsync(Behavior))!;
        Assert.That(e.BatchCommand, Is.SameAs(batch.BatchCommands[0]));

        // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
        // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
        batch.Dispose();
        var cmd2 = conn.CreateBatch();
        Assert.AreNotSame(cmd2, batch);
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/967")]
    public async Task GaussDBException_references_BatchCommand_with_multiple_commands()
    {
        await using var conn = await OpenConnectionAsync();
        var function = await GetTempFunctionName(conn);

        await conn.ExecuteNonQueryAsync($@"
CREATE OR REPLACE FUNCTION {function}() RETURNS VOID AS
   'BEGIN RAISE EXCEPTION ''testexception'' USING ERRCODE = ''12345''; END;'
LANGUAGE 'plpgsql'");

        // We use GaussDBConnection.CreateBatch to test that the batch isn't recycled when referenced in an exception
        var batch = conn.CreateBatch();
        batch.BatchCommands.Add(new("SELECT 1"));
        batch.BatchCommands.Add(new($"SELECT {function}()"));

        await using (var reader = await batch.ExecuteReaderAsync(Behavior))
        {
            var e = Assert.ThrowsAsync<PostgresException>(async () => await reader.NextResultAsync())!;
            Assert.That(e.BatchCommand, Is.SameAs(batch.BatchCommands[1]));
        }

        // Make sure the command isn't recycled by the connection when it's disposed - this is important since internal command
        // resources are referenced by the exception above, which is very likely to escape the using statement of the command.
        batch.Dispose();
        var cmd2 = conn.CreateBatch();
        Assert.AreNotSame(cmd2, batch);
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/4202")]
    public async Task ExecuteScalar_without_parameters()
    {
        await using var conn = await OpenConnectionAsync();
        var batch = new GaussDBBatch(conn) { BatchCommands = { new("SELECT 1") } };
        Assert.That(await batch.ExecuteScalarAsync(), Is.EqualTo(1));
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/4264")]
    public async Task Batch_with_auto_prepare_reuse()
    {
        await using var dataSource = CreateDataSource(csb => csb.MaxAutoPrepare = 20);
        await using var conn = await dataSource.OpenConnectionAsync();

        var tempTableName = await CreateTempTable(conn, "id int");

        await using var batch = new GaussDBBatch(conn);
        for (var i = 0; i < 2; ++i)
        {
            for (var j = 0; j < 10; ++j)
            {
                batch.BatchCommands.Add(new GaussDBBatchCommand($"DELETE FROM {tempTableName} WHERE 1=0"));
            }
            await batch.ExecuteNonQueryAsync();
            batch.BatchCommands.Clear();
        }
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/5239")]
    public async Task Batch_dispose_reuse()
    {
        await using var conn = await OpenConnectionAsync();
        GaussDBBatch firstBatch;
        await using (var batch = conn.CreateBatch())
        {
            firstBatch = batch;

            batch.BatchCommands.Add(new GaussDBBatchCommand("SELECT 1"));
            Assert.That(await batch.ExecuteScalarAsync(), Is.EqualTo(1));
        }

        await using (var batch = conn.CreateBatch())
        {
            Assert.That(batch, Is.SameAs(firstBatch));

            batch.BatchCommands.Add(new GaussDBBatchCommand("SELECT 2"));
            Assert.That(await batch.ExecuteScalarAsync(), Is.EqualTo(2));
        }

        await conn.CloseAsync();
        await conn.OpenAsync();

        await using (var batch = conn.CreateBatch())
        {
            Assert.That(batch, Is.SameAs(firstBatch));

            batch.BatchCommands.Add(new GaussDBBatchCommand("SELECT 3"));
            Assert.That(await batch.ExecuteScalarAsync(), Is.EqualTo(3));
        }
    }

    #endregion Miscellaneous

    #region Initialization / setup / teardown

    // ReSharper disable InconsistentNaming
    readonly bool IsSequential;
    readonly CommandBehavior Behavior;
    // ReSharper restore InconsistentNaming

    public BatchTests(MultiplexingMode multiplexingMode, CommandBehavior behavior) : base(multiplexingMode)
    {
        Behavior = behavior;
        IsSequential = (Behavior & CommandBehavior.SequentialAccess) != 0;
    }

    #endregion
}
