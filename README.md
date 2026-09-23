# Arrow.SourceGenerator

Compile-time, reflection-free mapping between ordinary .NET models and
[Apache Arrow](https://arrow.apache.org/) — generated `Schema`, model collection → `RecordBatch`,
and `RecordBatch` → model collection, with strict validation and Native AOT support.

> **Status: pre-0.1, under construction.** The repository is being built up milestone by milestone
> (docs/00-DESIGN-GOALS.md §39). Nothing here is released yet.

## Intended shape

```csharp
[ArrowSerializable]
public partial record Order(long Id, string? Customer, decimal Total);

Schema schema = OrderArrow.Schema;
using RecordBatch batch = OrderArrow.ToRecordBatch(orders);
Order[] roundTripped = OrderArrow.FromRecordBatch(batch);
```

A small, deliberately budgeted public surface per model; everything else is internal.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/Arrow.SourceGenerator` | The incremental generator (analyzer-only package, all types internal). |
| `src/Arrow.SourceGenerator.Attributes` | The consumer-referenceable contract: model annotations only. |
| `test/Arrow.SourceGenerator.Tests` | Unit, generator-driver, golden and API-baseline tests. |
| `test/Arrow.SourceGenerator.AotTest` | Native AOT gate — published and executed natively in CI. |
| `test/Arrow.SourceGenerator.PackageConsumption` | Consumes the packed `.nupkg` files like an outside project. |
| `benchmarks/Arrow.SourceGenerator.Benchmarks` | BenchmarkDotNet, always against a hand-written Apache.Arrow baseline. |
| `docs/` | Design goals and the contracts each capability must satisfy. |

## Building

```bash
dotnet tool restore --disable-parallel
dotnet build Arrow.SourceGenerator.slnx -c Release -warnaserror
dotnet test Arrow.SourceGenerator.slnx -c Release
dotnet csharpier check .
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the Native AOT and package-consumption gates.

## Relationship to Parquet.SourceGenerator

A separate product with a separate implementation. It reuses the *lessons* of
[Parquet.SourceGenerator](https://github.com/rtkelly13/Parquet.SourceGenerator) — tests as ideas,
bug repros, validation rules, API-governance techniques — not its code. The two interoperate
through Apache.Arrow's `RecordBatch`, never through each other's generator assemblies.

## License

MIT — see [LICENSE](LICENSE).
