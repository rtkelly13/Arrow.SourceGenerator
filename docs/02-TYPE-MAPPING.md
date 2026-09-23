# 02 — Type mapping

The single mapping table is `Planning/ArrowMappingTable.cs`. Schema, write, read and view
emission all consume the plan it produces, so they cannot disagree.

## P0 scalar built-ins

| C# member type | Arrow type | Notes |
| --- | --- | --- |
| `bool` | `Boolean` | Bit-packed. |
| `sbyte` / `byte` | `Int8` / `UInt8` | |
| `short` / `ushort` | `Int16` / `UInt16` | |
| `int` / `uint` | `Int32` / `UInt32` | |
| `long` / `ulong` | `Int64` / `UInt64` | |
| `float` / `double` | `Float` / `Double` | |
| `string` | `Utf8` | int32 offsets. |
| `byte[]` | `Binary` | int32 offsets. |
| `decimal` | `Decimal128(38, 18)` | Override with `[ArrowDecimal(p, s)]`: 1 ≤ p ≤ 38, 0 ≤ s ≤ min(p, 28). Values that do not fit throw; nothing rounds. |
| `DateOnly` | `Date32` | Days since 1970-01-01. |
| `TimeOnly` | `Time64(Microsecond)` | Sub-microsecond ticks truncate. |
| `DateTime` | `Timestamp(Microsecond)`, no timezone | **Wall-clock** semantics: the value is stored as written; `Kind` is not stored and reads produce `Unspecified`. Sub-microsecond ticks truncate. |
| `DateTimeOffset` | `Timestamp(Microsecond, "UTC")` | **Instant** semantics: stored as UTC; the offset is not stored and reads produce offset zero. |
| `TimeSpan` | `Duration(Microsecond)` | Sub-microsecond ticks truncate. |
| `Guid` | `FixedSizeBinary(16)` | RFC 4122 byte order (big-endian), the order `uuid.bytes` and the `arrow.uuid` extension use. The extension metadata itself is not written yet. |
| `enum` | its underlying integer type | No validation of defined values on read. |

## Nullability

| Member | Arrow field |
| --- | --- |
| value type `T` | non-nullable |
| `T?` (`Nullable<T>`) | nullable |
| reference type annotated non-null (`string`) | non-nullable — writing `null` throws |
| reference type annotated nullable (`string?`) | nullable |
| reference type in a nullable-oblivious context | nullable (nothing promises non-null) |

## Unsupported

Any other member type is a compile-time error, **ARROW001**, never a silently missing field or
API. `char`, collections, nested classes and arbitrary types fall here until structural types
(Foundation 8) or a type adapter (Foundation 6) cover them. Mark such a member `[ArrowIgnore]` to
exclude it.
