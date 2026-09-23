# ARROW.SOURCEGENERATOR

## Repository conventions

1. **Squash merge only**, branch deleted on merge, linear history. Direct pushes to `main` are
   blocked; every change goes through a pull request.
2. **Conventional PR titles** (`feat:`, `fix:`, `test:`, `docs:`, `build:`, `ci:`, …) — enforced by
   the `pr-title` check.
3. **Pinned action SHAs**: every third-party action in `.github/workflows/` uses a 40-character
   commit SHA. `WorkflowPolicyTests` enforces it.
4. **CSharpier is the only C# formatter.** Restore it with `dotnet tool restore --disable-parallel`
   and run `dotnet csharpier format .`. Do not reintroduce `dotnet format`.
5. **C# for tooling.** Scripts and tools are C#. The one exception is `scripts/pyarrow_interop.py`,
   which exists because the check needs an independent Arrow implementation.
6. **Local scratch space** goes in `/temp/` and a local `TODO.md`; both are gitignored.
7. **Golden files are refreshed, never hand-edited**: `UPDATE_GOLDEN_FILES=true dotnet test`, then
   review the diff. A missing golden file fails the run, the refresh included; add a new one with
   `UPDATE_GOLDEN_FILES=create`.

## Architectural invariants

These are the invariants in `CONTRIBUTING.md` (from docs/00-DESIGN-GOALS.md §41). A change that
needs to break one needs a design note first, not a code review argument.
