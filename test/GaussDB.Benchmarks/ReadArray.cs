using BenchmarkDotNet.Attributes;

namespace HuaweiCloud.GaussDB.Benchmarks;

public class ReadArrays
{
    [Params(true, false)]
    public bool AllNulls;

    [Params(1, 10, 1000, 100000)]
    public int NumElements;

    GaussDBConnection _intConn = default!;
    GaussDBCommand _intCmd = default!;
    GaussDBDataReader _intReader = default!;

    GaussDBConnection _nullableIntConn = default!;
    GaussDBCommand _nullableIntCmd = default!;
    GaussDBDataReader _nullableIntReader = default!;

    GaussDBConnection _stringConn = default!;
    GaussDBCommand _stringCmd = default!;
    GaussDBDataReader _stringReader = default!;

    [GlobalSetup]
    public void Setup()
    {
        var intArray = new int[NumElements];
        for (var i = 0; i < NumElements; i++)
            intArray[i] = 666;
        _intConn = BenchmarkEnvironment.OpenConnection();
        _intCmd = new GaussDBCommand("SELECT @p1", _intConn);
        _intCmd.Parameters.AddWithValue("p1", intArray);
        _intReader = _intCmd.ExecuteReader();
        _intReader.Read();

        var nullableIntArray = new int?[NumElements];
        for (var i = 0; i < NumElements; i++)
            nullableIntArray[i] = AllNulls ? (int?)null : 666;
        _nullableIntConn = BenchmarkEnvironment.OpenConnection();
        _nullableIntCmd = new GaussDBCommand("SELECT @p1", _nullableIntConn);
        _nullableIntCmd.Parameters.AddWithValue("p1", nullableIntArray);
        _nullableIntReader = _nullableIntCmd.ExecuteReader();
        _nullableIntReader.Read();

        var stringArray = new string?[NumElements];
        for (var i = 0; i < NumElements; i++)
            stringArray[i] = AllNulls ? null : "666";
        _stringConn = BenchmarkEnvironment.OpenConnection();
        _stringCmd = new GaussDBCommand("SELECT @p1", _stringConn);
        _stringCmd.Parameters.AddWithValue("p1", stringArray);
        _stringReader = _stringCmd.ExecuteReader();
        _stringReader.Read();
    }

    protected void Cleanup()
    {
        _intReader.Dispose();
        _nullableIntReader.Dispose();
        _stringReader.Dispose();

        _intCmd.Dispose();
        _nullableIntCmd.Dispose();
        _stringCmd.Dispose();

        _intConn.Dispose();
        _nullableIntConn.Dispose();
        _stringConn.Dispose();
    }

    [Benchmark]
    public int ReadIntArray()
        => _intReader.GetFieldValue<int[]>(0).Length;

    [Benchmark]
    public int ReadNullableIntArray()
        => _nullableIntReader.GetFieldValue<int?[]>(0).Length;

    [Benchmark]
    public int ReadStringArray()
        => _stringReader.GetFieldValue<string[]>(0).Length;
}
