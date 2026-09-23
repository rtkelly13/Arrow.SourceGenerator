; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ARROW008 | Arrow.SourceGenerator | Error | Arrow target must be partial
ARROW009 | Arrow.SourceGenerator | Error | Containing type of an Arrow target must be partial
ARROW010 | Arrow.SourceGenerator | Error | Unsupported Arrow target
ARROW011 | Arrow.SourceGenerator | Error | Generated companion name is already taken
ARROW012 | Arrow.SourceGenerator | Warning | Arrow target has no mapped members
ARROW013 | Arrow.SourceGenerator | Error | Invalid or duplicate Arrow field name
ARROW014 | Arrow.SourceGenerator | Error | Member cannot be assigned when reading
ARROW015 | Arrow.SourceGenerator | Error | No usable constructor
ARROW016 | Arrow.SourceGenerator | Warning | Public field is not mapped
ARROW018 | Arrow.SourceGenerator | Error | Required member is not mapped
ARROW019 | Arrow.SourceGenerator | Error | Ambiguous constructor
