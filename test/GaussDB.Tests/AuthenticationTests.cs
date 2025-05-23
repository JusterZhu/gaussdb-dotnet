using System;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HuaweiCloud.GaussDB.Properties;
using HuaweiCloud.GaussDB.Tests.Support;
using NUnit.Framework;
using static HuaweiCloud.GaussDB.Tests.TestUtil;

namespace HuaweiCloud.GaussDB.Tests;

public class AuthenticationTests(MultiplexingMode multiplexingMode) : MultiplexingTestBase(multiplexingMode)
{
    [Test]
    [NonParallelizable] // Sets environment variable
    public async Task Connect_UserNameFromEnvironment_Succeeds()
    {
        using var _ = SetEnvironmentVariable("PGUSER", new GaussDBConnectionStringBuilder(ConnectionString).Username);
        await using var dataSource = CreateDataSource(csb => csb.Username = null);
        await using var __ = await dataSource.OpenConnectionAsync();
    }

    [Test]
    [NonParallelizable] // Sets environment variable
    public async Task Connect_PasswordFromEnvironment_Succeeds()
    {
        using var _ = SetEnvironmentVariable("PGPASSWORD", new GaussDBConnectionStringBuilder(ConnectionString).Password);
        await using var dataSource = CreateDataSource(csb => csb.Passfile = null);
        await using var __ = await dataSource.OpenConnectionAsync();
    }

    [Test]
    public async Task Set_Password_on_GaussDBDataSource()
    {
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        await using var dataSource = dataSourceBuilder.Build();

        // No password provided
        Assert.That(() => dataSource.OpenConnectionAsync(), Throws.Exception.TypeOf<GaussDBException>());

        var connectionStringBuilder = new GaussDBConnectionStringBuilder(TestUtil.ConnectionString);
        dataSource.Password = connectionStringBuilder.Password!;

        await using var connection1 = await dataSource.OpenConnectionAsync();
        await using var connection2 = dataSource.OpenConnection();
    }

    [Test]
    public async Task Password_provider([Values]bool async)
    {
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        var password = new GaussDBConnectionStringBuilder(TestUtil.ConnectionString).Password!;
        var syncProviderCalled = false;
        var asyncProviderCalled = false;
        dataSourceBuilder.UsePasswordProvider(_ =>
        {
            syncProviderCalled = true;
            return password;
        }, (_,_) =>
        {
            asyncProviderCalled = true;
            return new(password);
        });

        using var dataSource = dataSourceBuilder.Build();
        using var conn = async ? await dataSource.OpenConnectionAsync() : dataSource.OpenConnection();
        Assert.True(async ? asyncProviderCalled : syncProviderCalled, "Password_provider not used");
    }

    [Test]
    public void Password_provider_exception()
    {
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        dataSourceBuilder.UsePasswordProvider(_ => throw new Exception(), (_,_) => throw new Exception());

        using var dataSource = dataSourceBuilder.Build();
        Assert.ThrowsAsync<GaussDBException>(async () => await dataSource.OpenConnectionAsync());
    }

    [Test]
    public async Task Periodic_password_provider()
    {
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        var password = new GaussDBConnectionStringBuilder(TestUtil.ConnectionString).Password!;

        var mre = new ManualResetEvent(false);
        dataSourceBuilder.UsePeriodicPasswordProvider((_, _) =>
        {
            mre.Set();
            return new(password);
        }, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10));

        await using (var dataSource = dataSourceBuilder.Build())
        {
            await using var connection1 = await dataSource.OpenConnectionAsync();
            await using var connection2 = dataSource.OpenConnection();

            mre.Reset();
            if (!mre.WaitOne(TimeSpan.FromSeconds(30)))
                Assert.Fail("Periodic password refresh did not occur");
        }

        mre.Reset();
        if (mre.WaitOne(TimeSpan.FromSeconds(1)))
            Assert.Fail("Periodic password refresh occurred after disposal of the data source");
    }

    [Test]
    public async Task Periodic_password_provider_with_first_time_exception()
    {
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        dataSourceBuilder.UsePeriodicPasswordProvider(
            (_, _) => throw new Exception("FOO"), TimeSpan.FromDays(30), TimeSpan.FromSeconds(10));
        await using var dataSource = dataSourceBuilder.Build();

        Assert.That(() => dataSource.OpenConnectionAsync(), Throws.Exception.TypeOf<GaussDBException>()
            .With.InnerException.With.Message.EqualTo("FOO"));
        Assert.That(() => dataSource.OpenConnection(), Throws.Exception.TypeOf<GaussDBException>()
            .With.InnerException.With.Message.EqualTo("FOO"));
    }

    [Test]
    public async Task Periodic_password_provider_with_second_time_exception()
    {
        var dataSourceBuilder = GetPasswordlessDataSourceBuilder();
        var password = new GaussDBConnectionStringBuilder(TestUtil.ConnectionString).Password!;

        var times = 0;
        var mre = new ManualResetEvent(false);

        dataSourceBuilder.UsePeriodicPasswordProvider(
            (_, _) =>
            {
                if (times++ > 1)
                {
                    mre.Set();
                    throw new Exception("FOO");
                }

                return new(password);
            },
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10));
        await using var dataSource = dataSourceBuilder.Build();

        mre.WaitOne();

        // The periodic timer threw, but previously returned a password. Make sure we keep using that last known one.
        using (await dataSource.OpenConnectionAsync()) {}
        using (dataSource.OpenConnection()) {}
    }

    [Test]
    public void Both_password_and_password_provider_is_not_supported()
    {
        var dataSourceBuilder = new GaussDBDataSourceBuilder(TestUtil.ConnectionString);
        dataSourceBuilder.UsePeriodicPasswordProvider((_, _) => new("foo"), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
        Assert.That(() => dataSourceBuilder.Build(), Throws.Exception.TypeOf<NotSupportedException>()
            .With.Message.EqualTo(GaussDBStrings.CannotSetBothPasswordProviderAndPassword));
    }

    [Test]
    public void Multiple_password_providers_is_not_supported()
    {
        var dataSourceBuilder = new GaussDBDataSourceBuilder(TestUtil.ConnectionString);
        dataSourceBuilder
            .UsePeriodicPasswordProvider((_, _) => new("foo"), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10))
            .UsePasswordProvider(_ => "foo", (_,_) => new("foo"));
        Assert.That(() => dataSourceBuilder.Build(), Throws.Exception.TypeOf<NotSupportedException>()
            .With.Message.EqualTo(GaussDBStrings.CannotSetMultiplePasswordProviderKinds));
    }

    #region pgpass

    [Test]
    [NonParallelizable] // Sets environment variable
    public async Task Use_pgpass_from_connection_string()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);
        var builder = new GaussDBConnectionStringBuilder(ConnectionString);
        var passFile = Path.GetTempFileName();
        File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{builder.Password}");

        try
        {
            await using var dataSource = CreateDataSource(csb =>
            {
                csb.Passfile = null;
                csb.Passfile = passFile;
            });
            await using var conn = await dataSource.OpenConnectionAsync();
        }
        finally
        {
            File.Delete(passFile);
        }
    }

    [Test]
    [NonParallelizable] // Sets environment variable
    public async Task Use_pgpass_from_environment_variable()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);
        var builder = new GaussDBConnectionStringBuilder(ConnectionString);
        var passFile = Path.GetTempFileName();
        File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{builder.Password}");
        using var passFileVariable = SetEnvironmentVariable("PGPASSFILE", passFile);

        try
        {
            await using var dataSource = CreateDataSource(csb => csb.Password = null);
            await using var conn = await dataSource.OpenConnectionAsync();
        }
        finally
        {
            File.Delete(passFile);
        }
    }

    [Test]
    [NonParallelizable] // Sets environment variable
    public async Task Use_pgpass_from_homedir()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);

        string? dirToDelete = null;
        string passFile;
        string? previousPassFile = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var dir = Path.Combine(Environment.GetEnvironmentVariable("APPDATA")!, "postgresql");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                dirToDelete = dir;
            }
            passFile = Path.Combine(dir, "pgpass.conf");
        }
        else
        {
            passFile = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, ".pgpass");
        }

        if (File.Exists(passFile))
        {
            previousPassFile = Path.GetTempFileName();
            File.Move(passFile, previousPassFile);
        }

        try
        {
            var builder = new GaussDBConnectionStringBuilder(ConnectionString);
            File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{builder.Password}");
            await using var dataSource = CreateDataSource(csb => csb.Passfile = null);
            await using var conn = await dataSource.OpenConnectionAsync();
        }
        finally
        {
            File.Delete(passFile);
            if (dirToDelete is not null)
                Directory.Delete(dirToDelete);
            if (previousPassFile is not null)
                File.Move(previousPassFile, passFile);
        }
    }

    #endregion pgpass

    [Test]
    [NonParallelizable] // Sets environment variable
    public void Password_source_precedence()
    {
        using var resetPassword = SetEnvironmentVariable("PGPASSWORD", null);

        var builder = new GaussDBConnectionStringBuilder(ConnectionString);
        var password = builder.Password;
        var passwordBad = password + "_bad";

        var passFile = Path.GetTempFileName();
        var passFileBad = passFile + "_bad";

        using var deletePassFile = Defer(() => File.Delete(passFile));
        using var deletePassFileBad = Defer(() => File.Delete(passFileBad));

        File.WriteAllText(passFile, $"*:*:*:{builder.Username}:{password}");
        File.WriteAllText(passFileBad, $"*:*:*:{builder.Username}:{passwordBad}");

        using (SetEnvironmentVariable("PGPASSFILE", passFileBad))
        {
            // Password from the connection string goes first
            using (SetEnvironmentVariable("PGPASSWORD", passwordBad))
            {
                using var dataSource1 = CreateDataSource(csb =>
                {
                    csb.Password = password;
                    csb.Passfile = passFileBad;
                });

                Assert.That(() => dataSource1.OpenConnection(), Throws.Nothing);
            }

            // Password from the environment variable goes second
            using (SetEnvironmentVariable("PGPASSWORD", password))
            {
                using var dataSource2 = CreateDataSource(csb =>
                {
                    csb.Password = null;
                    csb.Passfile = passFileBad;
                });

                Assert.That(() => dataSource2.OpenConnection(), Throws.Nothing);
            }

            // Passfile from the connection string goes third
            using var dataSource3 = CreateDataSource(csb =>
            {
                csb.Password = null;
                csb.Passfile = passFile;
            });

            Assert.That(() => dataSource3.OpenConnection(), Throws.Nothing);
        }

        // Passfile from the environment variable goes fourth
        using (SetEnvironmentVariable("PGPASSFILE", passFile))
        {
            using var dataSource4 = CreateDataSource(csb =>
            {
                csb.Password = null;
                csb.Passfile = null;
            });

            Assert.That(() => dataSource4.OpenConnection(), Throws.Nothing);
        }

        static DeferDisposable Defer(Action action) => new(action);
    }

    readonly struct DeferDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    [Test, Description("Connects with a bad password to ensure the proper error is thrown")]
    public void Authentication_failure()
    {
        using var dataSource = CreateDataSource(csb => csb.Password = "bad");
        using var conn = dataSource.CreateConnection();

        Assert.That(() => conn.OpenAsync(), Throws.Exception
            .TypeOf<PostgresException>()
            .With.Property(nameof(PostgresException.SqlState)).StartsWith("28")
        );
        Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Closed));
    }

    [Test, Description("Simulates a timeout during the authentication phase")]
    [IssueLink("https://github.com/npgsql/npgsql/issues/3227")]
    public async Task Timeout_during_authentication()
    {
        var builder = new GaussDBConnectionStringBuilder(ConnectionString) { Timeout = 1 };
        await using var postmasterMock = new PgPostmasterMock(builder.ConnectionString);
        _ = postmasterMock.AcceptServer();

        // The server will accept a connection from the client, but will not respond to the client's authentication
        // request. This should trigger a timeout
        await using var dataSource = CreateDataSource(postmasterMock.ConnectionString);
        await using var connection = dataSource.CreateConnection();
        Assert.That(async () => await connection.OpenAsync(),
            Throws.Exception.TypeOf<GaussDBException>()
                .With.InnerException.TypeOf<TimeoutException>());
    }

    [Test, IssueLink("https://github.com/npgsql/npgsql/issues/1180")]
    public void Pool_by_password()
    {
        using var _ = CreateTempPool(ConnectionString, out var connectionString);
        using (var goodConn = new GaussDBConnection(connectionString))
            goodConn.Open();

        var badConnectionString = new GaussDBConnectionStringBuilder(connectionString)
        {
            Password = "badpasswd"
        }.ConnectionString;
        using (var conn = new GaussDBConnection(badConnectionString))
            Assert.That(conn.Open, Throws.Exception.TypeOf<PostgresException>());
    }

    [Test, Explicit("Requires user specific local setup")]
    public async Task AuthenticateIntegratedSecurity()
    {
        await using var dataSource = GaussDBDataSource.Create(new GaussDBConnectionStringBuilder(ConnectionString)
        {
            Username = null,
            Password = null
        });
        await using var c = await dataSource.OpenConnectionAsync();
        Assert.That(c.State, Is.EqualTo(ConnectionState.Open));
    }

    #region ProvidePasswordCallback Tests

#pragma warning disable CS0618 // ProvidePasswordCallback is Obsolete

    [Test, Description("ProvidePasswordCallback is used when password is not supplied in connection string")]
    public async Task ProvidePasswordCallback_is_used()
    {
        using var _ = CreateTempPool(ConnectionString, out var connString);
        var builder = new GaussDBConnectionStringBuilder(connString);
        var goodPassword = builder.Password;
        var getPasswordDelegateWasCalled = false;
        builder.Password = null;

        Assume.That(goodPassword, Is.Not.Null);

        using (var conn = new GaussDBConnection(builder.ConnectionString) { ProvidePasswordCallback = ProvidePasswordCallback })
        {
            conn.Open();
            Assert.True(getPasswordDelegateWasCalled, "ProvidePasswordCallback delegate not used");

            // Do this again, since with multiplexing the very first connection attempt is done via
            // the non-multiplexing path, to surface any exceptions.
            GaussDBConnection.ClearPool(conn);
            conn.Close();
            getPasswordDelegateWasCalled = false;
            conn.Open();
            Assert.That(await conn.ExecuteScalarAsync("SELECT 1"), Is.EqualTo(1));
            Assert.True(getPasswordDelegateWasCalled, "ProvidePasswordCallback delegate not used");
        }

        string ProvidePasswordCallback(string host, int port, string database, string username)
        {
            getPasswordDelegateWasCalled = true;
            return goodPassword!;
        }
    }

    [Test, Description("ProvidePasswordCallback is not used when password is supplied in connection string")]
    public void ProvidePasswordCallback_is_not_used()
    {
        using var _ = CreateTempPool(ConnectionString, out var connString);

        using (var conn = new GaussDBConnection(connString) { ProvidePasswordCallback = ProvidePasswordCallback })
        {
            conn.Open();

            // Do this again, since with multiplexing the very first connection attempt is done via
            // the non-multiplexing path, to surface any exceptions.
            GaussDBConnection.ClearPool(conn);
            conn.Close();
            conn.Open();
        }

        string ProvidePasswordCallback(string host, int port, string database, string username)
        {
            throw new Exception("password should come from connection string, not delegate");
        }
    }

    [Test, Description("Exceptions thrown from client application are wrapped when using ProvidePasswordCallback Delegate")]
    public void ProvidePasswordCallback_exceptions_are_wrapped()
    {
        using var _ = CreateTempPool(ConnectionString, out var connString);
        var builder = new GaussDBConnectionStringBuilder(connString)
        {
            Password = null
        };

        using (var conn = new GaussDBConnection(builder.ConnectionString) { ProvidePasswordCallback = ProvidePasswordCallback })
        {
            Assert.That(() => conn.Open(), Throws.Exception
                .TypeOf<GaussDBException>()
                .With.InnerException.Message.EqualTo("inner exception from ProvidePasswordCallback")
            );
        }

        string ProvidePasswordCallback(string host, int port, string database, string username)
        {
            throw new Exception("inner exception from ProvidePasswordCallback");
        }
    }

    [Test, Description("Parameters passed to ProvidePasswordCallback delegate are correct")]
    public void ProvidePasswordCallback_gets_correct_arguments()
    {
        using var _ = CreateTempPool(ConnectionString, out var connString);
        var builder = new GaussDBConnectionStringBuilder(connString);
        var goodPassword = builder.Password;
        builder.Password = null;

        Assume.That(goodPassword, Is.Not.Null);

        string? receivedHost = null;
        int? receivedPort = null;
        string? receivedDatabase = null;
        string? receivedUsername = null;

        using (var conn = new GaussDBConnection(builder.ConnectionString) { ProvidePasswordCallback = ProvidePasswordCallback })
        {
            conn.Open();
            Assert.AreEqual(builder.Host, receivedHost);
            Assert.AreEqual(builder.Port, receivedPort);
            Assert.AreEqual(builder.Database, receivedDatabase);
            Assert.AreEqual(builder.Username, receivedUsername);
        }

        string ProvidePasswordCallback(string host, int port, string database, string username)
        {
            receivedHost = host;
            receivedPort = port;
            receivedDatabase = database;
            receivedUsername = username;

            return goodPassword!;
        }
    }

#pragma warning restore CS0618 // ProvidePasswordCallback is Obsolete

    #endregion

    GaussDBDataSourceBuilder GetPasswordlessDataSourceBuilder()
        => new(TestUtil.ConnectionString)
        {
            ConnectionStringBuilder =
            {
                Password = null
            }
        };
}
