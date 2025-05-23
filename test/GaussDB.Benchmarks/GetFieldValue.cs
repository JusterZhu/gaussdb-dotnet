using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;

namespace HuaweiCloud.GaussDB.Benchmarks;

[Config(typeof(Config))]
public class GetFieldValue
{
    readonly GaussDBConnection _conn;
    readonly GaussDBCommand _cmd;
    readonly GaussDBDataReader _reader;

    public GetFieldValue()
    {
        _conn = BenchmarkEnvironment.OpenConnection();
        _cmd = new GaussDBCommand("SELECT 0, 'str'", _conn);
        _reader = _cmd.ExecuteReader();
        _reader.Read();
    }

    [Benchmark]
    public void NullableField() => _reader.GetFieldValue<int?>(0);

    [Benchmark]
    public void ValueTypeField() => _reader.GetFieldValue<int>(0);

    [Benchmark]
    public void ReferenceTypeField() => _reader.GetFieldValue<string>(1);

    [Benchmark]
    public void ObjectField() => _reader.GetFieldValue<object>(1);

    class Config : ManualConfig
    {
        public Config() => AddColumn(StatisticColumn.OperationsPerSecond);
    }
}
