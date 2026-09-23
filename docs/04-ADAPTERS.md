# 04 — Type adapters

A type adapter maps a domain type with no built-in Arrow mapping (a `Money`, an `EmailAddress`, a
NodaTime `LocalDate`) onto one that has one — its **surrogate** — through two static methods:

```csharp
public static class MoneyAdapter
{
    public static decimal ToStorage(this Money value) => value.Amount;   // extension or plain static
    public static Money FromStorage(decimal storage) => new(storage);
}
```

Generated code calls these directly (`global::Ns.MoneyAdapter.ToStorage(x)`): no reflection, no
delegates, no interface dispatch, so adapters are Native AOT and trimming safe.

## Registering

```csharp
[assembly: ArrowTypeAdapter(typeof(MoneyAdapter))]   // default for every Money member

[ArrowSerializable]
public partial class Invoice
{
    [ArrowDecimal(18, 2)] public Money Total { get; set; }      // Decimal128(18, 2)
    public Money? Discount { get; set; }                        // nullable Decimal128(38, 18)

    [ArrowAdapter(typeof(UnixSecondsAdapter))]                   // per-member override,
    public DateTimeOffset IssuedAt { get; set; }                 // even of a built-in → Int64
}
```

An assembly-level registration also applies in every assembly that **references** the registering
one. That is how an adapter package (for example a future `Arrow.SourceGenerator.NodaTime`) works:
reference it and its registrations apply.

## Precedence

Highest first, and deterministic — declaration order never decides:

1. `[ArrowAdapter]` on the member (or on a positional-record parameter).
2. For a type **without** a built-in mapping: a registration in the compiling assembly.
3. A registration in a referenced assembly.

Built-in types (`DateTime`, `decimal`, …) are never adapted by registration — registering one is
ARROW017 — only by an explicit `[ArrowAdapter]`. Two registrations for one type at the tier that
decides are **ARROW005**, an error, never a silent pick.

## The contract (validated at compile time)

| Rule | Otherwise |
| --- | --- |
| Exactly one `static ToStorage(TDomain)` and one `static FromStorage(TSurrogate)`, non-generic, by-value parameter, non-void. | ARROW017 |
| They are inverses: `ToStorage: TDomain → TSurrogate`, `FromStorage: TSurrogate → TDomain`. | ARROW017 |
| Neither `TDomain` nor `TSurrogate` is `Nullable<T>`. | ARROW017 |
| The adapter is non-generic and callable from generated code (public when in another assembly). | ARROW017 (a broken registration in a *referenced* assembly simply does not participate) |
| A member adapter's `TDomain` is exactly the member's type. | ARROW017 |
| `TSurrogate` is a built-in (docs/02-TYPE-MAPPING.md). Adapter chains and structural surrogates are not supported yet. | ARROW003 |

## Nulls

Nulls never reach an adapter. For `Money?` or `EmailAddress?` the generator writes null itself and
calls the adapter only for values. For a non-nullable reference domain, a null member is reported
(`ArgumentException` naming the field) before the adapter could be called. Arrow-side nullability
follows the **member**, as for any other field; `[ArrowDecimal]` and the other annotations apply to
the surrogate's Arrow type.

## Exceptions

An adapter may throw — `FromStorage` is where domain validation belongs (an `EmailAddress` without
an `@`). Its exception propagates unchanged from `ToRecordBatch` / `FromRecordBatch`.

## Lossless by default

A default adapter should be lossless. Narrower, native-Arrow interoperability mappings belong behind
an explicit `[ArrowAdapter]`, or at least should throw rather than silently narrow — see
`samples/NodaTime`, whose `LocalDate → DateOnly` and `Instant → DateTimeOffset` adapters throw for
values outside the surrogate's range. Fully lossless NodaTime defaults (nanosecond `Instant`
across ±10,000 years) need structural surrogates, which arrive with struct support (Foundation 8).
