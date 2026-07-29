# protobuf 32-bit encoding benchmarks

BenchmarkDotNet micro-benchmarks measuring the wire size, serialization cost and
parsing cost of protobuf's 32-bit scalar encodings: `fixed32`, `uint32`,
`int32`, `sint32`, and a hand-packed `bytes` field.

## Requirements

- .NET 10 SDK
- A Release build. BenchmarkDotNet refuses to run against a Debug build.

## Running

Run everything (roughly 20 minutes):

```bash
dotnet run -c Release -- --filter *
```

Run a single suite:

```bash
dotnet run -c Release -- --filter *EncodingBenchmarks*
```

Omit `--filter` for an interactive menu:

```bash
dotnet run -c Release
```

## Suites

| Suite | Measures | Approx. |
|---|---|---|
| `EncodingBenchmarks` | Serialize and parse cost per value for `repeated fixed32`, `repeated uint32` (random, <128 and <16384 value ranges), `repeated int32` and `repeated sint32`, over 10,000-element arrays. | 2.5 min |
| `SingleValueBenchmarks` | The same encodings as singular (non-`repeated`) fields, one value per message. | 1.5 min |
| `BatchSweepBenchmarks` | Serialization cost across array lengths from 1 to 65,536, for `fixed32`, `uint32` and `int32`. | 6 min |
| `MechanismBenchmarks` | protobuf `fixed32` serialization against a raw span copy, a deliberately misaligned span copy, per-element 4-byte stores, and a hand-written varint loop. | 1 min |
| `BoundaryCheckBenchmarks` | Array lengths from 16,384 to 131,072, including 65,535 / 65,536 / 65,537. | 5 min |
| `ArrayShapeBenchmarks` | `repeated fixed32` against a singular `bytes` field carrying the same packed payload. | 1 min |

In every suite `fixed32` is the BenchmarkDotNet baseline, so the `Ratio` column
is relative to it.

## Output

Each run first prints a wire-size table from the host process. Wire sizes are
deterministic, so they are measured with `CalculateSize()` rather than timed.
This is followed by the analytic expected varint size for a uniformly random
`uint32`, as a cross-check on the measured figure.

BenchmarkDotNet then prints its summary and writes full reports to
`BenchmarkDotNet.Artifacts/results/` in GitHub-flavoured markdown, CSV and HTML.
That directory is gitignored.

Where a suite operates on arrays, `OperationsPerInvoke` is set to the array
length, so the `Mean` column reads as nanoseconds **per value**. In
`SingleValueBenchmarks` it is left unset and `Mean` is per message.

`MemoryDiagnoser` is enabled throughout, so `Allocated` is bytes per operation
using the same per-value or per-message basis as `Mean`.

## Messages

`protos/random.proto` defines the message shapes. Every message has the same
structure — an `int64` sequence field and a values field — differing only in the
scalar type of the values, so that measured differences are attributable to the
encoding alone.

`BytesBatch` uses a singular `bytes` field, not `repeated bytes`. `bytes` is not
a packable scalar, so a repeated `bytes` field would give each element its own
tag and length prefix.

## Notes

Results are specific to the machine, runtime and Google.Protobuf version they
were produced on. Re-run locally rather than relying on figures recorded
elsewhere. The BenchmarkDotNet summary header records the environment for each
run.
