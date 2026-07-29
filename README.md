# protobuf: `fixed32` vs `uint32` for random 32-bit values

BenchmarkDotNet micro-benchmarks isolating protobuf serialization cost from any
transport, prompted by a surprising result while comparing gRPC streaming
throughput against WCF.

## TL;DR

Streaming uniformly-random 32-bit integers over gRPC was **~5.7x faster** using
`repeated fixed32` than `repeated uint32`. The obvious explanation — varint makes
random 32-bit values bigger on the wire — is real but nowhere near big enough to
account for it:

| | bytes/value |
|---|---|
| `fixed32` | 4.00 |
| `uint32` (varint), random | 4.94 |

That is only **23% more bytes**. The actual cause is CPU: varint encoding is
**~200x slower to serialize** than `fixed32`. The end-to-end gap was smaller only
because in the `fixed32` case serialization isn't the bottleneck at all — the
network stack is. Switching to varint moves the bottleneck off the network and
onto the CPU.

## Running it

```bash
dotnet run -c Release -- --filter *
```

Release is required; BenchmarkDotNet refuses to run a debug build. The full set
is about 20 minutes. Wire sizes are printed first by the host process (they are
deterministic and need no statistical treatment), then the selected timings.

To run one suite:

```bash
dotnet run -c Release -- --filter *EncodingBenchmarks*
```

Omitting `--filter` entirely gives an interactive menu.

## Reproducing each table

| Table | Suite | Filter | Approx. |
|---|---|---|---|
| Wire size | *(host process)* | any run | instant |
| Serialize / Parse | `EncodingBenchmarks` | `*EncodingBenchmarks*` | 2.5 min |
| Singular (non-repeated) fields | `SingleValueBenchmarks` | `*SingleValueBenchmarks*` | 1.5 min |
| Array length sweep | `BatchSweepBenchmarks` | `*BatchSweepBenchmarks*` | 6 min |
| Where the ~200x comes from | `MechanismBenchmarks` | `*MechanismBenchmarks*` | 1 min |
| Cache effects at large N | `BoundaryCheckBenchmarks` | `*BoundaryCheckBenchmarks*` | 5 min |
| `bytes` vs `repeated fixed32` | `ArrayShapeBenchmarks` | `*ArrayShapeBenchmarks*` | 1 min |

The end-to-end gRPC throughput figures quoted in the TL;DR come from a **separate**
client/server harness and are not reproducible from this repository.

## Wire size

Measured, and cross-checked against the analytic expectation. A uniformly random
`uint32` needs 5 varint bytes whenever it is >= 2^28, which is 15/16 of the time,
giving an expected 4.9370 bytes/value — matching the measured 4.94 exactly.

| scenario | bytes/value | notes |
|---|---|---|
| `fixed32` | 4.00 | always 4, by definition |
| `uint32` varint, random | 4.94 | 93.75% of values need the full 5 bytes |
| `uint32` varint, small (<128) | 1.00 | varint's best case |
| `uint32` varint, medium (<16384) | 1.99 | |
| `int32` varint, random | 7.40 | negatives sign-extend to 64 bits, 10 bytes each |
| `sint32` zigzag, random | 4.94 | recovers the `uint32` size |

## Timings

`OperationsPerInvoke` is set to the batch size where applicable, so `Mean` reads
directly as nanoseconds **per value**. `fixed32` is the baseline in each
category, so `Ratio` is the slowdown factor.

### Serialize

| Scenario | Mean (ns/value) | Error | Ratio | RatioSD |
|---|---:|---:|---:|---:|
| **`fixed32`** | **0.0763** | 0.0018 | **1.00** | 0.02 |
| `uint32` small (<128) | 5.7624 | 0.2614 | 75.56 | 2.43 |
| `uint32` medium (<16k) | 7.6027 | 0.2225 | 99.69 | 2.35 |
| `sint32` zigzag random | 14.6771 | 0.2054 | 192.45 | 3.20 |
| `uint32` random | 15.0770 | 0.4895 | **197.69** | 4.97 |
| `int32` random | 24.6258 | 0.2570 | **322.89** | 5.00 |

### Parse

| Scenario | Mean (ns/value) | Error | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|
| **`fixed32`** | **0.2057** | 0.0143 | **1.00** | 4 B | 1.00 |
| `uint32` small (<128) | 6.6161 | 0.1724 | 32.22 | 13 B | 3.25 |
| `uint32` medium (<16k) | 7.5080 | 0.1826 | 36.56 | 13 B | 3.25 |
| `uint32` random | 12.2316 | 0.2528 | **59.56** | 13 B | 3.25 |
| `sint32` zigzag random | 12.6286 | 0.7901 | 61.50 | 13 B | 3.25 |
| `int32` random | 20.4702 | 0.3753 | **99.68** | 13 B | 3.25 |

### Singular (non-`repeated`) fields — per message

None of the packed machinery applies here, so only the raw per-value encoding
cost is left. Note `fixed32` **wins** for a singular field — the opposite of a
`repeated` field holding one element (see the sweep below).

| Category | Scenario | Mean | Ratio | Message size |
|---|---|---:|---:|---:|
| Serialize | `fixed32` | 30.30 ns | 1.00 | 8 B |
| Serialize | `uint32` | 38.92 ns | 1.28 | 9 B |
| Serialize | `int32` (negative) | 51.22 ns | 1.69 | 14 B |
| Parse | `fixed32` | 58.32 ns | 1.00 | |
| Parse | `uint32` | 56.84 ns | 0.98 | |
| Parse | `int32` (negative) | 58.62 ns | 1.01 | |

These are the noisiest numbers here — absolute times are tens of nanoseconds and
dominated by the 208 B parser allocation, so ratios move by 10-20% between runs.
The stable claims are that `fixed32` wins on serialize and that `uint32` parse is
a dead heat. Re-run before quoting the exact figures.

### Array length sweep — serialization, per message

| N | `fixed32` | `uint32` | ratio | `int32` | ratio |
|---:|---:|---:|---:|---:|---:|
| 1 | 83.52 ns | 38.25 ns | 0.46 | 49.66 ns | 0.59 |
| 4 | 78.06 ns | 73.89 ns | 0.95 | 96.91 ns | 1.24 |
| 16 | 82.30 ns | 263.83 ns | 3.21 | 357.59 ns | 4.35 |
| 64 | 84.83 ns | 969.16 ns | 11.42 | 1,413.52 ns | 16.66 |
| 256 | 93.31 ns | 3,801.52 ns | 40.74 | 5,435.40 ns | 58.25 |
| 1,024 | 120.92 ns | 15,210.79 ns | 125.79 | 22,917.93 ns | 189.53 |
| 8,192 | 635.78 ns | 119,693.73 ns | 188.27 | 203,784.41 ns | 320.54 |
| 65,536 | 5,742.85 ns | 962,093.92 ns | 167.55 | 1,671,699.69 ns | 291.14 |

`fixed32` is a net **loss** below about 5 elements. Its column is flat at
78-93 ns from N=1 to N=256 — that is fixed overhead, not copy cost. Entering the
fast path allocates and frees a pinned `GCHandle` whether it is copying 4 bytes
or 256 KB, and small arrays never earn that back.

### Where the ~200x comes from

| Method | Mean (ns/value) | Ratio |
|---|---:|---:|
| raw span copy (memcpy floor) | 0.0727 | 1.00 |
| raw span copy, misaligned by 3 | 0.0730 | 1.01 |
| protobuf `fixed32` | 0.0778 | 1.07 |
| per-element 4-byte stores | 0.7184 | 9.89 |
| hand-written varint loop | 3.4723 | 47.81 |

| Step | Factor | Cost |
|---|---:|---|
| memcpy to per-element stores | 9.89x | losing the bulk copy |
| per-element stores to varint loop | 4.83x | branchy 1-5 byte emit |
| hand-written varint to protobuf's | 4.34x | delegate indirection + two-pass length pre-computation |
| **product** | **207x** | vs 197.69x measured |

Alignment is **not** the mechanism — the deliberately misaligned copy costs 1.01x.

### Cache effects at large N

| N | `fixed32` | `uint32` | ratio | `fixed32` ns/value | copy bandwidth | working set |
|---:|---:|---:|---:|---:|---:|---:|
| 16,384 | 1.257 µs | 251.1 µs | 200.13 | 0.0767 | 52.1 GB/s | 128 KB |
| 32,768 | 2.606 µs | 505.5 µs | 194.36 | 0.0795 | 50.3 GB/s | 256 KB |
| 65,535 | 5.756 µs | 1,003.7 µs | 174.57 | 0.0878 | 45.5 GB/s | 512 KB |
| 65,536 | 5.841 µs | 1,002.9 µs | 171.88 | 0.0891 | 44.9 GB/s | 512 KB |
| 65,537 | 5.882 µs | 1,004.6 µs | 171.09 | 0.0898 | 44.6 GB/s | 512 KB |
| 131,072 | 12.285 µs | 2,050.9 µs | 167.18 | 0.0937 | 42.7 GB/s | 1 MB |

65,535 / 65,536 / 65,537 are the control: they are indistinguishable, so the
decline is a smooth cache-capacity effect rather than a power-of-two artifact.
The working set is source + destination, 8N bytes; the test machine has 512 KB
L2 per core, which N=65,536 exactly saturates. `fixed32` is a pure memory copy
and is directly exposed to this; `uint32` stays flat at ~15.3-15.65 ns/value
because it is compute-bound and hides the latency.

### `bytes` vs `repeated fixed32`

Both encode to 40,004 bytes for 10,000 values — packed `repeated fixed32` and a
`bytes` field are byte-identical on the wire.

| Category | Method | Mean (ns/value) | Ratio | Allocated |
|---|---|---:|---:|---:|
| Serialize | `repeated fixed32` | 0.0752 | 1.00 | - |
| Serialize | `bytes` (hand-packed) | 0.0730 | 0.97 | - |
| Parse | `repeated fixed32` | 0.2196 | 1.00 | 4 B |
| Parse | `bytes` + `MemoryMarshal.Cast` | 0.2310 | 1.06 | 4 B |

Note this is a **singular** `bytes` field, not `repeated bytes`. `bytes` is not a
packable scalar, so `repeated bytes` gives every element its own tag and length
prefix and no bulk copy at all.

This comparison excludes the cost of getting data *into* the message. Populating
a `RepeatedField<uint>` element-by-element runs about 0.7 ns/value — roughly ten
times the serialization itself — which `UnsafeByteOperations.UnsafeWrap` skips
entirely. So `bytes` can win end-to-end even though serialize and parse tie.

## Why

**`fixed32` is essentially free.** 0.0763 ns/value is about 13 billion
values/sec, roughly 52 GB/s. `RepeatedField<T>.WriteTo` pins the `uint[]` with a
`GCHandle`, reinterprets it as `Span<byte>`, and issues one bulk `WriteRawBytes`:

```csharp
if (TryGetArrayAsSpanPinnedUnsafe(codec, out Span<byte> span, out GCHandle handle))
{
    span = span.Slice(0, Count * codec.FixedSize);
    WritingPrimitives.WriteRawBytes(ref ctx.buffer, ref ctx.state, span);
    handle.Free();
}
else
{
    for (int i = 0; i < count; i++) writer(ref ctx, array[i]);
}
```

The gate is `codec.FixedSize > 0` plus little-endianness — which is why the
generated code matters: `FieldCodec.ForFixed32` sets `FixedSize = 4`,
`ForUInt32` leaves it 0.

The deeper reason this is possible is **representation identity**. Protobuf's
`fixed32` is *defined* as 4 bytes little-endian, byte-for-byte how .NET lays out
a `uint` in a `uint[]` on x86-64. Serializing is a no-op transformation. On a
big-endian machine the path is disabled.

**Allocation is a second, independent signal.** Parsing packed `fixed32`
allocates 4 B/value; every varint variant allocates 13 B/value — a ratio of
exactly **3.25 regardless of value magnitude**. That constancy is the giveaway:
it is structural. Fixed-width lets the parser derive the element count from the
byte length and size the array once; packed varint cannot know the count without
scanning, so the backing list grows by reallocation and copying.

**`int32` is the trap.** Protobuf sign-extends negative `int32` to 64 bits before
varint-encoding, so every negative value costs the full 10 bytes. Half of random
`int32`s are negative: 0.5x10 + 0.5x4.87 = 7.43, matching the measured 7.40.
`sint32` (zigzag) fixes it, landing statistically indistinguishable from `uint32`
(192.45 vs 197.69, RatioSD around 5).

## Guidance

Match the encoding to the data's distribution, not to its C# type:

- **Uniform over the full range** (random numbers, hashes, IDs, checksums,
  ticks) -> `fixed32` / `fixed64`
- **Clustered near zero** (counts, indices, enum values, sequence numbers) ->
  `uint32` / `int32` varint
- **Negative and clustered near zero** -> `sint32` / `sint64`; never plain
  `int32`

And by array length, for uniformly random values:

- **Under ~8 per message:** don't bother; `uint32` is marginally faster
- **8-64:** `fixed32` pulls ahead, but the gap is below typical per-message
  transport overhead
- **64+:** use `fixed32`; the difference is now a majority of the per-message
  budget
- **1,000+:** this is a design error rather than a missed optimisation

## Caveat

Varint is a **bandwidth** optimization paid for in CPU. Even varint's best case
(values < 128, 1 byte each) still costs 5.76 ns/value to serialize — 75x
`fixed32` — while saving 4x the bytes. These measurements say nothing about a
bandwidth-constrained link, where trading CPU for 4x fewer bytes can be clearly
correct. The conclusion is not "varint is slow", it is "varint is the wrong tool
for uniformly random values", where it costs *both* more bytes and far more CPU.

## Environment

- BenchmarkDotNet v0.15.8
- Google.Protobuf 3.35.1
- .NET 10.0.10, X64 RyuJIT x86-64-v3, `net10.0`
- Windows 10 22H2 (19045), AMD Ryzen 7 3800X, 8 physical / 16 logical cores
- `IterationCount=10  WarmupCount=3`
