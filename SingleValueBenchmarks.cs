using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using RandomNumberGrpc;

namespace ProtoBench;

/// <summary>
/// Does the encoding choice matter for a SINGLE value per message, as in the
/// original post's one-number-at-a-time RPC? None of the packed-repeated
/// machinery (bulk copy, length pre-pass, array pre-sizing) applies here, so
/// only the raw varint encode cost of one value is left.
///
/// Times are per whole message, not per value.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SingleValueBenchmarks
{
    private SingleFixed _fixed = null!;
    private SingleVarint _varint = null!;
    private SingleSigned _signed = null!;
    private byte[] _fixedDest = null!, _varintDest = null!, _signedDest = null!;
    private byte[] _fixedSrc = null!, _varintSrc = null!, _signedSrc = null!;

    [GlobalSetup]
    public void Setup()
    {
        // A large random value, so the varint needs its full 5 bytes -- the same
        // worst case the batched benchmark exercised.
        const uint value = 3_141_592_653;
        const long seq = 12_345;

        _fixed = new SingleFixed { SequenceNumber = seq, Value = value };
        _varint = new SingleVarint { SequenceNumber = seq, Value = value };
        _signed = new SingleSigned { SequenceNumber = seq, Value = unchecked((int)value) };

        _fixedDest = new byte[_fixed.CalculateSize()];
        _varintDest = new byte[_varint.CalculateSize()];
        _signedDest = new byte[_signed.CalculateSize()];
        _fixedSrc = _fixed.ToByteArray();
        _varintSrc = _varint.ToByteArray();
        _signedSrc = _signed.ToByteArray();

        Console.WriteLine($"[setup] message sizes -- fixed32 {_fixedSrc.Length} B, " +
                          $"uint32 {_varintSrc.Length} B, int32 {_signedSrc.Length} B");
    }

    private static void Write(IMessage m, byte[] dest)
    {
        var w = new CodedOutputStream(dest);
        m.WriteTo(w);
        w.Flush();
    }

    [BenchmarkCategory("Serialize")]
    [Benchmark(Baseline = true, Description = "fixed32")]
    public void Serialize_Fixed() => Write(_fixed, _fixedDest);

    [BenchmarkCategory("Serialize")]
    [Benchmark(Description = "uint32 varint")]
    public void Serialize_Varint() => Write(_varint, _varintDest);

    [BenchmarkCategory("Serialize")]
    [Benchmark(Description = "int32 (negative)")]
    public void Serialize_Signed() => Write(_signed, _signedDest);

    [BenchmarkCategory("Parse")]
    [Benchmark(Baseline = true, Description = "fixed32")]
    public IMessage Parse_Fixed() => SingleFixed.Parser.ParseFrom(_fixedSrc);

    [BenchmarkCategory("Parse")]
    [Benchmark(Description = "uint32 varint")]
    public IMessage Parse_Varint() => SingleVarint.Parser.ParseFrom(_varintSrc);

    [BenchmarkCategory("Parse")]
    [Benchmark(Description = "int32 (negative)")]
    public IMessage Parse_Signed() => SingleSigned.Parser.ParseFrom(_signedSrc);
}
