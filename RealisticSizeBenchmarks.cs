using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// <see cref="EncodingBenchmarks"/> measures 10,000 values per message, which is
/// large for a typical RPC batch. This runs the same serialize and parse
/// comparison at sizes closer to what services actually configure, spanning the
/// region either side of the point where the difference starts to matter.
///
/// int32 and sint32 are omitted; signed encodings are a separate topic.
///
/// Mean is per MESSAGE here, not per value, because OperationsPerInvoke must be
/// a compile-time constant and the length is a parameter. The Ratio column is
/// unaffected by that.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class RealisticSizeBenchmarks
{
    [Params(16, 64, 256, 1024)]
    public int N { get; set; }

    private FixedBatch _fixed = null!;
    private VarintBatch _random = null!, _small = null!, _medium = null!;
    private byte[] _fixedDest = null!, _randomDest = null!, _smallDest = null!, _mediumDest = null!;
    private byte[] _fixedSrc = null!, _randomSrc = null!, _smallSrc = null!, _mediumSrc = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        var random = new uint[N];
        var small = new uint[N];
        var medium = new uint[N];
        for (int i = 0; i < N; i++)
        {
            random[i] = (uint)rng.NextInt64(0, uint.MaxValue);
            small[i] = (uint)rng.Next(0, 128);
            medium[i] = (uint)rng.Next(0, 16_384);
        }

        _fixed = new FixedBatch(); _fixed.Values.AddRange(random);
        _random = new VarintBatch(); _random.Values.AddRange(random);
        _small = new VarintBatch(); _small.Values.AddRange(small);
        _medium = new VarintBatch(); _medium.Values.AddRange(medium);

        _fixedDest = new byte[_fixed.CalculateSize()];
        _randomDest = new byte[_random.CalculateSize()];
        _smallDest = new byte[_small.CalculateSize()];
        _mediumDest = new byte[_medium.CalculateSize()];

        _fixedSrc = _fixed.ToByteArray();
        _randomSrc = _random.ToByteArray();
        _smallSrc = _small.ToByteArray();
        _mediumSrc = _medium.ToByteArray();
    }

    private static void Write(IMessage m, byte[] dest)
    {
        var w = new CodedOutputStream(dest);
        m.WriteTo(w);
        w.Flush();
    }

    [BenchmarkCategory("Serialize")]
    [Benchmark(Baseline = true, Description = "fixed32")]
    public void Ser_Fixed() => Write(_fixed, _fixedDest);

    [BenchmarkCategory("Serialize")]
    [Benchmark(Description = "uint32 random")]
    public void Ser_Random() => Write(_random, _randomDest);

    [BenchmarkCategory("Serialize")]
    [Benchmark(Description = "uint32 small (<128)")]
    public void Ser_Small() => Write(_small, _smallDest);

    [BenchmarkCategory("Serialize")]
    [Benchmark(Description = "uint32 medium (<16k)")]
    public void Ser_Medium() => Write(_medium, _mediumDest);

    [BenchmarkCategory("Parse")]
    [Benchmark(Baseline = true, Description = "fixed32")]
    public int Parse_Fixed() => FixedBatch.Parser.ParseFrom(_fixedSrc).Values.Count;

    [BenchmarkCategory("Parse")]
    [Benchmark(Description = "uint32 random")]
    public int Parse_Random() => VarintBatch.Parser.ParseFrom(_randomSrc).Values.Count;

    [BenchmarkCategory("Parse")]
    [Benchmark(Description = "uint32 small (<128)")]
    public int Parse_Small() => VarintBatch.Parser.ParseFrom(_smallSrc).Values.Count;

    [BenchmarkCategory("Parse")]
    [Benchmark(Description = "uint32 medium (<16k)")]
    public int Parse_Medium() => VarintBatch.Parser.ParseFrom(_mediumSrc).Values.Count;
}
