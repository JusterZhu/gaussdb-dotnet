using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HuaweiCloud.GaussDB.Tests;
using HuaweiCloud.GaussDB.Tests.Support;
using NUnit.Framework;

namespace HuaweiCloud.GaussDB.DependencyInjection.Tests;

[TestFixture(DataSourceMode.Standard)]
[TestFixture(DataSourceMode.Slim)]
public class DependencyInjectionTests(DataSourceMode mode)
{
    [Test]
    public async Task GaussDBDataSource_is_registered_properly([Values] bool async)
    {
        var serviceCollection = new ServiceCollection();
        RegisterDataSource(serviceCollection, TestUtil.ConnectionString);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var dataSource = serviceProvider.GetRequiredService<GaussDBDataSource>();

        await using var connection = async
            ? await dataSource.OpenConnectionAsync()
            : dataSource.OpenConnection();
    }

    [Test]
    public async Task GaussDBMultiHostDataSource_is_registered_properly([Values] bool async)
    {
        var serviceCollection = new ServiceCollection();
        RegisterMultiHostDataSource(serviceCollection, TestUtil.ConnectionString);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var multiHostDataSource = serviceProvider.GetRequiredService<GaussDBMultiHostDataSource>();
        var dataSource = serviceProvider.GetRequiredService<GaussDBDataSource>();

        Assert.That(dataSource, Is.SameAs(multiHostDataSource));

        await using var connection = async
            ? await dataSource.OpenConnectionAsync()
            : dataSource.OpenConnection();
    }

    [Test]
    public async Task GaussDBDataSource_with_service_key_is_registered_properly([Values] bool async)
    {
        const string serviceKey = "key";
        var serviceCollection = new ServiceCollection();
        RegisterDataSource(serviceCollection, TestUtil.ConnectionString, serviceKey);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var dataSource = serviceProvider.GetRequiredKeyedService<GaussDBDataSource>(serviceKey);
        Assert.Throws<InvalidOperationException>(() => serviceProvider.GetRequiredService<GaussDBDataSource>());

        await using var connection = async
            ? await dataSource.OpenConnectionAsync()
            : dataSource.OpenConnection();
    }

    [Test]
    public async Task GaussDBMultiHostDataSource_with_service_key_is_registered_properly([Values] bool async)
    {
        const string serviceKey = "key";
        var serviceCollection = new ServiceCollection();
        RegisterMultiHostDataSource(serviceCollection, TestUtil.ConnectionString, serviceKey);

        await using var serviceProvider = serviceCollection.BuildServiceProvider();
        var multiHostDataSource = serviceProvider.GetRequiredKeyedService<GaussDBMultiHostDataSource>(serviceKey);
        var dataSource = serviceProvider.GetRequiredKeyedService<GaussDBDataSource>(serviceKey);
        Assert.Throws<InvalidOperationException>(() => serviceProvider.GetRequiredService<GaussDBMultiHostDataSource>());
        Assert.Throws<InvalidOperationException>(() => serviceProvider.GetRequiredService<GaussDBDataSource>());

        Assert.That(dataSource, Is.SameAs(multiHostDataSource));

        await using var connection = async
            ? await dataSource.OpenConnectionAsync()
            : dataSource.OpenConnection();
    }

    [Test]
    public void GaussDBDataSource_is_registered_as_singleton_by_default()
    {
        var serviceCollection = new ServiceCollection();
        RegisterDataSource(serviceCollection, TestUtil.ConnectionString);

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();
        var scopeServiceProvider1 = scope1.ServiceProvider;
        var scopeServiceProvider2 = scope2.ServiceProvider;

        var dataSource1 = scopeServiceProvider1.GetRequiredService<GaussDBDataSource>();
        var dataSource2 = scopeServiceProvider2.GetRequiredService<GaussDBDataSource>();

        Assert.That(dataSource2, Is.SameAs(dataSource1));
    }

    [Test]
    public async Task GaussDBConnection_is_registered_properly([Values] bool async)
    {
        var serviceCollection = new ServiceCollection();
        RegisterDataSource(serviceCollection, TestUtil.ConnectionString);

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServiceProvider = scope.ServiceProvider;

        var connection = scopedServiceProvider.GetRequiredService<GaussDBConnection>();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));

        if (async)
            await connection.OpenAsync();
        else
            connection.Open();
    }

    [Test]
    public void GaussDBConnection_is_registered_as_transient_by_default()
    {
        var serviceCollection = new ServiceCollection();
        RegisterDataSource(serviceCollection, "Host=localhost;Username=test;Password=test");

        using var serviceProvider = serviceCollection.BuildServiceProvider();
        using var scope1 = serviceProvider.CreateScope();
        var scopedServiceProvider1 = scope1.ServiceProvider;

        var connection1 = scopedServiceProvider1.GetRequiredService<GaussDBConnection>();
        var connection2 = scopedServiceProvider1.GetRequiredService<GaussDBConnection>();

        Assert.That(connection2, Is.Not.SameAs(connection1));

        using var scope2 = serviceProvider.CreateScope();
        var scopedServiceProvider2 = scope2.ServiceProvider;

        var connection3 = scopedServiceProvider2.GetRequiredService<GaussDBConnection>();
        Assert.That(connection3, Is.Not.SameAs(connection1));
    }

    [Test]
    public async Task LoggerFactory_is_picked_up_from_ServiceCollection()
    {
        var listLoggerProvider = new ListLoggerProvider();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(b => b.AddProvider(listLoggerProvider));
        RegisterDataSource(serviceCollection, TestUtil.ConnectionString);
        await using var serviceProvider = serviceCollection.BuildServiceProvider();

        var dataSource = serviceProvider.GetRequiredService<GaussDBDataSource>();
        await using var command = dataSource.CreateCommand("SELECT 1");

        using (listLoggerProvider.Record())
            _ = command.ExecuteNonQuery();

        Assert.That(listLoggerProvider.Log.Any(l => l.Id == GaussDBEventId.CommandExecutionCompleted));
    }

    IServiceCollection RegisterDataSource(ServiceCollection serviceCollection, string connectionString, object? serviceKey = null)
        => mode switch
        {
            DataSourceMode.Standard => serviceCollection.AddGaussDBDataSource(connectionString, serviceKey: serviceKey),
            DataSourceMode.Slim => serviceCollection.AddGaussDBSlimDataSource(connectionString, serviceKey: serviceKey),
            _ => throw new NotSupportedException($"Mode {mode} not supported")
        };

    IServiceCollection RegisterMultiHostDataSource(ServiceCollection serviceCollection, string connectionString, object? serviceKey = null)
        => mode switch
        {
            DataSourceMode.Standard => serviceCollection.AddMultiHostGaussDBDataSource(connectionString, serviceKey: serviceKey),
            DataSourceMode.Slim => serviceCollection.AddMultiHostGaussDBSlimDataSource(connectionString, serviceKey: serviceKey),
            _ => throw new NotSupportedException($"Mode {mode} not supported")
        };
}

public enum DataSourceMode
{
    Standard,
    Slim
}
