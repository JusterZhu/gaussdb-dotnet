using System;
using NUnit.Framework;

namespace HuaweiCloud.GaussDB.Tests;

class ConnectionStringBuilderTests
{
    [Test]
    public void Basic()
    {
        var builder = new GaussDBConnectionStringBuilder();
        Assert.That(builder.Count, Is.EqualTo(0));
        Assert.That(builder.ContainsKey("server"), Is.True);
        builder.Host = "myhost";
        Assert.That(builder["host"], Is.EqualTo("myhost"));
        Assert.That(builder.Count, Is.EqualTo(1));
        Assert.That(builder.ConnectionString, Is.EqualTo("Host=myhost"));
        builder.Remove("HOST");
        Assert.That(builder["host"], Is.EqualTo(""));
        Assert.That(builder.Count, Is.EqualTo(0));
    }

    [Test]
    public void TryGetValue()
    {
        var builder = new GaussDBConnectionStringBuilder();
        builder.ConnectionString = "Host=myhost";
        Assert.That(builder.TryGetValue("Host", out var value), Is.True);
        Assert.That(value, Is.EqualTo("myhost"));
        Assert.That(builder.TryGetValue("SomethingUnknown", out value), Is.False);
    }

    [Test]
    public void Remove()
    {
        var builder = new GaussDBConnectionStringBuilder();
        builder.SslMode = SslMode.Require;
        Assert.That(builder["SSL Mode"], Is.EqualTo(SslMode.Require));
        builder.Remove("SSL Mode");
        Assert.That(builder.ConnectionString, Is.EqualTo(""));
        builder.CommandTimeout = 120;
        Assert.That(builder["Command Timeout"], Is.EqualTo(120));
        builder.Remove("Command Timeout");
        Assert.That(builder.ConnectionString, Is.EqualTo(""));
    }

    [Test]
    public void Clear()
    {
        var builder = new GaussDBConnectionStringBuilder { Host = "myhost" };
        builder.Clear();
        Assert.That(builder.Count, Is.EqualTo(0));
        Assert.That(builder["host"], Is.EqualTo(""));
        Assert.That(builder.Host, Is.Null);
    }

    [Test]
    public void Removing_resets_to_default()
    {
        var builder = new GaussDBConnectionStringBuilder();
        Assert.That(builder.Port, Is.EqualTo(GaussDBConnection.DefaultPort));
        builder.Port = 8;
        builder.Remove("Port");
        Assert.That(builder.Port, Is.EqualTo(GaussDBConnection.DefaultPort));
    }

    [Test]
    public void Setting_to_null_resets_to_default()
    {
        var builder = new GaussDBConnectionStringBuilder();
        Assert.That(builder.Port, Is.EqualTo(GaussDBConnection.DefaultPort));
        builder.Port = 8;
        builder["Port"] = null;
        Assert.That(builder.Port, Is.EqualTo(GaussDBConnection.DefaultPort));
    }

    [Test]
    public void Enum()
    {
        var builder = new GaussDBConnectionStringBuilder();
        builder.ConnectionString = "SslMode=Require";
        Assert.That(builder.SslMode, Is.EqualTo(SslMode.Require));
        Assert.That(builder.Count, Is.EqualTo(1));
    }

    [Test]
    public void Enum_insensitive()
    {
        var builder = new GaussDBConnectionStringBuilder();
        builder.ConnectionString = "SslMode=require";
        Assert.That(builder.SslMode, Is.EqualTo(SslMode.Require));
        Assert.That(builder.Count, Is.EqualTo(1));
    }

    [Test]
    public void Clone()
    {
        var builder = new GaussDBConnectionStringBuilder();
        builder.Host = "myhost";
        var builder2 = builder.Clone();
        Assert.That(builder2.Host, Is.EqualTo("myhost"));
        Assert.That(builder2["Host"], Is.EqualTo("myhost"));
        Assert.That(builder.Port, Is.EqualTo(GaussDBConnection.DefaultPort));
    }

    [Test]
    public void Conversion_error_throws()
    {
        var builder = new GaussDBConnectionStringBuilder();
        Assert.That(() => builder["Port"] = "hello",
            Throws.Exception.TypeOf<ArgumentException>().With.Message.Contains("Port"));
    }

    [Test]
    public void Invalid_connection_string_throws()
    {
        var builder = new GaussDBConnectionStringBuilder();
        Assert.That(() => builder.ConnectionString = "Server=127.0.0.1;User Id=gaussdb_tests;Pooling:false",
            Throws.Exception.TypeOf<ArgumentException>());
    }
}
