using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// Is the parse-side allocation ratio a constant, or a function of array length?
///
/// fixed32 pre-sizes to exactly N (for N >= 8), so it allocates 4N bytes at any
/// length. Packed varint grows 8 -> 16 -> ... -> C, where C is the next power of
/// two, allocating 4 * (2C - 8) bytes across the whole series. The ratio is
/// therefore (2C - 8) / N, which depends on where N falls inside its bracket:
///
///   N exactly a power of two  ->  ~2.0  (least waste)
///   N just above one          ->  ~4.0  (most waste)
///
/// GlobalSetup prints the closed-form prediction for each length so it can be
/// checked against the measured Alloc Ratio column.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class AllocationByLengthBenchmarks
{
    [Params(8_192, 10_000, 16_384, 16_385)]
    public int N { get; set; }

    private byte[] _packedFixed = null!;
    private byte[] _packedVarint = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        var values = new uint[N];
        for (int i = 0; i < N; i++) values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        var pf = new FixedBatch(); pf.Values.AddRange(values);
        var pv = new VarintBatch(); pv.Values.AddRange(values);
        _packedFixed = pf.ToByteArray();
        _packedVarint = pv.ToByteArray();

        long c = 8;
        while (c < N) c *= 2;
        Console.WriteLine($"[setup] N={N} finalCapacity={c} predictedAllocRatio={(2.0 * c - 8) / N:N3}");
    }

    [Benchmark(Baseline = true, Description = "packed fixed32 parse")]
    public int Fixed() => FixedBatch.Parser.ParseFrom(_packedFixed).Values.Count;

    [Benchmark(Description = "packed uint32 parse")]
    public int Varint() => VarintBatch.Parser.ParseFrom(_packedVarint).Values.Count;
}
