# 01 — Public API

Generated public API is a product contract (invariant 1). This page is the contract; the
`*.api.txt` golden baselines are its evidence; `ApiBudgetTests` is its gate.

## Package surface

`Arrow.SourceGenerator.Attributes` contains model annotations only, tracked in
`PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` (RS0016/RS0017):

| Type | Purpose |
| --- | --- |
| `ArrowSerializableAttribute` | Marks a partial class/struct/record as a target. |
| `ArrowColumnAttribute` | Field name and explicit order. Allowed on a positional-record parameter. |
| `ArrowIgnoreAttribute` | Excludes a member. |
| `ArrowDecimalAttribute` | `Decimal128` precision and scale for a `decimal` member. |

The `Arrow.SourceGenerator` package is analyzer-only: every type in it is internal and nothing can
compile against it.

## Generated surface per model

For `[ArrowSerializable] partial class Order`, the generator emits one companion type beside it:

```csharp
public static partial class OrderArrow
{
    public static Apache.Arrow.Schema Schema { get; }

    public static Apache.Arrow.RecordBatch ToRecordBatch(IReadOnlyCollection<Order> rows);
    public static IEnumerable<Apache.Arrow.RecordBatch> ToRecordBatches(IEnumerable<Order> rows, int batchSize);

    public static Order[] FromRecordBatch(Apache.Arrow.RecordBatch batch);
    public static IEnumerable<Order> FromRecordBatches(IEnumerable<Apache.Arrow.RecordBatch> batches);
}
```

The companion has the target's accessibility (a nested target's companion is nested beside it in
the same containing type).

## Budget

The companion exposes a fixed set of operations that does **not** grow with the number of fields:

| Member | Since |
| --- | --- |
| `Schema` | Foundation 2 |
| `ToRecordBatch(IReadOnlyCollection<T>)` | Foundation 3 |
| `ToRecordBatches(IEnumerable<T>, int)` | Foundation 3 |
| `FromRecordBatch(RecordBatch)` | Foundation 4 |
| `FromRecordBatches(IEnumerable<RecordBatch>)` | Foundation 4 |

## Reading contract

`FromRecordBatch` validates the whole batch once, then materialises. Fields are matched by **name**
(ordinal comparison), so field order and extra fields do not matter. Rejected, all at once in one
`InvalidDataException`:

- a missing field, or one that appears more than once;
- an array whose type differs from the mapped Arrow type — no numeric widening, no unit conversion,
  no reinterpretation of wall-clock and UTC timestamps;
- type parameters that differ: decimal precision/scale, time units, timestamp timezone presence,
  fixed-size width;
- dictionary-encoded and extension-typed columns;
- a schema field whose type disagrees with its column;
- a column whose length differs from the batch;
- nulls in a field mapped to a non-nullable member (checked on the data, so producers such as
  PyArrow that mark every field nullable interoperate);
- structurally unsound buffers: short value buffers, short validity bitmaps, and offsets that
  decrease or run past the value buffer.

Individual values that the CLR type cannot hold (a `Date32` past `DateOnly.MaxValue`, a
`Decimal128` wider than `System.Decimal`'s 96 bits) fail with the row and field named. Nothing is
rounded: this deliberately does not use `Decimal128Array.GetValue`, which rounds.

Construction: a positional record's primary constructor, or any reachable constructor whose
parameters bind to mapped members, is called; remaining members are assigned through an object
initializer (so `init` and `required` members work). See ARROW014/015/019 for the shapes that cannot
be materialised.

Rules enforced by `ApiBudgetTests`:

- the companion's public members are exactly the table above;
- a 1-field and an 80-field model expose the same number of operations;
- no signature contains an emitter slot name (`column_0`, `field_3`, `__x`) or a helper.

Adding a row is an API decision: it changes this table, the test's `CompanionOperations`, and the
golden `.api.txt` files in the same pull request.
