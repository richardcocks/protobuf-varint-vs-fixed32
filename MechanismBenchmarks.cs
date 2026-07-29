using System.Buffers.Binary;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// Discriminates between competing explanations for why protobuf `fixed32`
/// serializes ~200x faster than `uint32`:
///
///   A  raw span copy             -- the memcpy floor
///   A2 raw span copy, offset 3   -- same, deliberately misaligned
///   B  protobuf fixed32          -- is it at the floor, or above it?
///   C  per-element 4-byte stores -- what "no bulk copy" would cost
///   D  hand-written varint loop  -- what the varint path is actually doing
///
/// If B sits at A's level and far below C, the mechanism is a bulk copy.
/// If A2 matches A, alignment is not the explanation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MechanismBenchmarks
{
    public const int N = 10_000;

    private uint[] _values = null!;
    private byte[] _dest = null!;
    private byte[] _protoDest = null!;
    private FixedBatch _fixedMsg = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        _values = new uint[N];
        for (int i = 0; i < N; i++) _values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        _dest = new byte[N * 5 + 16];          // headroom for varint worst case + offset
        _fixedMsg = new FixedBatch();
        _fixedMsg.Values.AddRange(_values);
        _protoDest = new byte[_fixedMsg.CalculateSize()];
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = N, Description = "A raw span copy (memcpy floor)")]
    public void RawSpanCopy() =>
        MemoryMarshal.AsBytes(_values.AsSpan()).CopyTo(_dest);

    [Benchmark(OperationsPerInvoke = N, Description = "A2 raw span copy, misaligned by 3")]
    public void RawSpanCopyMisaligned() =>
        MemoryMarshal.AsBytes(_values.AsSpan()).CopyTo(_dest.AsSpan(3));

    [Benchmark(OperationsPerInvoke = N, Description = "B protobuf fixed32")]
    public void ProtobufFixed32()
    {
        var writer = new CodedOutputStream(_protoDest);
        _fixedMsg.WriteTo(writer);
        writer.Flush();
    }

    [Benchmark(OperationsPerInvoke = N, Description = "C per-element 4-byte stores")]
    public void PerElementStores()
    {
        var d = _dest.AsSpan();
        var v = _values;
        for (int i = 0; i < v.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(d.Slice(i * 4), v[i]);
    }

    [Benchmark(OperationsPerInvoke = N, Description = "D hand-written varint loop")]
    public int HandWrittenVarint()
    {
        var d = _dest.AsSpan();
        var v = _values;
        int pos = 0;
        for (int i = 0; i < v.Length; i++)
        {
            uint x = v[i];
            while (x >= 0x80)
            {
                d[pos++] = (byte)(x | 0x80);
                x >>= 7;
            }
            d[pos++] = (byte)x;
        }
        return pos;
    }
}
