using Arrow.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// Every declaration-level problem surfaces as a diagnostic at compile time, and a target with an
/// error emits nothing (the error explains the gap; generated code would only add noise).
/// </summary>
public sealed class DiagnosticTests
{
    private static GeneratorOutcome Run(string body) =>
        GeneratorHarness.Run("using Arrow.SourceGenerator;\nnamespace Demo;\n" + body);

    private static void ShouldReport(string body, string id, bool emits = false)
    {
        GeneratorOutcome outcome = Run(body);
        outcome.RunResult.Exception.ShouldBeNull();
        outcome.GeneratorDiagnostics.Select(d => d.Id).ShouldContain(id);
        // Diagnostics are rebuilt from cached LocationInfo, so they are file locations rather than
        // tree-bound ones; what matters is that they name the user's file and a real line.
        FileLinePositionSpan span = outcome
            .GeneratorDiagnostics.Single(d => d.Id == id)
            .Location.GetLineSpan();
        span.Path.ShouldStartWith("Source");
        span.StartLinePosition.Line.ShouldBeGreaterThanOrEqualTo(2);
        (outcome.GeneratedSources.Length > 0).ShouldBe(emits);
    }

    [Fact]
    public void NotPartial() =>
        ShouldReport(
            "[ArrowSerializable] public class Order { public int Id { get; set; } }",
            "ARROW008"
        );

    [Fact]
    public void ContainingTypeNotPartial() =>
        ShouldReport(
            "public class Outer { [ArrowSerializable] public partial class Inner { public int Id { get; set; } } }",
            "ARROW009"
        );

    [Fact]
    public void GenericContainingType() =>
        ShouldReport(
            "public partial class Outer<T> { [ArrowSerializable] public partial class Inner { public int Id { get; set; } } }",
            "ARROW010"
        );

    [Fact]
    public void FileLocalType() =>
        ShouldReport(
            "[ArrowSerializable] file partial class Local { public int Id { get; set; } }",
            "ARROW010"
        );

    [Fact]
    public void CompanionNameTaken() =>
        ShouldReport(
            "[ArrowSerializable] public partial class Order { public int Id { get; set; } }\npublic static class OrderArrow { }",
            "ARROW011"
        );

    [Fact]
    public void NoMappedMembersIsAWarningAndStillEmits() =>
        ShouldReport("[ArrowSerializable] public partial class Empty { }", "ARROW012", emits: true);

    [Fact]
    public void DuplicateFieldName() =>
        ShouldReport(
            """
            [ArrowSerializable]
            public partial class Order
            {
                [ArrowColumn("id")] public int A { get; set; }
                [ArrowColumn("id")] public int B { get; set; }
            }
            """,
            "ARROW013"
        );

    [Fact]
    public void EmptyFieldName() =>
        ShouldReport(
            "[ArrowSerializable] public partial class Order { [ArrowColumn(\"\")] public int A { get; set; } }",
            "ARROW013"
        );

    [Fact]
    public void GetOnlyPropertyWithoutConstructorParameter() =>
        ShouldReport(
            "[ArrowSerializable] public partial class Order { public int Id { get; } }",
            "ARROW014"
        );

    [Fact]
    public void PrivateSetterIsNotAssignable() =>
        ShouldReport(
            "[ArrowSerializable] public partial class Order { public int Id { get; private set; } }",
            "ARROW014"
        );

    [Fact]
    public void NoReachableConstructor() =>
        ShouldReport(
            "[ArrowSerializable] public partial class Order { private Order() { } public Order(string other) { } public int Id { get; set; } }",
            "ARROW015"
        );

    [Fact]
    public void PublicFieldIsReportedNotSilentlySkipped() =>
        ShouldReport(
            "[ArrowSerializable] public partial class Order { public int Id { get; set; } public int Count; }",
            "ARROW016",
            emits: true
        );

    [Fact]
    public void IgnoredPublicFieldIsSilent()
    {
        GeneratorOutcome outcome = Run(
            "[ArrowSerializable] public partial class Order { public int Id { get; set; } [ArrowIgnore] public int Count; }"
        );
        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();
    }

    [Fact]
    public void IgnoredRequiredMember() =>
        ShouldReport(
            "[ArrowSerializable] public partial class Order { [ArrowIgnore] public required int Id { get; set; } public int X { get; set; } }",
            "ARROW018"
        );

    [Fact]
    public void ConstructorsThatLeaveMembersUnassignableAreUnusable() =>
        ShouldReport(
            """
            [ArrowSerializable]
            public partial class Order
            {
                public Order(int a) { A = a; }
                public Order(long b) { B = b; }
                public int A { get; }
                public long B { get; }
            }
            """,
            "ARROW015"
        );

    [Fact]
    public void TwoEquallyGoodConstructorsAreAmbiguous() =>
        ShouldReport(
            """
            [ArrowSerializable]
            public partial class Order
            {
                public Order(int a) { A = a; }
                public Order(long b) { B = b; }
                public int A { get; set; }
                public long B { get; set; }
            }
            """,
            "ARROW019"
        );

    [Fact]
    public void DiagnosticDescriptorsHaveUniqueIdsAndAreReleaseTracked()
    {
        var descriptors = typeof(Diagnostics.DiagnosticDescriptors)
            .GetFields()
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToList();

        descriptors.Select(d => d.Id).Distinct().Count().ShouldBe(descriptors.Count);

        string unshipped = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Arrow.SourceGenerator",
                "AnalyzerReleases.Unshipped.md"
            )
        );
        foreach (DiagnosticDescriptor descriptor in descriptors)
        {
            unshipped.ShouldContain(descriptor.Id + " |");
        }
    }
}
