using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// Parse-side counterpart to <see cref="EmitBenchmarks"/>, using protobuf code
/// paths on both sides of every comparison.
///
/// AddEntriesFrom has three routes:
///   packed, fixed-size  -> EnsureSize(count + length / FixedSize), then a bulk
///                          ReadPackedFieldLittleEndian
///   packed, variable    -> while (!limit) Add(reader(ctx))
///   unpacked, any type  -> do { Add(reader(ctx)) } while (MaybeConsumeTag)
///
/// So unpacked fixed32 against unpacked uint32 isolates decode cost: both go
/// through Add() per element with doubling growth, both consume a tag per
/// element, and they differ only in how each value is read.
///
/// The parse-reused category clears an already-grown RepeatedField and merges
/// into it, so the backing array never reallocates. Comparing that against a
/// fresh ParseFrom separates growth churn from decoding.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ParseDecompositionBenchmarks
{
    public const int N = 10_000;

    private byte[] _packedFixed = null!, _unpackedFixed = null!;
    private byte[] _packedVarint = null!, _unpackedVarint = null!;
    private FixedBatch _reusableFixed = null!;
    private VarintBatch _reusableVarint = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        var values = new uint[N];
        for (int i = 0; i < N; i++) values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        var pf = new FixedBatch(); pf.Values.AddRange(values);
        var pv = new VarintBatch(); pv.Values.AddRange(values);
        var uf = new UnpackedFixedBatch(); uf.Values.AddRange(values);
        var uv = new UnpackedVarintBatch(); uv.Values.AddRange(values);

        _packedFixed = pf.ToByteArray();
        _packedVarint = pv.ToByteArray();
        _unpackedFixed = uf.ToByteArray();
        _unpackedVarint = uv.ToByteArray();

        // Parse once so the reusable messages' backing arrays are already grown.
        _reusableFixed = FixedBatch.Parser.ParseFrom(_packedFixed);
        _reusableVarint = VarintBatch.Parser.ParseFrom(_packedVarint);
    }

    // -------------------------- fresh parse (allocates) --------------------------

    [BenchmarkCategory("parse-fresh")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "packed fixed32 (presize + bulk read)")]
    public int PackedFixed() => FixedBatch.Parser.ParseFrom(_packedFixed).Values.Count;

    [BenchmarkCategory("parse-fresh")]
    [Benchmark(OperationsPerInvoke = N, Description = "unpacked fixed32 (Add per element)")]
    public int UnpackedFixed() => UnpackedFixedBatch.Parser.ParseFrom(_unpackedFixed).Values.Count;

    [BenchmarkCategory("parse-fresh")]
    [Benchmark(OperationsPerInvoke = N, Description = "unpacked uint32 (Add per element)")]
    public int UnpackedVarint() => UnpackedVarintBatch.Parser.ParseFrom(_unpackedVarint).Values.Count;

    [BenchmarkCategory("parse-fresh")]
    [Benchmark(OperationsPerInvoke = N, Description = "packed uint32 (Add per element, no tags)")]
    public int PackedVarint() => VarintBatch.Parser.ParseFrom(_packedVarint).Values.Count;

    // ------------------ merge into a pre-grown array (no reallocation) ------------------

    [BenchmarkCategory("parse-reused")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "packed fixed32, pre-grown target")]
    public int ReusedFixed()
    {
        _reusableFixed.Values.Clear();
        _reusableFixed.MergeFrom(_packedFixed);
        return _reusableFixed.Values.Count;
    }

    [BenchmarkCategory("parse-reused")]
    [Benchmark(OperationsPerInvoke = N, Description = "packed uint32, pre-grown target")]
    public int ReusedVarint()
    {
        _reusableVarint.Values.Clear();
        _reusableVarint.MergeFrom(_packedVarint);
        return _reusableVarint.Values.Count;
    }
}
