# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Foundation 1: `[ArrowSerializable]`, `[ArrowColumn]`, `[ArrowIgnore]`; symbol-keyed discovery via
  `ForAttributeWithMetadataName` (one output per type, however many partial declarations); an
  immutable, value-equatable C# model with no Roslyn objects in cached state; constructor binding
  for positional records, init-only and settable members; diagnostics ARROW008–ARROW019; central
  identifier/literal/XML/comment/hint-name escaping; a companion `{Type}Arrow` marker emitted beside
  each target; golden, API-baseline and incrementality tests.
- Foundation 0: solution layout, central package management, analyzers and CSharpier gates,
  generator test harness, signature-only generated-API renderer, Native AOT gate, package
  consumption gate, hand-written Apache.Arrow benchmark baseline, and CI.
