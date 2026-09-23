# Contributing to Arrow.SourceGenerator

## Prerequisites

- .NET 10 SDK (the `global.json` floor); the .NET 8 runtime to execute the net8.0
  package-consumption target.
- For the Native AOT gate: `clang` and `zlib` development headers (Linux).

## Build, test, format

```bash
dotnet tool restore --disable-parallel
dotnet build Arrow.SourceGenerator.slnx -c Release -warnaserror
dotnet test Arrow.SourceGenerator.slnx -c Release
dotnet csharpier format .
```

Warnings are not errors locally; CI passes `-warnaserror`.

## Golden files and generated API baselines

Each representative model under `test/Arrow.SourceGenerator.Tests/GoldenFiles/Models` produces three
checked-in artefacts:

| File | Guards |
| --- | --- |
| `<Model>.Arrow.g.cs` | Implementation regression — the exact emitted source. |
| `<Model>.Arrow.api.txt` | Public-contract regression — signatures only. |
| `<Model>.Arrow.api.shape.txt` | Public-surface budget — type, member and parameter counts. |

Refresh with `UPDATE_GOLDEN_FILES=true dotnet test` and review the diff. An `.api.txt` diff is an
API change and must be explained in the pull request. A missing baseline fails the run; it is never
created implicitly.

## Native AOT

`dotnet run` executes under CoreCLR and ignores `PublishAot`. The real gate is:

```bash
dotnet publish test/Arrow.SourceGenerator.AotTest -c Release -r linux-x64 -o ./aot-out
./aot-out/Arrow.SourceGenerator.AotTest
```

CI runs exactly this and rejects any `IL` trim/AOT warning.

## Package consumption

```bash
dotnet pack Arrow.SourceGenerator.slnx -c Release -o ./artifacts
dotnet run --project test/Arrow.SourceGenerator.PackageConsumption -f net10.0 \
  -p:GeneratorPackageVersion=0.0.1
```

## Architectural invariants

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
