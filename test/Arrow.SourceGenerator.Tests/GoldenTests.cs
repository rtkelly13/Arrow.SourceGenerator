using Arrow.SourceGenerator.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// Golden-master suite (docs/00-DESIGN-GOALS.md sections 29-30). Each representative model yields
/// its generated source, its signature-only API baseline and its API shape summary. The generated
/// source must also compile cleanly against Apache.Arrow: a string comparison alone would accept a
/// golden file that does not build.
/// </summary>
public sealed class GoldenTests
{
    public static TheoryData<string> Models() => ["ScalarEvent", "KeywordAndEscaping"];

    [Theory]
    [MemberData(nameof(Models))]
    public void GeneratedSourceApiBaselineAndShapeMatchGoldenFiles(string model)
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(GoldenFile.ReadInput($"Models/{model}.cs"));

        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();

        string source = outcome.GeneratedSources.Single().SourceText.ToString();
        GoldenFile.AssertMatches($"{model}.Arrow.g.cs", source);
        GoldenFile.AssertMatches(
            $"{model}.Arrow.api.txt",
            GeneratedApiSurface.CreateBaseline(source)
        );
        GoldenFile.AssertMatches(
            $"{model}.Arrow.api.shape.txt",
            GeneratedApiSurface.CreateShapeSummary(source)
        );
    }
}
