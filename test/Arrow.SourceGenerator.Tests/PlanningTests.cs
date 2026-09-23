using Arrow.SourceGenerator.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>Planning diagnostics: unsupported types and invalid type parameters are compile errors.</summary>
public sealed class PlanningTests
{
    private static GeneratorOutcome Run(string members) =>
        GeneratorHarness.Run(
            "using Arrow.SourceGenerator;\nnamespace Demo;\n[ArrowSerializable] public partial class Target\n{\n"
                + members
                + "\n}"
        );

    [Theory]
    [InlineData("public char Letter { get; set; }", "char")]
    [InlineData(
        "public System.Collections.Generic.List<int> Items { get; set; } = new();",
        "System.Collections.Generic.List<int>"
    )]
    [InlineData("public object Anything { get; set; } = new();", "object")]
    [InlineData("public System.Uri Link { get; set; } = null!;", "System.Uri")]
    public void UnsupportedMemberTypesAreReportedAndNothingIsEmitted(string member, string typeName)
    {
        GeneratorOutcome outcome = Run(member);

        var diagnostic = outcome.GeneratorDiagnostics.Single();
        diagnostic.Id.ShouldBe("ARROW001");
        diagnostic
            .GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain($"'{typeName}'");
        outcome.GeneratedSources.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[ArrowDecimal(0, 0)] public decimal A { get; set; }", "precision 0")]
    [InlineData("[ArrowDecimal(39, 2)] public decimal A { get; set; }", "precision 39")]
    [InlineData("[ArrowDecimal(10, 11)] public decimal A { get; set; }", "scale 11")]
    [InlineData("[ArrowDecimal(38, 30)] public decimal A { get; set; }", "exceeds 28")]
    [InlineData("[ArrowDecimal(10, 2)] public int A { get; set; }", "only to decimal")]
    public void InvalidDecimalParametersAreReported(string member, string fragment)
    {
        GeneratorOutcome outcome = Run(member);

        var diagnostic = outcome.GeneratorDiagnostics.Single();
        diagnostic.Id.ShouldBe("ARROW002");
        diagnostic
            .GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain(fragment);
        outcome.GeneratedSources.ShouldBeEmpty();
    }

    [Fact]
    public void AnnotationsOnAnOverriddenBasePropertyAreInherited()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            public class Priced
            {
                [ArrowDecimal(10, 2)] public virtual decimal Price { get; set; }
                [ArrowColumn("sku")] public virtual string Code { get; set; } = "";
                [ArrowIgnore] public virtual int Internal { get; set; }
            }

            [ArrowSerializable]
            public partial class Item : Priced
            {
                public override decimal Price { get; set; }
                public override string Code { get; set; } = "";
                public override int Internal { get; set; }
            }
            """
        );

        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();
        string source = outcome.SourceFor(".Arrow.g.cs");
        source.ShouldContain(
            "new global::Apache.Arrow.Field(\"Price\", new global::Apache.Arrow.Types.Decimal128Type(10, 2)"
        );
        source.ShouldContain("new global::Apache.Arrow.Field(\"sku\",");
        source.ShouldNotContain("\"Internal\"");
    }

    [Fact]
    public void AnAnnotationOnTheOverrideWinsOverTheBase()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            public class Priced { [ArrowDecimal(10, 2)] public virtual decimal Price { get; set; } }

            [ArrowSerializable]
            public partial class Item : Priced
            {
                [ArrowDecimal(18, 4)] public override decimal Price { get; set; }
            }
            """
        );

        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome
            .SourceFor(".Arrow.g.cs")
            .ShouldContain("new global::Apache.Arrow.Types.Decimal128Type(18, 4)");
    }

    [Fact]
    public void IgnoringAnUnsupportedMemberResolvesTheError()
    {
        GeneratorOutcome outcome = Run(
            "public int Id { get; set; }\n[ArrowIgnore] public char Letter { get; set; }"
        );

        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();
    }

    [Fact]
    public void MissingApacheArrowReferenceIsReportedInsteadOfBrokenGeneratedCode()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            [
                "using Arrow.SourceGenerator;\n[ArrowSerializable] public partial class Target { public int Id { get; set; } }",
            ],
            GeneratorHarness.ReferencesWithoutArrow
        );

        outcome.GeneratorDiagnostics.Select(d => d.Id).ShouldBe(["ARROW020"]);
        outcome.GeneratedSources.ShouldBeEmpty();
    }
}
