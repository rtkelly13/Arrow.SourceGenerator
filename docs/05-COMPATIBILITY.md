# 05 — Compatibility and version policy

## Supported floors

| Axis | Floor | Pinned in tests | Notes |
| --- | --- | --- | --- |
| Roslyn (host compiler) | 4.8 (.NET 8 SDK, VS 17.8) | 4.14 | The generator compiles against the floor (`RoslynFloor` in its project), so it cannot call newer APIs. |
| Apache.Arrow | 23.0.0 | 23.0.0 | Generated code calls Apache.Arrow's public API directly — never through reflection. |
| Consumer target framework | net8.0 | net8.0, net10.0 | `DateOnly`/`TimeOnly` members need net6.0+; the floor is what CI executes. |
| Attributes package | netstandard2.0 | — | Attributes only; no runtime behaviour. |

## Widening a floor

Before a floor moves, the package-consumption project is built against the minimum supported,
current pinned, and newest intended versions (`-p:ApacheArrowVersion=...`). A floor is a promise:
lowering one requires that evidence, raising one is a breaking change recorded in `CHANGELOG.md`.

## Pre-0.1

Breaking changes are acceptable before 0.1 when they shrink accidental API or make an unsafe
contract unrepresentable. They are still recorded in `CHANGELOG.md`.
