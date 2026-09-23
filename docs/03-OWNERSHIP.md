# 03 — Ownership and lifetime

Arrow buffers are ownership-sensitive. Before any buffer trick is emitted, this page must answer:
who owns the memory, how long it is valid, whether it can be mutated, whether anything can obtain a
writable alias, and when it is disposed.

**Founding invariant: read-only source memory is never laundered into writable memory.**

## Classification

| Category | Meaning |
| --- | --- |
| True zero-copy | Final Arrow arrays own, or safely reference, compatible source buffers with a compatible lifetime. |
| View-only zero-copy | Generated code exposes a read-only typed view over an existing Arrow buffer without a payload copy. |
| Structural copy | Offsets, validity or the array object are created; the payload is reused. |
| Conversion copy | Values are transformed into new Arrow-owned buffers. |

Tests and benchmarks name the category each path uses.

## Per operation

### `ToRecordBatch` / `ToRecordBatches` — conversion copy

Row-oriented models must be transposed into columns, so this is never zero-copy, and the generator
does not claim otherwise. What it guarantees:

- **The returned `RecordBatch` owns every buffer in it.** Nothing in it aliases model memory: string
  and binary payloads are copied into Arrow value buffers.
- **The caller owns the batch** and disposes it. `ToRecordBatches` yields independent batches; each
  can be disposed as soon as it is consumed.
- **No retention of the input.** The rows are not referenced by the batch. `ToRecordBatches` holds at
  most one chunk of row references and clears it after each batch.
- **Bounded temporary memory.** Columns are built one after another into buffers reserved to the
  exact row count, so the peak is the finished columns plus one column under construction — never
  every column's temporary at once (Parquet.SourceGenerator PR #326 showed the cost of the latter).

### `FromRecordBatch` / `FromRecordBatches` — conversion copy

- **The batch is only read.** It is never retained, mutated or disposed; the caller keeps ownership
  and may dispose it as soon as the call returns (for `FromRecordBatches`, once enumeration has
  moved past it).
- **Models own their data.** Strings are decoded into new `string`s and binary values are copied
  into new `byte[]`s, so no model aliases Arrow memory and disposing the batch cannot affect them.

## Not yet present

No generated path hands out borrowed or pooled memory, and none will until its lifetime rule is
written here first. Writable `MemoryManager<T>` wrappers over Arrow-owned read-only memory are
banned outright: that is how the Parquet Arrow bridge produced a writable alias.
