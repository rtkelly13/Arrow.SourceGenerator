using Arrow.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// Target discovery: one output per type symbol, collision-safe identities, and supported shapes.
/// </summary>
public sealed class DiscoveryTests
{
    /// <summary>
    /// Regression for Parquet.SourceGenerator #368: a partial type whose other part carries an
    /// unrelated attribute produced a second output (and a CS8785 hint-name crash).
    /// </summary>
    [Fact]
    public void PartialTypeWithAnUnrelatedAttributeOnAnotherPartYieldsExactlyOneOutput()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            [ArrowSerializable]
            public partial class Order
            {
                public long Id { get; set; }
            }
            """,
            """
            namespace Demo;

            [System.Obsolete("unrelated")]
            public partial class Order
            {
                public string Name { get; set; } = "";
            }
            """
        );

        outcome.RunResult.Exception.ShouldBeNull();
        outcome.GeneratedSources.Length.ShouldBe(1);
        outcome.CompilationProblems.Where(d => d.Id != "CS0618").ShouldBeEmpty();
    }

    [Fact]
    public void AttributeAppliedOnTwoPartsStillYieldsOneOutputAndDoesNotCrash()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            [ArrowSerializable]
            public partial class Order { public long Id { get; set; } }
            """,
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            [ArrowSerializable]
            public partial class Order { public string Name { get; set; } = ""; }
            """
        );

        outcome.RunResult.Exception.ShouldBeNull();
        outcome.GeneratedSources.Length.ShouldBe(1);
        // The duplicate attribute is the compiler's to report (CS0579), not ours to crash on.
        outcome.CompilationProblems.Select(d => d.Id).ShouldBe(["CS0579"]);
    }

    [Fact]
    public void SameNameInTwoNamespacesProducesDistinctHintNames()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Sales { [ArrowSerializable] public partial class Order { public int Id { get; set; } } }
            namespace Billing { [ArrowSerializable] public partial class Order { public int Id { get; set; } } }
            """
        );

        outcome
            .GeneratedSources.Select(s => s.HintName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["Billing.Order.Arrow.g.cs", "Sales.Order.Arrow.g.cs"]);
        outcome.CompilationProblems.ShouldBeEmpty();
    }

    /// <summary>
    /// A flattened identity would map both <c>A.BC</c> and <c>AB.C</c> to <c>ABC</c>; the
    /// metadata identity keeps them apart.
    /// </summary>
    [Fact]
    public void NestedTypesUseCollisionSafeIdentities()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            public partial class A { [ArrowSerializable] public partial class BC { public int X { get; set; } } }
            public partial class AB { [ArrowSerializable] public partial class C { public int X { get; set; } } }
            """
        );

        outcome
            .GeneratedSources.Select(s => s.HintName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["Demo.A+BC.Arrow.g.cs", "Demo.AB+C.Arrow.g.cs"]);
        outcome.CompilationProblems.ShouldBeEmpty();
    }

    [Fact]
    public void NestedCompanionIsDeclaredBesideTheTargetWithItsAccessibility()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            public partial class Outer
            {
                [ArrowSerializable]
                private partial record struct Inner(int X);

                internal static System.Type Companion => typeof(InnerArrow);
            }
            """
        );

        outcome.CompilationProblems.ShouldBeEmpty();
        string source = outcome.SourceFor(".Arrow.g.cs");
        source.ShouldContain("partial class Outer");
        source.ShouldContain("private static partial class InnerArrow");
    }

    [Fact]
    public void GlobalNamespaceAndInternalTargetsCompile()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;

            [ArrowSerializable]
            internal partial struct Point { public int X { get; set; } }
            """
        );

        outcome.CompilationProblems.ShouldBeEmpty();
        outcome.GeneratedSources.Single().HintName.ShouldBe("Point.Arrow.g.cs");
        outcome.SourceFor(".Arrow.g.cs").ShouldContain("internal static partial class PointArrow");
    }

    [Fact]
    public void AnnotatedTypesWithoutTheAttributeAreIgnored()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            namespace Demo;

            [System.Serializable]
            public partial class NotATarget { public int X { get; set; } }
            """
        );

        outcome.GeneratedSources.ShouldBeEmpty();
    }

    [Fact]
    public void GeneratorNeverThrowsForAnyDiagnosedShape()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace Demo;

            [ArrowSerializable] public partial class Generic<T> { public T? Value { get; set; } }
            [ArrowSerializable] public static partial class Static { }
            [ArrowSerializable] public abstract partial class Abstract { public int X { get; set; } }
            [ArrowSerializable] public ref partial struct RefLike { public int X { get; set; } }
            [ArrowSerializable] public class NotPartial { public int X { get; set; } }
            """
        );

        outcome.RunResult.Exception.ShouldBeNull();
        outcome.GeneratedSources.ShouldBeEmpty();
        outcome
            .GeneratorDiagnostics.Select(d => d.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ShouldBe(["ARROW008", "ARROW010", "ARROW010", "ARROW010", "ARROW010"]);
    }
}
