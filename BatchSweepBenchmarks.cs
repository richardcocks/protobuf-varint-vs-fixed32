using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// Sweeps array length to locate where the encoding choice starts to matter for
/// serialization. Times are per whole message, so the Ratio column shows how the
/// advantage grows as the per-message fixed overhead amortizes away.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class BatchSweepBenchmarks
{
    [Params(1, 4, 16, 64, 256, 1024, 8192, 65536)]
    public int N { get; set; }

    private FixedBatch _fixed = null!;
    private VarintBatch _varint = null!;
    private SignedBatch _signed = null!;
    private byte[] _fixedDest = null!, _varintDest = null!, _signedDest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        var values = new uint[N];
        var signed = new int[N];
        for (int i = 0; i < N; i++)
        {
            values[i] = (uint)rng.NextInt64(0, uint.MaxValue);
            signed[i] = unchecked((int)values[i]);
        }

        _fixed = new FixedBatch();
        _fixed.Values.AddRange(values);
        _varint = new VarintBatch();
        _varint.Values.AddRange(values);
        _signed = new SignedBatch();
        _signed.Values.AddRange(signed);

        _fixedDest = new byte[_fixed.CalculateSize()];
        _varintDest = new byte[_varint.CalculateSize()];
        _signedDest = new byte[_signed.CalculateSize()];
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

    [Benchmark(Description = "int32")]
    public void Int32Signed() => Write(_signed, _signedDest);
}
