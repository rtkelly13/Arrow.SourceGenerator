# 06 — Native AOT and trimming

## Claim

Generated code is Native AOT and trimming compatible: it uses no reflection, no
`System.Linq.Expressions`, no runtime code generation, and calls Apache.Arrow's public API and the
model's own members directly.

## Evidence

A claim is only as good as the test that executes it. `test/Arrow.SourceGenerator.AotTest` is:

1. published with `-r linux-x64` (the ILCompiler runs, and fails on AOT violations);
2. checked for `ILxxxx` trim/AOT warnings — any warning fails CI;
3. executed as a native binary, which throws on any mismatch.

Ordinary unit tests are not AOT tests; `dotnet run` executes under CoreCLR.

## What the matrix covers

Each capability adds its checks to the AOT program as it lands. The baseline check drives
Apache.Arrow by hand, so a failure there points at the dependency rather than generated code.
