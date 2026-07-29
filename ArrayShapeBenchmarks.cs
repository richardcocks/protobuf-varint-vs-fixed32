using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// protobuf has no fixed-length array type. The usual workaround suggested for
/// "I know the length and want it faster" is an opaque `bytes` field that you
/// pack yourself. Does that actually beat `repeated fixed32`?
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ArrayShapeBenchmarks
{
    public const int N = 10_000;

    private FixedBatch _fixedMsg = null!;
    private BytesBatch _bytesMsg = null!;
    private byte[] _fixedDest = null!, _bytesDest = null!;
    private byte[] _fixedSource = null!, _bytesSource = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        var values = new uint[N];
        for (int i = 0; i < N; i++) values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        // The packed byte representation -- in a real app you would generate
        // straight into this, exactly as the gRPC server did with NextBytes.
        var packed = new byte[N * 4];
        MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(packed);

        _fixedMsg = new FixedBatch();
        _fixedMsg.Values.AddRange(values);

        // UnsafeWrap avoids the defensive copy that ByteString.CopyFrom would make.
        _bytesMsg = new BytesBatch { Values = UnsafeByteOperations.UnsafeWrap(packed) };

        _fixedDest = new byte[_fixedMsg.CalculateSize()];
        _bytesDest = new byte[_bytesMsg.CalculateSize()];
        _fixedSource = _fixedMsg.ToByteArray();
        _bytesSource = _bytesMsg.ToByteArray();

        Console.WriteLine($"[setup] fixed32 encoded = {_fixedSource.Length} bytes, " +
                          $"bytes encoded = {_bytesSource.Length} bytes");
    }

    [BenchmarkCategory("Serialize")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "repeated fixed32")]
    public void Serialize_Fixed32()
    {
        var w = new CodedOutputStream(_fixedDest);
        _fixedMsg.WriteTo(w);
        w.Flush();
    }

    [BenchmarkCategory("Serialize")]
    [Benchmark(OperationsPerInvoke = N, Description = "bytes (hand-packed)")]
    public void Serialize_Bytes()
    {
        var w = new CodedOutputStream(_bytesDest);
        _bytesMsg.WriteTo(w);
        w.Flush();
    }

    [BenchmarkCategory("Parse")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "repeated fixed32")]
    public int Parse_Fixed32()
    {
        var msg = FixedBatch.Parser.ParseFrom(_fixedSource);
        return msg.Values.Count;
    }

    [BenchmarkCategory("Parse")]
    [Benchmark(OperationsPerInvoke = N, Description = "bytes + MemoryMarshal.Cast")]
    public int Parse_Bytes()
    {
        var msg = BytesBatch.Parser.ParseFrom(_bytesSource);
        var view = MemoryMarshal.Cast<byte, uint>(msg.Values.Span);
        return view.Length;
    }
}
