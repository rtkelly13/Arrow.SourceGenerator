using Arrow.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// Incrementality is a tested product property (docs/00-DESIGN-GOALS.md section 10): an edit
/// re-runs only the work it affects, and cached steps hold no Roslyn objects.
/// </summary>
public sealed class IncrementalityTests
{
    private const string ModelA = """
        using Arrow.SourceGenerator;
        namespace Demo;

        [ArrowSerializable]
        public partial class ModelA { public int Id { get; set; } }
        """;

    private const string ModelB = """
        using Arrow.SourceGenerator;
        namespace Demo;

        [ArrowSerializable]
        public partial class ModelB { public string Name { get; set; } = ""; }
        """;

    private const string Unrelated = """
        namespace Demo;
        internal static class Unrelated { public const int Value = 1; }
        """;

    private static (GeneratorDriver Driver, CSharpCompilation Compilation) FirstRun()
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation([
            ModelA,
            ModelB,
            Unrelated,
        ]);
        GeneratorDriver driver = GeneratorHarness
            .CreateDriver(trackSteps: true)
            .RunGenerators(compilation);
        return (driver, compilation);
    }

    private static GeneratorRunResult Rerun(
        GeneratorDriver driver,
        CSharpCompilation compilation,
        int treeIndex,
        string newText
    )
    {
        SyntaxTree old = compilation.SyntaxTrees.ElementAt(treeIndex);
        CSharpCompilation edited = compilation.ReplaceSyntaxTree(
            old,
            CSharpSyntaxTree.ParseText(newText, GeneratorHarness.ParseOptions, old.FilePath)
        );
        return driver.RunGenerators(edited).GetRunResult().Results.Single();
    }

    private static IEnumerable<IncrementalStepRunReason> Reasons(
        GeneratorRunResult result,
        string step
    ) => result.TrackedSteps[step].SelectMany(s => s.Outputs).Select(o => o.Reason);

    [Fact]
    public void AnUnrelatedEditLeavesEveryModelCached()
    {
        (GeneratorDriver driver, CSharpCompilation compilation) = FirstRun();
        GeneratorRunResult result = Rerun(driver, compilation, 2, Unrelated.Replace("= 1", "= 2"));

        Reasons(result, TrackingNames.EmissionPlan)
            .ShouldAllBe(r => r == IncrementalStepRunReason.Cached);
        result
            .TrackedOutputSteps.SelectMany(p => p.Value)
            .SelectMany(s => s.Outputs)
            .ShouldAllBe(o => o.Reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void EditingOneModelInvalidatesOnlyThatModel()
    {
        (GeneratorDriver driver, CSharpCompilation compilation) = FirstRun();
        GeneratorRunResult result = Rerun(
            driver,
            compilation,
            0,
            ModelA.Replace("public int Id", "public long Id")
        );

        var reasons = Reasons(result, TrackingNames.EmissionPlan).ToList();
        reasons.Count(r => r == IncrementalStepRunReason.Modified).ShouldBe(1);
        reasons
            .Count(r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)
            .ShouldBe(1);
    }

    /// <summary>
    /// Locations are kept out of the model, so shifting a declaration down a line changes its
    /// diagnostics' positions but leaves the emitted source untouched.
    /// </summary>
    [Fact]
    public void MovingADeclarationDoesNotChangeTheModel()
    {
        (GeneratorDriver driver, CSharpCompilation compilation) = FirstRun();
        GeneratorRunResult result = Rerun(driver, compilation, 0, "\n\n// moved\n" + ModelA);

        Reasons(result, TrackingNames.EmissionPlan)
            .ShouldAllBe(r =>
                r == IncrementalStepRunReason.Cached || r == IncrementalStepRunReason.Unchanged
            );
    }

    [Fact]
    public void CachedStepOutputsHoldNoRoslynObjects()
    {
        (GeneratorDriver driver, _) = FirstRun();
        GeneratorRunResult result = driver.GetRunResult().Results.Single();

        foreach (
            string step in new[]
            {
                TrackingNames.Parse,
                TrackingNames.EmissionPlan,
                TrackingNames.Diagnostics,
            }
        )
        {
            foreach (
                object value in result
                    .TrackedSteps[step]
                    .SelectMany(s => s.Outputs)
                    .Select(o => o.Value)
            )
            {
                RoslynObjectFinder.Find(value).ShouldBeNull($"step {step} caches a Roslyn object");
            }
        }
    }
}
