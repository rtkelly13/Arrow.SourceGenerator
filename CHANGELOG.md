# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Foundation 3: `{Type}Arrow.ToRecordBatch(rows)` and lazy `ToRecordBatches(rows, batchSize)`.
  Column-at-a-time conversion into exactly reserved Arrow buffers; validity bitmaps only when a
  column has nulls; clear errors for null rows, null non-nullable members, decimals that do not fit,
  and collections that change mid-conversion. IPC round-trip tests, the Native AOT write path, a
  generated-vs-hand-written benchmark, and `docs/03-OWNERSHIP.md`.
- Foundation 2: Arrow planning and the generated `{Type}Arrow.Schema`. P0 scalar mapping table
  (`docs/02-TYPE-MAPPING.md`), `[ArrowDecimal(p, s)]`, an immutable `EmissionPlan` shared by all
  emitters, ARROW001 (unsupported member type), ARROW002 (invalid decimal), ARROW020 (Apache.Arrow
  not referenced), and the public API budget gate (`docs/01-PUBLIC-API.md`). The Attributes
  package now declares the Apache.Arrow 23.0.0 floor.
- Foundation 1: `[ArrowSerializable]`, `[ArrowColumn]`, `[ArrowIgnore]`; symbol-keyed discovery via
  `ForAttributeWithMetadataName` (one output per type, however many partial declarations); an
  immutable, value-equatable C# model with no Roslyn objects in cached state; constructor binding
  for positional records, init-only and settable members; diagnostics ARROW008–ARROW019; central
  identifier/literal/XML/comment/hint-name escaping; a companion `{Type}Arrow` marker emitted beside
  each target; golden, API-baseline and incrementality tests.
- Foundation 0: solution layout, central package management, analyzers and CSharpier gates,
  generator test harness, signature-only generated-API renderer, Native AOT gate, package
  consumption gate, hand-written Apache.Arrow benchmark baseline, and CI.
