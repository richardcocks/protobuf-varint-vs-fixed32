using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// <see cref="ArrayShapeBenchmarks"/> compares `bytes` against `repeated fixed32`
/// for serialize and parse alone. That excludes the cost of getting data INTO
/// the message, which is where the two actually differ, and which depends on
/// where the data comes from:
///
///   uint[]  -- the application already holds typed values
///   byte[]  -- data arrives as bytes (Random.NextBytes, a file, a socket)
///
/// The populate category measures the conversion step on its own; end-to-end
/// measures populate plus serialize, which is the comparison that decides it.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ProvenanceBenchmarks
{
    public const int N = 10_000;

    private uint[] _values = null!;      // origin: typed
    private byte[] _packed = null!;      // origin: bytes (also reused as scratch)
    private FixedBatch _reusableFixed = null!;
    private byte[] _fixedDest = null!, _bytesDest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        _values = new uint[N];
        for (int i = 0; i < N; i++) _values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        _packed = new byte[N * 4];
        MemoryMarshal.AsBytes(_values.AsSpan()).CopyTo(_packed);

        _reusableFixed = new FixedBatch();
        _reusableFixed.Values.AddRange(_values);
        var bytesMsg = new BytesBatch { Values = UnsafeByteOperations.UnsafeWrap(_packed) };

        _fixedDest = new byte[_reusableFixed.CalculateSize()];
        _bytesDest = new byte[bytesMsg.CalculateSize()];
    }

    private static void Write(IMessage m, byte[] dest)
    {
        var w = new CodedOutputStream(dest);
        m.WriteTo(w);
        w.Flush();
    }

    // ---------------------------- populate step alone ----------------------------

    [BenchmarkCategory("populate")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "RepeatedField.AddRange (fresh msg)")]
    public FixedBatch Pop_AddRange()
    {
        var m = new FixedBatch();
        m.Values.AddRange(_values);
        return m;
    }

    [BenchmarkCategory("populate")]
    [Benchmark(OperationsPerInvoke = N, Description = "RepeatedField indexer overwrite (reused msg)")]
    public void Pop_Indexer()
    {
        var f = _reusableFixed.Values;
        var v = _values;
        for (int i = 0; i < v.Length; i++) f[i] = v[i];
    }

    [BenchmarkCategory("populate")]
    [Benchmark(OperationsPerInvoke = N, Description = "uint[] -> byte[] memcpy")]
    public void Pop_MemcpyToBytes() =>
        MemoryMarshal.AsBytes(_values.AsSpan()).CopyTo(_packed);

    [BenchmarkCategory("populate")]
    [Benchmark(OperationsPerInvoke = N, Description = "UnsafeWrap existing byte[] (no copy)")]
    public BytesBatch Pop_UnsafeWrap() =>
        new BytesBatch { Values = UnsafeByteOperations.UnsafeWrap(_packed) };

    // -------------------- end to end: data -> serialized bytes --------------------

    [BenchmarkCategory("end-to-end")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "from uint[]: fixed32, fresh msg")]
    public void E2E_Fixed_Fresh()
    {
        var m = new FixedBatch();
        m.Values.AddRange(_values);
        Write(m, _fixedDest);
    }

    [BenchmarkCategory("end-to-end")]
    [Benchmark(OperationsPerInvoke = N, Description = "from uint[]: fixed32, reused msg")]
    public void E2E_Fixed_Reuse()
    {
        var f = _reusableFixed.Values;
        var v = _values;
        for (int i = 0; i < v.Length; i++) f[i] = v[i];
        Write(_reusableFixed, _fixedDest);
    }

    [BenchmarkCategory("end-to-end")]
    [Benchmark(OperationsPerInvoke = N, Description = "from uint[]: bytes, memcpy + wrap")]
    public void E2E_Bytes_FromUints()
    {
        MemoryMarshal.AsBytes(_values.AsSpan()).CopyTo(_packed);
        var m = new BytesBatch { Values = UnsafeByteOperations.UnsafeWrap(_packed) };
        Write(m, _bytesDest);
    }

    [BenchmarkCategory("end-to-end")]
    [Benchmark(OperationsPerInvoke = N, Description = "from byte[]: bytes, wrap only")]
    public void E2E_Bytes_FromBytes()
    {
        var m = new BytesBatch { Values = UnsafeByteOperations.UnsafeWrap(_packed) };
        Write(m, _bytesDest);
    }

    [BenchmarkCategory("end-to-end")]
    [Benchmark(OperationsPerInvoke = N, Description = "from byte[]: fixed32, must unpack first")]
    public void E2E_Fixed_FromBytes()
    {
        var src = MemoryMarshal.Cast<byte, uint>(_packed.AsSpan());
        var f = _reusableFixed.Values;
        for (int i = 0; i < src.Length; i++) f[i] = src[i];
        Write(_reusableFixed, _fixedDest);
    }
}
