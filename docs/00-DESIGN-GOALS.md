# Arrow.SourceGenerator — Foundations Plan

> **Status:** Foundational design proposal  
> **Purpose:** Establish the architecture, repository shape, product boundary, quality gates, and first implementation sequence for a new `Arrow.SourceGenerator`, using the strongest lessons from `Parquet.SourceGenerator` while deliberately avoiding its accumulated design debt.
>
> **Relationship to Parquet.SourceGenerator:** separate repository, separate product, separate implementation. Reuse ideas, evidence, tests, failure modes, and API-shape lessons. Do not begin with a shared core library.

## 1. Product thesis

`Arrow.SourceGenerator` should make ordinary strongly typed .NET models first-class producers and consumers of Apache Arrow data without reflection.

```text
C# domain model
      ↕
compile-time generated mapping
      ↕
Apache Arrow Schema / Arrays / RecordBatch
```

The product should provide:

- generated Arrow `Schema`;
- model collection → `RecordBatch`;
- `RecordBatch` → model collection/stream;
- strongly typed batch access where useful;
- compile-time type validation and diagnostics;
- reflection-free conversion;
- Native AOT support;
- trimming friendliness;
- explicit ownership/lifetime semantics;
- extension adapters for non-native CLR/domain types.

It should **not** begin life as:

- a Parquet companion package;
- a generic source-generator framework;
- an Arrow IPC wrapper;
- a Flight client library;
- a LINQ/query engine;
- a shared-code extraction from `Parquet.SourceGenerator`.

Those can become integration points later.

## 2. Why a distinct repository

The Arrow generator and Parquet generator share ideas but own different domains.

```text
Parquet.SourceGenerator
C# model
  ↓
Parquet schema / row groups / levels / encoding
  ↓
Parquet.Net
```

```text
Arrow.SourceGenerator
C# model
  ↓
Arrow schema / arrays / buffers / validity / offsets
  ↓
Apache.Arrow
```

A distinct repository gives Arrow its own dependency graph, release cadence, public API, test matrix, benchmark suite, Native AOT contract, type-support roadmap, issue backlog, and integration priorities.

The repositories should converge through **documented concepts**, not forced shared binaries.

## 3. What should be learned directly from Parquet.SourceGenerator

### Preserve successful patterns

1. **Roslyn incremental source generation** — compile-time discovery, no runtime reflection, ordinary generated C# calling the backend's public API.
2. **Golden generated-source tests** — generated code is itself a product artifact, and semantic compilation matters as much as string comparison.
3. **Signature-only generated API baselines** — generated public API must be reviewable separately from implementation diffs.
4. **Native AOT consumer tests** — publish and execute a native binary; do not infer AOT support from ordinary unit tests.
5. **Conditional integration** — the Parquet Arrow bridge showed optional capabilities can be strongly typed without forcing dependencies on unrelated users.
6. **Strict schema validation** — reject mismatched physical/logical types, aggregate useful field-level errors, and do not silently coerce external data.
7. **Columnar-first paths** — Parquet PR #212 demonstrated the value of separating generic columnar handoff from format-specific adapters.
8. **Independent interoperability validation** — Arrow data should be round-tripped through Apache.Arrow IPC and, where useful, PyArrow.

### Avoid repeating accumulated problems

1. **Public API growth by accretion.** Parquet reached dozens of generated public members per model. Arrow should begin with an explicit API budget.
2. **Cross-products encoded as method/type families.** Source × execution × result shape should not multiply public types.
3. **Generator internals made public for tests.** Parser/model/emitter types should be `internal` from day one; use `InternalsVisibleTo`.
4. **Large procedural emitter hubs.** Do not let a single `CodeEmitter` become the architecture.
5. **Plans recomputed repeatedly.** Compute one immutable emission plan and thread it through an emission context.
6. **Bool/string primitive obsession.** Model backend/capability/ownership concepts explicitly rather than threading booleans and raw generated-variable names.
7. **Roslyn objects retained in incremental state.** Do not cache `GeneratorSyntaxContext`, `Compilation`, symbols, or `Location`; convert to small value-equatable models early.
8. **Syntax-node identity used where symbol identity is required.** Duplicate partial declarations must not generate duplicate outputs.
9. **Unsafe ownership tricks before lifetime contracts were settled.** Arrow buffers are ownership-sensitive; borrowed memory must have mechanically safe lifetime rules.
10. **Performance helpers becoming accidental public API.** SIMD/conversion helpers remain internal unless consumers genuinely need them.

## 4. Founding architectural rule

The architecture should be:

```text
Roslyn discovery
      ↓
compile-time domain model
      ↓
logical/adaptation resolution
      ↓
Arrow mapping plan
      ↓
immutable EmissionPlan
      ↓
small emitters
      ↓
generated code
```

Recommended source structure:

```text
Generator
├── Discovery
├── Parsing
├── Adapters
├── Model
├── Planning
├── Emit
│   ├── SchemaEmitter
│   ├── ToArrowEmitter
│   ├── FromArrowEmitter
│   ├── TypedViewEmitter
│   └── SharedHelperEmitter
├── Diagnostics
└── Configuration
```

Dependency direction should be obvious:

```text
Discovery/Parsing
      ↓
Model
      ↓
Adapters / Mapping
      ↓
Planning
      ↓
Emit
```

Emitters must not call back into parsing.

## 5. Repository shape

```text
src/
  Arrow.SourceGenerator/
  Arrow.SourceGenerator.Attributes/

test/
  Arrow.SourceGenerator.Tests/
  Arrow.SourceGenerator.AotTest/
  Arrow.SourceGenerator.PackageConsumption/
  Arrow.SourceGenerator.InteropTests/

benchmarks/
  Arrow.SourceGenerator.Benchmarks/

docs/
  00-DESIGN-GOALS.md
  01-PUBLIC-API.md
  02-TYPE-MAPPING.md
  03-OWNERSHIP.md
  04-ADAPTERS.md
  05-COMPATIBILITY.md
  06-AOT-AND-TRIMMING.md

samples/
  Basic/
  NodaTime/
```

Do not create a legacy backend unless there is a concrete Apache.Arrow compatibility need. Do not create a shared `Core` package at inception.

## 6. Package topology

### `Arrow.SourceGenerator.Attributes`

Consumer-referenceable contract only. Likely contains:

```text
ArrowSerializableAttribute
ArrowColumnAttribute
ArrowIgnoreAttribute
ArrowAdapterAttribute
ArrowTypeAdapterAttribute
```

This assembly should remain tiny.

### `Arrow.SourceGenerator`

Analyzer/source-generator assembly. All implementation types should be internal:

```text
ArrowIncrementalGenerator
TargetParser
TargetModel
MemberModel
ArrowTypePlan
EmissionPlan
SchemaEmitter
...
```

The generator assembly should not maintain a consumer-facing public API contract. Tests use `InternalsVisibleTo`.

## 7. Public API philosophy

Generated API should express user intent, not emitter strategy.

A normal model should expose approximately:

```csharp
OrderArrow.Schema

OrderArrow.ToRecordBatch(rows)
OrderArrow.ToRecordBatches(rows, batchSize)

OrderArrow.FromRecordBatch(batch)
OrderArrow.FromRecordBatches(batches)
```

Potentially:

```csharp
OrderArrow.View(batch)
```

for a validated strongly typed array view.

Avoid exposing state/helper types that exist only because of emission strategy.

## 8. Public API budget from day one

Parquet's generated API-size problem should become a founding Arrow test.

For representative models, CI should enforce:

- public generated type count;
- public member count;
- public parameter count;
- no emitter slot names in signatures;
- no helper method leakage.

A flat model should not emit public methods in proportion to `number_of_fields × operations`.

Field count can naturally affect schema fields and typed-view properties; it should not multiply top-level operation names.

## 9. Model discovery

Use Roslyn's attribute-aware incremental APIs where supported by the chosen Roslyn floor. Prefer `ForAttributeWithMetadataName` over broad “any attributed declaration” syntax providers.

The target is a **type symbol**, not an arbitrary syntax node.

Requirements:

- multiple partial declarations → one target;
- unrelated attributes on another partial part → no duplicate output;
- nested types use collision-safe identities;
- unsupported file-local shapes receive diagnostics rather than colliding hint names;
- generated hint names derive from stable unique type identity.

A regression test should reproduce the Parquet #368 partial-declaration failure and prove Arrow never regresses into it.

## 10. Incremental-state discipline

Collapse Roslyn objects into immutable value data as early as possible.

Good cached model:

```csharp
record TargetModel(
    string Namespace,
    string MetadataName,
    EquatableArray<MemberModel> Members);
```

Do not retain these in cached model state:

```text
Compilation
GeneratorSyntaxContext
ITypeSymbol
IPropertySymbol
Location
List<T>
```

Requirements:

- value equality;
- immutable arrays/wrappers;
- deterministic ordering;
- diagnostic data represented safely;
- named tracking steps so incremental behaviour can be tested.

Incrementality itself should be a tested product property.

## 11. Compile-time domain model

Do not begin with an Arrow-physical parser model.

The parsed model should represent C# meaning:

```text
member name
serialized/logical name
CLR type
nullability
collection shape
nested children
enum underlying type
explicit adapter
metadata annotations
```

Arrow planning then decides:

```text
Arrow type
array implementation
validity handling
offset handling
conversion strategy
buffer ownership strategy
```

This keeps Apache.Arrow implementation details out of parsing.

## 12. Type mapping architecture

Use a central mapping table/strategy similar in spirit to Parquet's `ArrowMappingComponent`, but Arrow-native and structured.

Examples:

```text
int     → Int32       → Int32Array      → fixed-width
string  → Utf8        → StringArray     → offsets + values
bool    → Boolean     → BooleanArray    → bit-packed
DateOnly→ Date32      → Date32Array
decimal → Decimal128  → Decimal128Array
```

Prefer a structured plan:

```csharp
record ArrowLeafPlan(
    ArrowLogicalKind Kind,
    ArrowArrayKind ArrayKind,
    ConversionStrategy Conversion,
    NullStrategy Nulls,
    ...);
```

rather than many loose strings and booleans.

## 13. Supported type scope for the first milestone

### P0 built-ins

- signed/unsigned integer widths;
- `float`;
- `double`;
- `bool`;
- `string`;
- binary input (`byte[]` and carefully chosen equivalents);
- `decimal` → Decimal128;
- `DateOnly`;
- `TimeOnly`;
- `DateTime` under an explicitly documented semantic contract;
- `TimeSpan` if duration semantics are defined;
- `Guid`;
- enums;
- nullable forms of supported scalar types.

### P1 structural types

- nested records/classes/structs → `StructArray`;
- arrays/lists → `ListArray`;
- dictionaries/maps → `MapArray`.

### Later

- dictionary arrays;
- large string/binary/list variants;
- unions;
- fixed-size lists;
- Decimal256;
- custom Arrow extension types.

`0.1` should not depend on supporting the entire Arrow type system.

## 14. Schema as first-class generated output

One of Arrow.SourceGenerator's strongest independent benefits is:

```csharp
Schema schema = OrderArrow.Schema;
```

The generated schema should be:

- deterministic;
- cached/static where safe;
- derived from the exact same plan used for conversion;
- usable when there are zero rows;
- suitable for IPC/Flight/schema negotiation;
- inspectable in tests.

There must not be a separate schema-mapping implementation that can drift from array conversion.

## 15. POCO → RecordBatch path

```text
IReadOnlyCollection<T>
       ↓
generated per-column extraction
       ↓
Arrow buffers/arrays
       ↓
RecordBatch
```

Design principles:

- one pass per required column where reasonable;
- bounded allocation;
- no reflection;
- validity built directly from nullability;
- strings/binary use Arrow offset/value-buffer conventions;
- avoid per-row temporary object allocations;
- Arrow owns the final buffers.

Do not claim zero-copy for row-oriented POCO input. AoS → SoA requires transposition.

The optimisation goal is **minimal necessary copying and allocation**.

## 16. RecordBatch → POCO path

```text
RecordBatch
    ↓
validate once
    ↓
typed generated array access
    ↓
construct T values
```

Requirements:

- strict field/type validation;
- resolve name-based indices once;
- nullability mismatch is an error;
- no silent numeric coercion;
- decimal precision/scale checked;
- temporal unit/timezone semantics checked;
- array length equals batch length;
- dictionary/extension types either deliberately supported or clearly rejected.

The strict validation in Parquet's Arrow bridge is a useful prototype.

## 17. Strongly typed batch view

A useful ergonomic feature:

```csharp
var view = OrderArrow.View(batch);

Int64Array ids = view.Id;
StringArray names = view.Name;
Decimal128Array totals = view.Total;
```

Creation validates the schema once. After that there are no repeated name lookups, casts, or ordinal constants in user code.

The view should wrap the `RecordBatch`; it should not copy it.

## 18. Ownership must be a documented design axis

Before emitting any buffer trick, answer:

```text
Who owns this memory?
How long is it valid?
Can it be mutated?
Can another component obtain a writable alias?
When is it disposed?
```

The Parquet Arrow bridge exposed an important failure mode: a `MemoryManager<T>` over Arrow-owned read-only memory can accidentally produce a writable alias.

Founding invariant:

> **Read-only source memory must never be laundered into writable memory.**

A dedicated `docs/03-OWNERSHIP.md` should exist before zero-copy optimisations land.

## 19. Zero-copy classification

Use precise terminology.

### True zero-copy
Final Arrow arrays directly own or safely reference compatible source buffers with compatible lifetime.

### View-only zero-copy
Generated code exposes a read-only typed view over an existing Arrow buffer without payload copy.

### Structural copy
Offsets/validity/array object are created while payload is reused.

### Conversion copy
Values are transformed into new Arrow-owned buffers.

Tests and benchmarks should report which category each mapping uses.

## 20. Adapter system is a founding feature

The type-adapter extension model should be planned early, even if implemented after the first built-ins.

```text
CLR member type
     ↓
built-in mapping?
     ├─ yes
     └─ no
         ↓
registered Arrow adapter?
         ↓
surrogate type
         ↓
ordinary Arrow planning
```

Use the independently designed `TDomain ↔ TSurrogate` model with statically resolved `ToStorage` / `FromStorage` extension methods.

The Arrow generator should own its adapter discovery, precedence, ambiguity diagnostics, nullability, surrogate recursion, and AOT-safe direct calls. It should not depend on the Parquet adapter implementation.

## 21. NodaTime as the first adapter stress test

NodaTime should be the first serious external adapter package:

```text
Arrow.SourceGenerator.NodaTime
```

It forces correct handling of:

- temporal semantics;
- precision/range;
- local vs global time;
- structural surrogates;
- native representation vs lossless representation.

Default NodaTime adapters should be lossless. Narrower native Arrow temporal mappings can be explicit interoperability adapters.

## 22. Emitter architecture

Do not create one giant emitter.

Recommended shape:

```text
ArrowEmitter
  ├── SchemaEmitter
  ├── ToRecordBatchEmitter
  ├── FromRecordBatchEmitter
  ├── TypedViewEmitter
  ├── AdapterCallEmitter
  └── HelperEmitter
```

Each consumes an `EmissionContext` containing approximately:

```text
TargetModel
EmissionPlan
GeneratorConfiguration
stable naming helpers
```

The `EmissionPlan` is built once. Emitter components must not independently reconstruct mappings.

## 23. EmissionPlan

`EmissionPlan` should be immutable and value-oriented.

It should contain all resolved decisions necessary to emit:

```text
field order
Arrow field/type plan
array kind
null strategy
conversion strategy
adapter calls
nested child plans
generated safe identifiers
schema metadata
```

A key invariant:

> schema emission, write emission, read emission, and typed-view emission consume the same plan.

That prevents drift.

## 24. Generated identifier/literal safety

The Parquet project found multiple escaping/source-injection defects because raw user strings reached generated source.

Arrow.SourceGenerator should centralise safe forms for:

```text
C# identifier
C# string literal
XML-doc text
comment-safe text
hint-name identity
```

No emitter should interpolate raw model/member metadata directly.

Tests should include quotes, backslashes, newlines, XML characters, C# keywords, Unicode, and nested names.

## 25. Diagnostics-first unsupported behaviour

Unsupported types should not silently cause missing generated API.

Example diagnostic families:

```text
ARROW001 Unsupported member type
ARROW002 Invalid decimal precision/scale
ARROW003 Unsupported adapter surrogate
ARROW004 Nested cycle detected
ARROW005 Ambiguous adapter
ARROW006 Nullability/schema mismatch
ARROW007 Unsupported configured capability
```

A consumer should understand at compile time why generation cannot proceed.

## 26. Configuration model

Start with very little configuration. Every option multiplies state and tests.

Likely early configuration:

- batch-size default;
- strict schema-name policy;
- possibly `DateTime` semantic policy.

Prefer attributes for schema semantics and options/parameters for execution choices.

Do not introduce feature-profile levels until real compatibility pressure exists. If profiles eventually exist, encode the capability matrix as data rather than scattered `if` statements.

## 27. Native AOT from the first meaningful feature

Add an AOT test app before first release.

It should exercise:

```text
generated schema
POCO → RecordBatch
RecordBatch → POCO
nullable values
string/binary
decimal
temporal type
adapter call
```

Then:

```text
dotnet publish NativeAOT
run native executable
assert all checks
```

A normal unit test is not an AOT test.

## 28. Interoperability testing

### C# self-validation
Inspect generated batches using Apache.Arrow typed arrays.

### IPC round trip
Write/read through Apache.Arrow IPC. This catches invalid offsets, validity bitmaps, schema mismatches, and malformed nested structures.

### External validation
Add PyArrow fixtures for a representative subset, especially temporal semantics, decimals, nested shapes, metadata, and later extension types.

Do not require every unit test to invoke Python.

## 29. Golden test strategy

Each representative model should produce:

```text
<Model>.Arrow.g.cs
<Model>.Arrow.api.txt
<Model>.Arrow.api.shape.txt
```

- golden source → implementation regression;
- API baseline → public-contract regression;
- API shape → public-surface budget.

Every golden `.g.cs` should also be semantically compiled. A missing baseline should fail rather than self-heal in release CI.

## 30. Representative golden models

### `ScalarEvent`
All scalar built-ins, nullable forms, decimal, temporal values, enum, string/binary.

### `KeywordAndEscaping`
C# keywords, renamed fields, quotes/newlines/XML chars, nested names.

### `NestedOrder`
Later: struct, list, map.

### `AdapterEvent`
Custom adapter, nullable adapted value, structural surrogate, explicit override.

### `NodaTimeEvent`
Integration/sample coverage for the NodaTime package.

Keep the corpus small enough that API diffs remain readable.

## 31. Performance philosophy

Use benchmarks to answer design questions, not to justify cleverness.

Compare:

```text
hand-written Apache.Arrow
generated Arrow
reflection/general mapper where available
```

for both directions:

```text
POCO → RecordBatch
RecordBatch → POCO
```

Track throughput, allocations, peak temporary memory, batch-size sensitivity, wide-schema sensitivity, null density, and string size.

Parquet PR #326 provides an important warning: consolidating all converted columns into one intermediate batch increased peak temporary memory from roughly the largest temporary column to the sum of all temporary columns. Arrow planning must therefore optimise **lifetime and peak memory**, not only allocation count.

## 32. Optimisations that should wait

Do not begin with:

- custom SIMD libraries;
- unsafe pointer arithmetic;
- writable memory managers over external buffers;
- borrowed pool-backed public APIs;
- vectorised special cases without baseline measurement.

First implement the correct ordinary version. Benchmark it. Then optimise one measured bottleneck at a time.

Optimisation helpers remain internal.

## 33. Security/correctness stance

Generated reads may process externally-originating `RecordBatch`/IPC data. Validate assumptions before unsafe reinterpretation.

At minimum:

- field exists;
- Arrow type matches;
- type parameters match;
- lengths match;
- required fields contain no nulls;
- nested child lengths/offsets are structurally valid;
- adapter/extension metadata is treated as untrusted;
- integer size calculations are checked.

Generated code should fail with clear managed exceptions, not memory corruption or process-terminating access violations.

## 34. Dependency/version policy

Pin Apache.Arrow in repository tests and define a supported version floor explicitly.

Before widening compatibility, run package-consumption tests against:

```text
minimum supported
current pinned
newest intended
```

Generated code should call Apache.Arrow's public API directly. Do not hide version incompatibility behind reflection.

## 35. First public API sketch

Illustrative only:

```csharp
[ArrowSerializable]
public partial record Order(
    long Id,
    string? Customer,
    decimal Total);
```

Generated:

```csharp
public static class OrderArrow
{
    public static Apache.Arrow.Schema Schema { get; }

    public static Apache.Arrow.RecordBatch ToRecordBatch(
        IReadOnlyCollection<Order> rows);

    public static IEnumerable<Apache.Arrow.RecordBatch> ToRecordBatches(
        IEnumerable<Order> rows,
        int batchSize);

    public static Order[] FromRecordBatch(
        Apache.Arrow.RecordBatch batch);

    public static OrderArrowView View(
        Apache.Arrow.RecordBatch batch);
}
```

Do not add many variants until real usage proves they are needed.

## 36. Streaming should follow core batch conversion

Do not begin by multiplying synchronous/asynchronous variants.

First prove:

```text
collection ↔ one RecordBatch
```

Then:

```text
sequence ↔ multiple RecordBatches
```

Then add async sequence support only if it provides real value:

```text
IAsyncEnumerable<T>
      ↓
IAsyncEnumerable<RecordBatch>
```

This keeps ownership and batching semantics tractable.

## 37. Relationship to Parquet.SourceGenerator

The two products should interoperate through Apache.Arrow, not generator internals.

```text
domain model
     ↓
Arrow.SourceGenerator
     ↓
RecordBatch
     ↓
Parquet.SourceGenerator Arrow bridge
     ↓
Parquet
```

Parquet can keep its Arrow bridge because `RecordBatch → Parquet` is a Parquet interoperability feature.

Arrow.SourceGenerator owns `domain model ↔ RecordBatch`.

Neither generator should reference the other's generator assembly.

## 38. What not to copy from Parquet

Do not transplant implementation code merely because it exists.

Do not wholesale copy:

- `TargetClassModel`;
- `PropertyModel`;
- `CodeEmitter`;
- buffer-pool components;
- feature-profile machinery;
- public helper APIs;
- decompression/security infrastructure;
- legacy backend abstractions;
- Parquet-oriented Arrow conversion expressions.

Copy instead:

- tests as ideas;
- bug repros;
- architectural lessons;
- validation rules;
- benchmark methodology;
- API governance techniques.

The new project should be smaller because those lessons are already learned.

## 39. Initial milestone sequence

### Foundation 0 — repository and gates

Create solution/projects, formatting/analyzers, unit tests, package-consumption tests, Native AOT app, benchmarks, golden infrastructure, generated API baselines, and CI.

No functional breadth yet.

### Foundation 1 — discovery/model

Implement:

- `[ArrowSerializable]`;
- symbol-keyed target discovery;
- immutable value-equatable model;
- safe generated naming;
- diagnostics pipeline;
- incremental tracking tests.

Emit a trivial marker source first.

### Foundation 2 — Arrow planning + Schema

Implement P0 scalar mapping and generated `Schema`.

Acceptance:

- schema matches model;
- nullability correct;
- decimal/time parameters correct;
- golden/API/AOT tests green.

### Foundation 3 — POCO → RecordBatch

Implement scalar batch construction.

Acceptance:

- no reflection;
- IPC round trip;
- Native AOT;
- baseline benchmark vs hand-written Arrow;
- bounded peak allocation.

### Foundation 4 — RecordBatch → POCO

Implement strict validation and materialisation.

Acceptance:

- malformed/mismatched batches fail clearly;
- nullability enforced;
- round trip across all P0 scalar kinds;
- IPC-originating batches work.

### Foundation 5 — typed view

Add a validated strongly typed `RecordBatch` view only if it materially improves ergonomics.

### Foundation 6 — adapter mechanism

Implement exact-type static adapters and a trivial external package/sample.

### Foundation 7 — NodaTime

Implement `Arrow.SourceGenerator.NodaTime` according to the separate NodaTime design.

### Foundation 8 — compound Arrow types

Add struct/list/map support after the core model/planner/emitter design has survived real use.

## 40. 0.1 definition

`Arrow.SourceGenerator 0.1` should be a confidence milestone, not “the complete Arrow type system”.

A strong 0.1 could include:

- flat models;
- scalar/nullability support;
- generated `Schema`;
- POCO → RecordBatch;
- RecordBatch → POCO;
- strict validation;
- Native AOT;
- IPC round-trip tests;
- selected PyArrow validation;
- API-size gate;
- adapter mechanism;
- initial NodaTime adapter package, or at minimum a proven adapter sample.

It does **not** need:

- Flight wrappers;
- IPC convenience APIs;
- nested collections/maps if not yet trustworthy;
- dictionary arrays;
- union arrays;
- every Apache.Arrow type;
- query/LINQ;
- generic adapters;
- shared Parquet/Arrow implementation code.

## 41. Architectural invariants for CONTRIBUTING.md

1. **Generated public API is a product contract.**
2. **Generator implementation types are internal.**
3. **One model → one immutable plan per emission.**
4. **Roslyn symbols do not survive into cached model state.**
5. **User strings never reach generated source unescaped.**
6. **Arrow ownership/lifetime is explicit for every zero-copy/view path.**
7. **No unsupported type silently disappears; emit a diagnostic.**
8. **AOT claims require a natively published and executed test.**
9. **Performance claims require benchmarks against a meaningful baseline.**
10. **Optimisation helpers remain internal until a consumer use case proves otherwise.**
11. **Adapter defaults are deterministic and ambiguity is an error.**
12. **Parquet and Arrow share concepts before they share code.**

## 42. The core lesson from Parquet.SourceGenerator

The most valuable inheritance from Parquet.SourceGenerator is not code. It is evidence.

The Parquet project has already shown that:

- source-generated columnar mapping is practical;
- Apache.Arrow can work through generated strongly typed code;
- Native AOT is achievable;
- strict schema validation is valuable;
- generated code can avoid reflection entirely;
- columnar interoperability is useful even when it is not faster than row materialisation;
- public API growth becomes expensive very quickly;
- ownership shortcuts are more dangerous than ordinary copies;
- incremental-generator architecture matters as much as runtime performance;
- good testing infrastructure is easier to build before features multiply.

`Arrow.SourceGenerator` should therefore start smaller, stricter, and more intentionally layered than Parquet.SourceGenerator did.

Its foundational advantage is that the expensive lessons have already been paid for.
