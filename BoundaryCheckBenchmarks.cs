using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// The fixed32/uint32 ratio dipped at N=65536 (188x at 8192 -> 168x at 65536).
/// Two competing explanations:
///
///   (a) a boundary artifact at the power of two, or
///   (b) fixed32 going memory-bandwidth-bound while varint stays compute-bound.
///
/// 65535 / 65536 / 65537 discriminates between them: if all three behave alike,
/// it is not a boundary. 16384 vs 32768 additionally straddles the Large Object
/// Heap threshold (85,000 bytes), which uint[] crosses around N=21,250.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class BoundaryCheckBenchmarks
{
    [Params(16_384, 32_768, 65_535, 65_536, 65_537, 131_072)]
    public int N { get; set; }

    private FixedBatch _fixed = null!;
    private VarintBatch _varint = null!;
    private byte[] _fixedDest = null!, _varintDest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        var values = new uint[N];
        for (int i = 0; i < N; i++) values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        _fixed = new FixedBatch();
        _fixed.Values.AddRange(values);
        _varint = new VarintBatch();
        _varint.Values.AddRange(values);

        _fixedDest = new byte[_fixed.CalculateSize()];
        _varintDest = new byte[_varint.CalculateSize()];

        Console.WriteLine($"[setup] N={N}  fixed32 payload={_fixedDest.Length} B  varint payload={_varintDest.Length} B");
    }

    private static void Write(IMessage m, byte[] dest)
    {
        var w = new CodedOutputStream(dest);
        m.WriteTo(w);
        w.Flush();
    }

    [Benchmark(Baseline = true, Description = "fixed32")]
    public void Fixed32() => Write(_fixed, _fixedDest);

    [Benchmark(Description = "uint32 varint")]
    public void UInt32Varint() => Write(_varint, _varintDest);
}
