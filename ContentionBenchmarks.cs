using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// fixed32 serialisation is a bulk copy, so it is memory-bandwidth hungry;
/// varint is compute-bound and touches memory slowly. Single-threaded that
/// favours fixed32 enormously. This measures what happens when several cores do
/// it at once and contend for one memory subsystem.
///
/// Each thread gets its own message and destination buffer, so there are no
/// shared buffers and no false sharing. N = 1,000,000 gives each thread a
/// working set of roughly 8 MB (4 MB source plus 4 MB destination), so at 8
/// threads the aggregate exceeds a typical L3 and the traffic must reach DRAM.
///
/// Because each invocation processes Threads x N values, compare aggregate
/// throughput rather than the Mean column directly: divide (Threads * N) by the
/// mean to see how each encoding scales.
///
/// Allocates roughly 136 MB at 8 threads.
/// </summary>
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ContentionBenchmarks
{
    public const int N = 1_000_000;

    [Params(1, 2, 4, 8)]
    public int Threads { get; set; }

    private FixedBatch[] _fixed = null!;
    private VarintBatch[] _varint = null!;
    private byte[][] _fixedDest = null!;
    private byte[][] _varintDest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(12345);
        var values = new uint[N];
        for (int i = 0; i < N; i++) values[i] = (uint)rng.NextInt64(0, uint.MaxValue);

        _fixed = new FixedBatch[Threads];
        _varint = new VarintBatch[Threads];
        _fixedDest = new byte[Threads][];
        _varintDest = new byte[Threads][];

        for (int t = 0; t < Threads; t++)
        {
            _fixed[t] = new FixedBatch(); _fixed[t].Values.AddRange(values);
            _varint[t] = new VarintBatch(); _varint[t].Values.AddRange(values);
            _fixedDest[t] = new byte[_fixed[t].CalculateSize()];
            _varintDest[t] = new byte[_varint[t].CalculateSize()];
        }

        double perThreadMb = (_fixedDest[0].Length + N * 4.0) / (1024 * 1024);
        Console.WriteLine($"[setup] threads={Threads} fixed32 working set {perThreadMb:N1} MB/thread, " +
                          $"{perThreadMb * Threads:N1} MB total");
    }

    private static void Write(IMessage m, byte[] dest)
    {
        var w = new CodedOutputStream(dest);
        m.WriteTo(w);
        w.Flush();
    }

    [Benchmark(Baseline = true, Description = "fixed32 (memcpy-bound)")]
    public void Fixed32() =>
        Parallel.For(0, Threads, t => Write(_fixed[t], _fixedDest[t]));

    [Benchmark(Description = "uint32 varint (compute-bound)")]
    public void Varint() =>
        Parallel.For(0, Threads, t => Write(_varint[t], _varintDest[t]));
}
