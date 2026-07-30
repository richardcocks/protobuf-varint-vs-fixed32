using BenchmarkDotNet.Running;
using Google.Protobuf;
using ProtoBench;
using RandomNumberGrpc;

// ---------------------------------------------------------------------------
// Wire sizes first. These are deterministic, so they need no statistical
// treatment -- just measure and print, then hand over to BenchmarkDotNet
// for the timings.
// ---------------------------------------------------------------------------

const int N = EncodingBenchmarks.ValuesPerMessage;
var rng = new Random(12345);

var random = new uint[N];
var small = new uint[N];
var medium = new uint[N];
var signed = new int[N];
for (int i = 0; i < N; i++)
{
    random[i] = (uint)rng.NextInt64(0, uint.MaxValue);
    small[i] = (uint)rng.Next(0, 128);
    medium[i] = (uint)rng.Next(0, 16_384);
    signed[i] = unchecked((int)random[i]);
}

IMessage Fixed(uint[] v) { var m = new FixedBatch(); m.Values.AddRange(v); return m; }
IMessage Varint(uint[] v) { var m = new VarintBatch(); m.Values.AddRange(v); return m; }
IMessage Signed(int[] v) { var m = new SignedBatch(); m.Values.AddRange(v); return m; }
IMessage ZigZag(int[] v) { var m = new ZigZagBatch(); m.Values.AddRange(v); return m; }

Console.WriteLine();
Console.WriteLine("Wire size (deterministic, not timed)");
Console.WriteLine("{0,-16} {1,12}", "scenario", "bytes/value");
foreach (var (label, msg) in new (string, IMessage)[]
         {
             ("fixed32", Fixed(random)),
             ("uint32-random", Varint(random)),
             ("uint32-small", Varint(small)),
             ("uint32-medium", Varint(medium)),
             ("int32-random", Signed(signed)),
             ("sint32-random", ZigZag(signed)),
         })
{
    Console.WriteLine("{0,-16} {1,12:N2}", label, msg.CalculateSize() / (double)N);
}

// Analytic expectation for uniformly random uint32, as a cross-check on the above.
double expected = 1 * (128.0 / 4294967296.0)
                + 2 * ((16384.0 - 128) / 4294967296.0)
                + 3 * ((2097152.0 - 16384) / 4294967296.0)
                + 4 * ((268435456.0 - 2097152) / 4294967296.0)
                + 5 * ((4294967296.0 - 268435456) / 4294967296.0);
Console.WriteLine();
Console.WriteLine($"analytic varint bytes/value for uniform random uint32 = {expected:N4} (fixed32 is exactly 4.0000)");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Timings. Each suite corresponds to one table in the README:
//
//   EncodingBenchmarks             Serialize + Parse
//   RealisticSizeBenchmarks        Same, at realistic batch sizes
//   SingleValueBenchmarks          Singular (non-repeated) fields
//   BatchSweepBenchmarks           Array length sweep
//   EmitBenchmarks                 Packed vs unpacked, write side
//   ParseDecompositionBenchmarks   Packed vs unpacked, parse side
//   AllocationByLengthBenchmarks   Allocation ratio vs array length
//   BoundaryCheckBenchmarks        Cache effects at large N
//   ArrayShapeBenchmarks           bytes vs repeated fixed32
//   ProvenanceBenchmarks           Cost of populating the message
//
// Run one:   dotnet run -c Release -- --filter *EncodingBenchmarks*
// Run all:   dotnet run -c Release -- --filter *
// Menu:      dotnet run -c Release
// ---------------------------------------------------------------------------

BenchmarkSwitcher.FromTypes(new[]
{
    typeof(EncodingBenchmarks),
    typeof(RealisticSizeBenchmarks),
    typeof(SingleValueBenchmarks),
    typeof(BatchSweepBenchmarks),
    typeof(EmitBenchmarks),
    typeof(ParseDecompositionBenchmarks),
    typeof(AllocationByLengthBenchmarks),
    typeof(BoundaryCheckBenchmarks),
    typeof(ArrayShapeBenchmarks),
    typeof(ProvenanceBenchmarks),
}).Run(args);
