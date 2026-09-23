## Description
What this pull request changes and why.

---

## Verification checklist
- [ ] `dotnet build Arrow.SourceGenerator.slnx -c Release -warnaserror` is clean.
- [ ] `dotnet test Arrow.SourceGenerator.slnx -c Release` passes.
- [ ] `dotnet csharpier check .` passes (CSharpier is the only C# formatter).
- [ ] Generated API changes are intended: every `*.api.txt` / `*.api.shape.txt` diff is explained
      above, and the API budget test still passes (`docs/01-PUBLIC-API.md`).
- [ ] Native AOT claims are covered by `test/Arrow.SourceGenerator.AotTest` (published, not `dotnet run`).
- [ ] Performance claims come with a benchmark against the hand-written baseline.
- [ ] Docs (`README.md`, `docs/`, `CHANGELOG.md`) updated where behaviour changed.
