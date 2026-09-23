using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Arrow.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Runs <see cref="ArrowIncrementalGenerator"/> over in-memory sources the way the compiler does,
/// and exposes the pieces tests assert on: emitted sources, generator diagnostics, and the
/// diagnostics of the compilation that includes the emitted code.
/// </summary>
internal static class GeneratorHarness
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> DefaultReferences = new(
        CreateDefaultReferences
    );

    /// <summary>Every assembly the test host runs on, plus Apache.Arrow and the attributes.</summary>
    public static ImmutableArray<MetadataReference> References => DefaultReferences.Value;

    /// <summary>The same set without Apache.Arrow, for the "reference missing" behaviour.</summary>
    public static ImmutableArray<MetadataReference> ReferencesWithoutArrow =>
        References.RemoveAll(reference =>
            reference.Display is { } path
            && Path.GetFileNameWithoutExtension(path)
                .Equals("Apache.Arrow", StringComparison.OrdinalIgnoreCase)
        );

    public static CSharpParseOptions ParseOptions { get; } =
        new(LanguageVersion.Latest, DocumentationMode.Diagnose);

    public static CSharpCompilation CreateCompilation(
        IEnumerable<string> sources,
        IEnumerable<MetadataReference>? references = null,
        string assemblyName = "GeneratorHarnessAssembly"
    ) =>
        CSharpCompilation.Create(
            assemblyName,
            sources.Select(
                (source, index) =>
                    CSharpSyntaxTree.ParseText(source, ParseOptions, path: $"Source{index}.cs")
            ),
            references ?? References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                // Documentation is parsed (so malformed XML docs in emitted code are reported) but
                // test models are not required to document themselves.
                // CS1701/CS1702 (assembly unification) are suppressed by the SDK in real builds too.
                specificDiagnosticOptions:
                [
                    new("CS1591", ReportDiagnostic.Suppress),
                    new("CS1701", ReportDiagnostic.Suppress),
                    new("CS1702", ReportDiagnostic.Suppress),
                ]
            )
        );

    public static CSharpGeneratorDriver CreateDriver(bool trackSteps = false) =>
        CSharpGeneratorDriver.Create(
            [new ArrowIncrementalGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: trackSteps
            )
        );

    public static GeneratorOutcome Run(params string[] sources) => Run(sources, references: null);

    public static GeneratorOutcome Run(
        IEnumerable<string> sources,
        IEnumerable<MetadataReference>? references
    )
    {
        CSharpCompilation compilation = CreateCompilation(sources, references);
        GeneratorDriver driver = CreateDriver()
            .RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation output,
                out ImmutableArray<Diagnostic> generatorDiagnostics
            );

        return new GeneratorOutcome(
            driver.GetRunResult().Results.Single(),
            generatorDiagnostics,
            output
        );
    }

    private static ImmutableArray<MetadataReference> CreateDefaultReferences()
    {
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        var paths = new SortedSet<string>(
            trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal
        )
        {
            typeof(Apache.Arrow.RecordBatch).Assembly.Location,
            // By path rather than typeof(...): the attributes assembly is copied beside the test
            // assembly by the project reference, and this keeps the harness independent of which
            // public types it happens to declare.
            Path.Combine(AppContext.BaseDirectory, "Arrow.SourceGenerator.Attributes.dll"),
        };

        return
        [
            .. paths
                .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ];
    }
}

/// <summary>What one generator run produced.</summary>
internal sealed record GeneratorOutcome(
    GeneratorRunResult RunResult,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    Compilation OutputCompilation
)
{
    public ImmutableArray<GeneratedSourceResult> GeneratedSources => RunResult.GeneratedSources;

    /// <summary>Errors and warnings from compiling the user sources together with the output.</summary>
    public IReadOnlyList<Diagnostic> CompilationProblems =>
        OutputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToList();

    public string SourceFor(string hintNameSuffix) =>
        GeneratedSources
            .Single(source => source.HintName.EndsWith(hintNameSuffix, StringComparison.Ordinal))
            .SourceText.ToString();
}
