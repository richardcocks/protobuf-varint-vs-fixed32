using System.Buffers.Binary;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// Separates the cost of protobuf's bulk-copy fast path from the cost of varint
/// encoding itself, using protobuf code paths on both sides of every comparison.
///
/// `[packed = false]` routes a repeated field through protobuf's per-element
/// writer. Applying it to fixed32 as well as uint32 gives two paths that share
/// the same dispatch, tag writing and buffer bookkeeping, and differ only in how
/// each value is emitted.
///
/// The `protobuf-write` and `protobuf-size` categories are the measurement. The
/// `synthetic` category is hand-written code included only to bracket those
/// numbers; it is not protobuf and should not be used to attribute protobuf's
/// costs. The spread between the two per-element store implementations there
/// shows how much a hand-written baseline depends on how it happens to be
/// written.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EmitBenchmarks
{
    public const int N = 10_000;

    private uint[] _values = null!;
    private FixedBatch _packedFixed = null!;
    private VarintBatch _packedVarint = null!;
    private UnpackedFixedBatch _unpackedFixed = null!;
    private UnpackedVarintBatch _unpackedVarint = null!;
    private byte[] _pfDest = null!, _pvDest = null!, _ufDest = null!, _uvDest = null!;
    private byte[] _scratch = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        _values = new uint[N];
        for (int i = 0; i < N; i++) _values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        _packedFixed = new FixedBatch();
        _packedFixed.Values.AddRange(_values);
        _packedVarint = new VarintBatch();
        _packedVarint.Values.AddRange(_values);
        _unpackedFixed = new UnpackedFixedBatch();
        _unpackedFixed.Values.AddRange(_values);
        _unpackedVarint = new UnpackedVarintBatch();
        _unpackedVarint.Values.AddRange(_values);

        _pfDest = new byte[_packedFixed.CalculateSize()];
        _pvDest = new byte[_packedVarint.CalculateSize()];
        _ufDest = new byte[_unpackedFixed.CalculateSize()];
        _uvDest = new byte[_unpackedVarint.CalculateSize()];
        _scratch = new byte[N * 6 + 16];

        Console.WriteLine(
            $"[setup] encoded bytes/value -- packed fixed32 {_pfDest.Length / (double)N:N2}, " +
            $"unpacked fixed32 {_ufDest.Length / (double)N:N2}, " +
            $"packed uint32 {_pvDest.Length / (double)N:N2}, " +
            $"unpacked uint32 {_uvDest.Length / (double)N:N2}");
    }

    private static void Write(IMessage m, byte[] dest)
    {
        var w = new CodedOutputStream(dest);
        m.WriteTo(w);
        w.Flush();
    }

    // ---------------------------- protobuf writes ----------------------------

    [BenchmarkCategory("protobuf-write")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "packed fixed32 (bulk copy)")]
    public void PackedFixed_Write() => Write(_packedFixed, _pfDest);

    [BenchmarkCategory("protobuf-write")]
    [Benchmark(OperationsPerInvoke = N, Description = "unpacked fixed32 (per-element)")]
    public void UnpackedFixed_Write() => Write(_unpackedFixed, _ufDest);

    [BenchmarkCategory("protobuf-write")]
    [Benchmark(OperationsPerInvoke = N, Description = "unpacked uint32 (per-element)")]
    public void UnpackedVarint_Write() => Write(_unpackedVarint, _uvDest);

    [BenchmarkCategory("protobuf-write")]
    [Benchmark(OperationsPerInvoke = N, Description = "packed uint32")]
    public void PackedVarint_Write() => Write(_packedVarint, _pvDest);

    // ------------------- length pre-pass, measured directly -------------------

    [BenchmarkCategory("protobuf-size")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "packed fixed32 CalculateSize")]
    public int PackedFixed_Size() => _packedFixed.CalculateSize();

    [BenchmarkCategory("protobuf-size")]
    [Benchmark(OperationsPerInvoke = N, Description = "packed uint32 CalculateSize")]
    public int PackedVarint_Size() => _packedVarint.CalculateSize();

    [BenchmarkCategory("protobuf-size")]
    [Benchmark(OperationsPerInvoke = N, Description = "unpacked uint32 CalculateSize")]
    public int UnpackedVarint_Size() => _unpackedVarint.CalculateSize();

    // ------------- synthetic brackets: NOT protobuf, context only -------------

    [BenchmarkCategory("synthetic")]
    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "raw span copy (memcpy floor)")]
    public void Synth_RawCopy() =>
        MemoryMarshal.AsBytes(_values.AsSpan()).CopyTo(_scratch);

    [BenchmarkCategory("synthetic")]
    [Benchmark(OperationsPerInvoke = N, Description = "raw span copy, misaligned by 3")]
    public void Synth_RawCopyMisaligned() =>
        MemoryMarshal.AsBytes(_values.AsSpan()).CopyTo(_scratch.AsSpan(3));

    [BenchmarkCategory("synthetic")]
    [Benchmark(OperationsPerInvoke = N, Description = "per-element store via Span<uint>")]
    public void Synth_StoreViaSpanUint()
    {
        var d = MemoryMarshal.Cast<byte, uint>(_scratch.AsSpan());
        var v = _values;
        for (int i = 0; i < v.Length; i++) d[i] = v[i];
    }

    [BenchmarkCategory("synthetic")]
    [Benchmark(OperationsPerInvoke = N, Description = "per-element store via Slice")]
    public void Synth_StoreViaSlice()
    {
        var d = _scratch.AsSpan();
        var v = _values;
        for (int i = 0; i < v.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(d.Slice(i * 4), v[i]);
    }

    [BenchmarkCategory("synthetic")]
    [Benchmark(OperationsPerInvoke = N, Description = "hand-written varint loop")]
    public int Synth_HandVarint()
    {
        var d = _scratch.AsSpan();
        var v = _values;
        int pos = 0;
        for (int i = 0; i < v.Length; i++)
        {
            uint x = v[i];
            while (x >= 0x80) { d[pos++] = (byte)(x | 0x80); x >>= 7; }
            d[pos++] = (byte)x;
        }
        return pos;
    }
}
