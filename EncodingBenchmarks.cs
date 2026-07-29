using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// Compares protobuf wire encodings for 32-bit values, isolated from any transport.
///
/// The encoding is the benchmark *method* rather than a [Params] value, because
/// BenchmarkDotNet baselines apply to methods and jobs -- there is no way to mark
/// one parameter value as the baseline. Serialize and Parse are separate
/// categories, each with its own fixed32 baseline, so the Ratio column compares
/// like with like.
///
/// OperationsPerInvoke = ValuesPerMessage means Mean reads directly as
/// nanoseconds *per value*, not per message.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EncodingBenchmarks
{
    public const int ValuesPerMessage = 10_000;

    private sealed class Case
    {
        public required IMessage Message { get; init; }
        public required MessageParser Parser { get; init; }
        public required byte[] Destination { get; init; }  // written into by Serialize
        public required byte[] Source { get; init; }       // pre-serialized, read by Parse
    }

    private Case _fixed32 = null!;
    private Case _uint32Random = null!;
    private Case _uint32Small = null!;
    private Case _uint32Medium = null!;
    private Case _int32Random = null!;
    private Case _sint32Random = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Fixed seed so every scenario sees the same underlying bits where relevant.
        var rng = new Random(12345);
        var random = new uint[ValuesPerMessage];
        var small = new uint[ValuesPerMessage];
        var medium = new uint[ValuesPerMessage];
        var signed = new int[ValuesPerMessage];

        for (int i = 0; i < ValuesPerMessage; i++)
        {
            random[i] = (uint)rng.NextInt64(0, uint.MaxValue);
            small[i] = (uint)rng.Next(0, 128);          // 1-byte varints
            medium[i] = (uint)rng.Next(0, 16_384);      // 2-byte varints
            signed[i] = unchecked((int)random[i]);      // negative ~half the time
        }

        _fixed32 = Build(Fixed(random), FixedBatch.Parser);
        _uint32Random = Build(Varint(random), VarintBatch.Parser);
        _uint32Small = Build(Varint(small), VarintBatch.Parser);
        _uint32Medium = Build(Varint(medium), VarintBatch.Parser);
        _int32Random = Build(Signed(signed), SignedBatch.Parser);
        _sint32Random = Build(ZigZag(signed), ZigZagBatch.Parser);

        static IMessage Fixed(uint[] v) { var m = new FixedBatch(); m.Values.AddRange(v); return m; }
        static IMessage Varint(uint[] v) { var m = new VarintBatch(); m.Values.AddRange(v); return m; }
        static IMessage Signed(int[] v) { var m = new SignedBatch(); m.Values.AddRange(v); return m; }
        static IMessage ZigZag(int[] v) { var m = new ZigZagBatch(); m.Values.AddRange(v); return m; }

        static Case Build(IMessage message, MessageParser parser)
        {
            int size = message.CalculateSize();
            var source = new byte[size];
            var writer = new CodedOutputStream(source);
            message.WriteTo(writer);
            writer.Flush();
            return new Case
            {
                Message = message,
                Parser = parser,
                Destination = new byte[size],
                Source = source,
            };
        }
    }

    private static void Serialize(Case c)
    {
        var writer = new CodedOutputStream(c.Destination);
        c.Message.WriteTo(writer);
        writer.Flush();
    }

    // ----------------------------- Serialize -----------------------------

    [BenchmarkCategory("Serialize")]
    [Benchmark(Baseline = true, OperationsPerInvoke = ValuesPerMessage, Description = "fixed32")]
    public void Serialize_Fixed32() => Serialize(_fixed32);

    [BenchmarkCategory("Serialize")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "uint32 random")]
    public void Serialize_UInt32Random() => Serialize(_uint32Random);

    [BenchmarkCategory("Serialize")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "uint32 small (<128)")]
    public void Serialize_UInt32Small() => Serialize(_uint32Small);

    [BenchmarkCategory("Serialize")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "uint32 medium (<16k)")]
    public void Serialize_UInt32Medium() => Serialize(_uint32Medium);

    [BenchmarkCategory("Serialize")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "int32 random")]
    public void Serialize_Int32Random() => Serialize(_int32Random);

    [BenchmarkCategory("Serialize")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "sint32 zigzag random")]
    public void Serialize_SInt32Random() => Serialize(_sint32Random);

    // ------------------------------- Parse -------------------------------

    [BenchmarkCategory("Parse")]
    [Benchmark(Baseline = true, OperationsPerInvoke = ValuesPerMessage, Description = "fixed32")]
    public IMessage Parse_Fixed32() => _fixed32.Parser.ParseFrom(_fixed32.Source);

    [BenchmarkCategory("Parse")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "uint32 random")]
    public IMessage Parse_UInt32Random() => _uint32Random.Parser.ParseFrom(_uint32Random.Source);

    [BenchmarkCategory("Parse")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "uint32 small (<128)")]
    public IMessage Parse_UInt32Small() => _uint32Small.Parser.ParseFrom(_uint32Small.Source);

    [BenchmarkCategory("Parse")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "uint32 medium (<16k)")]
    public IMessage Parse_UInt32Medium() => _uint32Medium.Parser.ParseFrom(_uint32Medium.Source);

    [BenchmarkCategory("Parse")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "int32 random")]
    public IMessage Parse_Int32Random() => _int32Random.Parser.ParseFrom(_int32Random.Source);

    [BenchmarkCategory("Parse")]
    [Benchmark(OperationsPerInvoke = ValuesPerMessage, Description = "sint32 zigzag random")]
    public IMessage Parse_SInt32Random() => _sint32Random.Parser.ParseFrom(_sint32Random.Source);
}
