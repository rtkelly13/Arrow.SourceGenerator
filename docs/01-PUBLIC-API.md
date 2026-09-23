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
}
```

The companion has the target's accessibility (a nested target's companion is nested beside it in
the same containing type).

## Budget

The companion exposes a fixed set of operations that does **not** grow with the number of fields:

| Member | Since |
| --- | --- |
| `Schema` | Foundation 2 |

Rules enforced by `ApiBudgetTests`:

- the companion's public members are exactly the table above;
- a 1-field and an 80-field model expose the same number of operations;
- no signature contains an emitter slot name (`column_0`, `field_3`, `__x`) or a helper.

Adding a row is an API decision: it changes this table, the test's `CompanionOperations`, and the
golden `.api.txt` files in the same pull request.
