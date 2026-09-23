# 05 — Compatibility and version policy

## Supported floors

| Axis | Floor | Pinned in tests | Notes |
| --- | --- | --- | --- |
| Roslyn (host compiler) | 4.8 (.NET 8 SDK, VS 17.8) | 4.14 | The generator compiles against the floor (`RoslynFloor` in its project), so it cannot call newer APIs. |
| Apache.Arrow | 23.0.0 | 23.0.0 | Generated code calls Apache.Arrow's public API directly — never through reflection. |
| Consumer target framework | net8.0 | net8.0, net10.0 | `DateOnly`/`TimeOnly` members need net6.0+; the floor is what CI executes. |
| Attributes package | netstandard2.0 | — | Attributes only; no runtime behaviour. |

## Interoperability evidence

| Check | Where | What it proves |
| --- | --- | --- |
| Apache.Arrow typed-array inspection | `test/Arrow.SourceGenerator.Tests/Runtime` | Exact stored encodings for every P0 kind. |
| Apache.Arrow IPC round trip | `IpcRoundTripTests`, AOT test | Offsets, validity bitmaps and schemas survive serialisation. |
| PyArrow reads generated output | CI, `scripts/pyarrow_interop.py verify` | An independent implementation agrees on schema (types, units, timezone, decimal parameters, nullability) and every value, and `Table.validate(full=True)` passes. |
| Generated reader accepts PyArrow output | CI, `scripts/pyarrow_interop.py produce` | PyArrow defaults (every field nullable, multiple batches) materialise into the expected rows. |

The interop rows (`test/Arrow.SourceGenerator.Interop/InteropRow.cs`, mirrored in the script)
include the boundaries that break naïve mappings: years 1 and 9999, pre-epoch dates, microsecond
precision, `ulong.MaxValue`, the largest `Decimal128(18, 4)`, empty strings and binaries, and GUIDs
in RFC 4122 byte order (compared against Python's `uuid.bytes`). pyarrow is pinned in the script's
PEP 723 header. Python is used for this external check only; every other script and tool is C#.

## Widening a floor

Before a floor moves, the package-consumption project is built against the minimum supported,
current pinned, and newest intended versions (`-p:ApacheArrowVersion=...`). A floor is a promise:
lowering one requires that evidence, raising one is a breaking change recorded in `CHANGELOG.md`.

## Pre-0.1

Breaking changes are acceptable before 0.1 when they shrink accidental API or make an unsafe
contract unrepresentable. They are still recorded in `CHANGELOG.md`.
