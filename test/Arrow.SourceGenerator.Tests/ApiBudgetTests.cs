using System.Text.RegularExpressions;
using Arrow.SourceGenerator.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// The public API budget (docs/01-PUBLIC-API.md, docs/00-DESIGN-GOALS.md section 8). Parquet's
/// generated surface grew to dozens of members per model by accretion; this makes growth an
/// explicit, reviewed edit to <see cref="CompanionOperations"/> instead.
/// </summary>
public sealed partial class ApiBudgetTests
{
    /// <summary>
    /// Every public member the companion type may expose, independent of the model. Adding an
    /// entry is an API decision and belongs in docs/01-PUBLIC-API.md in the same change.
    /// </summary>
    private static readonly string[] CompanionOperations =
    [
        "static FromRecordBatch(Apache.Arrow.RecordBatch batch) -> Model[]",
        "static FromRecordBatches(System.Collections.Generic.IEnumerable<Apache.Arrow.RecordBatch> batches) -> System.Collections.Generic.IEnumerable<Model>",
        "static Schema.get -> Apache.Arrow.Schema",
        "static ToRecordBatch(System.Collections.Generic.IReadOnlyCollection<Model> rows) -> Apache.Arrow.RecordBatch",
        "static ToRecordBatches(System.Collections.Generic.IEnumerable<Model> rows, int batchSize) -> System.Collections.Generic.IEnumerable<Apache.Arrow.RecordBatch>",
        "static View(Apache.Arrow.RecordBatch batch) -> ModelArrowView",
    ];

    /// <summary>
    /// The view's fixed members. Beyond these it has exactly one property per field — the one
    /// place field count legitimately shapes the surface (docs/00-DESIGN-GOALS.md section 8).
    /// </summary>
    private static readonly string[] ViewFixedMembers =
    [
        "ModelArrowView.Batch.get -> Apache.Arrow.RecordBatch",
        "ModelArrowView.Length.get -> int",
    ];

    private static IReadOnlyList<string> Surface(string members)
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            "using Arrow.SourceGenerator;\nnamespace Demo;\n[ArrowSerializable] public partial class Model\n{\n"
                + members
                + "\n}"
        );
        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();
        return GeneratedApiSurface.Lines(outcome.GeneratedSources.Single().SourceText.ToString());
    }

    private static string Normalize(string line) =>
        line.Replace("Demo.ModelArrow.", "", StringComparison.Ordinal)
            .Replace("Demo.", "", StringComparison.Ordinal);

    [Fact]
    public void CompanionExposesExactlyTheBudgetedOperations()
    {
        IReadOnlyList<string> surface = Surface("public int A { get; set; }");

        surface
            .Where(line => line != "static Demo.ModelArrow")
            .Select(Normalize)
            .Where(line => line.StartsWith("static ", StringComparison.Ordinal))
            .ShouldBe(CompanionOperations, ignoreOrder: true);
    }

    [Fact]
    public void ViewExposesItsFixedMembersPlusOneArrayPerFieldAndNoConstructor()
    {
        IReadOnlyList<string> surface = Surface(
            "public int A { get; set; } public string? B { get; set; }"
        );

        var view = surface
            .Where(line =>
                line.StartsWith("Demo.ModelArrowView", StringComparison.Ordinal)
                || line.EndsWith("Demo.ModelArrowView", StringComparison.Ordinal)
            )
            .Select(line => line.Replace("Demo.", "", StringComparison.Ordinal))
            .ToList();

        view.ShouldBe(
            [
                "ModelArrowView.A.get -> Apache.Arrow.Int32Array",
                "ModelArrowView.B.get -> Apache.Arrow.StringArray",
                .. ViewFixedMembers,
                "sealed ModelArrowView",
            ],
            ignoreOrder: true
        );
    }

    [Fact]
    public void OperationCountDoesNotGrowWithFieldCount()
    {
        string wide = string.Join(
            "\n",
            Enumerable
                .Range(0, 40)
                .Select(i =>
                    $"public int? F{i} {{ get; set; }} public string S{i} {{ get; set; }} = \"\";"
                )
        );

        IReadOnlyList<string> narrow = Surface("public int A { get; set; }");
        IReadOnlyList<string> broad = Surface(wide);

        broad.Count(IsCompanionOperation).ShouldBe(narrow.Count(IsCompanionOperation));
    }

    [Fact]
    public void NoSignatureLeaksEmitterSlotsOrHelpers()
    {
        IReadOnlyList<string> surface = Surface(
            "public int A { get; set; } public string? B { get; set; }"
        );

        surface.Where(line => SlotLikeName().IsMatch(line)).ShouldBeEmpty();
        surface
            .Where(line => line.Contains("Create", StringComparison.Ordinal))
            .ShouldBeEmpty("construction helpers are implementation detail");
    }

    private static bool IsCompanionOperation(string line) =>
        line.StartsWith("static Demo.ModelArrow.", StringComparison.Ordinal);

    [GeneratedRegex(@"(_\d+\b|\bslot|\bcolumn_?\d|\bfield_?\d|__)", RegexOptions.IgnoreCase)]
    private static partial Regex SlotLikeName();
}
