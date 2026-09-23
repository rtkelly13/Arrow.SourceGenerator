using Arrow.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// Adapter discovery, precedence and the adapter contract, driven through the generator
/// (docs/04-ADAPTERS.md).
/// </summary>
public sealed class AdapterDiscoveryTests
{
    private const string Domain = """
        namespace Lib
        {
            public readonly record struct Money(decimal Amount);
        }
        """;

    private static string Adapter(
        string name,
        string surrogate = "decimal",
        string access = "public",
        string toBody = "value.Amount",
        string fromBody = "new(storage)"
    ) =>
        $$"""
            namespace Lib
            {
                {{access}} static class {{name}}
                {
                    public static {{surrogate}} ToStorage(Money value) => {{toBody}};
                    public static Money FromStorage({{surrogate}} storage) => {{fromBody}};
                }
            }
            """;

    private const string Target = """
        using Arrow.SourceGenerator;
        namespace App;

        [ArrowSerializable]
        public partial class Invoice
        {
            public Lib.Money Total { get; set; }
        }
        """;

    private static MetadataReference Library(params string[] sources)
    {
        CSharpCompilation library = GeneratorHarness.CreateCompilation(
            sources,
            assemblyName: "AdapterLibrary"
        );
        using var stream = new MemoryStream();
        var result = library.Emit(stream);
        result.Success.ShouldBeTrue(string.Join("\n", result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static GeneratorOutcome Run(
        IEnumerable<string> sources,
        params MetadataReference[] extra
    ) => GeneratorHarness.Run(sources, GeneratorHarness.References.AddRange(extra));

    private static string Emitted(GeneratorOutcome outcome)
    {
        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();
        return outcome.SourceFor(".Arrow.g.cs");
    }

    [Fact]
    public void RegistrationInTheCompilingAssemblyIsUsed()
    {
        string source = Emitted(
            Run([
                Domain,
                Adapter("MoneyAdapter"),
                "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.MoneyAdapter))]",
                Target,
            ])
        );

        source.ShouldContain("global::Lib.MoneyAdapter.ToStorage(");
        source.ShouldContain("global::Lib.MoneyAdapter.FromStorage(");
    }

    [Fact]
    public void RegistrationInAReferencedAssemblyIsUsed()
    {
        MetadataReference library = Library(
            Domain,
            Adapter("MoneyAdapter"),
            "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.MoneyAdapter))]"
        );

        Emitted(Run([Target], library)).ShouldContain("global::Lib.MoneyAdapter.ToStorage(");
    }

    [Fact]
    public void TheCompilingAssemblyOverridesAReferencedRegistration()
    {
        MetadataReference library = Library(
            Domain,
            Adapter("MoneyAdapter"),
            "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.MoneyAdapter))]"
        );
        string local = """
            namespace App
            {
                public static class LocalMoney
                {
                    public static string ToStorage(Lib.Money value) => value.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    public static Lib.Money FromStorage(string storage) => new(decimal.Parse(storage, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            """;

        string source = Emitted(
            Run(
                [
                    local,
                    "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(App.LocalMoney))]",
                    Target,
                ],
                library
            )
        );
        source.ShouldContain("global::App.LocalMoney.ToStorage(");
        source.ShouldNotContain("global::Lib.MoneyAdapter");
    }

    [Fact]
    public void AMemberAdapterOverridesEveryRegistration()
    {
        string target = """
            using Arrow.SourceGenerator;
            namespace App;

            [ArrowSerializable]
            public partial class Invoice
            {
                [ArrowAdapter(typeof(Lib.Explicit))]
                public Lib.Money Total { get; set; }
            }
            """;

        string source = Emitted(
            Run([
                Domain,
                Adapter("Registered"),
                Adapter(
                    "Explicit",
                    "double",
                    toBody: "(double)value.Amount",
                    fromBody: "new((decimal)storage)"
                ),
                "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.Registered))]",
                target,
            ])
        );
        source.ShouldContain("global::Lib.Explicit.ToStorage(");
        source.ShouldContain("DoubleType.Default");
    }

    [Fact]
    public void TwoRegistrationsAtTheSameTierAreAmbiguous()
    {
        GeneratorOutcome outcome = Run([
            Domain,
            Adapter("A"),
            Adapter("B"),
            "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.A))]",
            "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.B))]",
            Target,
        ]);

        var diagnostic = outcome.GeneratorDiagnostics.Single();
        diagnostic.Id.ShouldBe("ARROW005");
        diagnostic
            .GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("'Lib.A'");
        outcome.GeneratedSources.ShouldBeEmpty();
    }

    [Fact]
    public void ANonPublicAdapterInAReferencedAssemblyDoesNotParticipate()
    {
        MetadataReference library = Library(
            Domain,
            Adapter("MoneyAdapter", access: "internal"),
            "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.MoneyAdapter))]"
        );

        Run([Target], library).GeneratorDiagnostics.Select(d => d.Id).ShouldBe(["ARROW001"]);
    }

    [Fact]
    public void SurrogateMustBeABuiltIn()
    {
        GeneratorOutcome outcome = Run([
            Domain,
            Adapter(
                "ToList",
                "System.Collections.Generic.List<int>",
                toBody: "new()",
                fromBody: "default"
            ),
            "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.ToList))]",
            Target,
        ]);

        outcome.GeneratorDiagnostics.Select(d => d.Id).ShouldBe(["ARROW003"]);
    }

    public static TheoryData<string, string> InvalidAdapters() =>
        new()
        {
            {
                "public static class Bad { public static decimal ToStorage(Money v) => 0; }",
                "no accessible, non-generic 'static FromStorage(value)'"
            },
            {
                "public static class Bad { public static decimal ToStorage(Money v) => 0; public static Money FromStorage(double s) => default; }",
                "they must be inverses"
            },
            {
                "public static class Bad<T> { public static decimal ToStorage(Money v) => 0; public static Money FromStorage(decimal s) => default; }",
                "generic adapters are not supported"
            },
            {
                "public static class Bad { public static decimal? ToStorage(Money v) => 0; public static Money FromStorage(decimal? s) => default; }",
                "may be Nullable<T>"
            },
            {
                "public static class Bad { public static decimal ToStorage(Money v) => 0; public static decimal ToStorage(string v) => 0; public static Money FromStorage(decimal s) => default; }",
                "2 'ToStorage' overloads"
            },
        };

    [Theory]
    [MemberData(nameof(InvalidAdapters))]
    public void InvalidAdapterShapesAreReported(string adapter, string fragment)
    {
        GeneratorOutcome outcome = Run([
            Domain,
            "namespace Lib { " + adapter + " }",
            $"[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.{(adapter.Contains("Bad<T>", StringComparison.Ordinal) ? "Bad<>" : "Bad")}))]",
            Target,
        ]);

        Diagnostic invalid = outcome.GeneratorDiagnostics.First(d => d.Id == "ARROW017");
        invalid
            .GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain(fragment);
    }

    [Fact]
    public void RegisteringABuiltInTypeIsReported()
    {
        string adapter = """
            namespace Lib
            {
                public static class Ticks
                {
                    public static long ToStorage(System.DateTime v) => v.Ticks;
                    public static System.DateTime FromStorage(long s) => new(s);
                }
            }
            """;

        GeneratorOutcome outcome = Run([
            adapter,
            "[assembly: Arrow.SourceGenerator.ArrowTypeAdapter(typeof(Lib.Ticks))]",
        ]);
        outcome
            .GeneratorDiagnostics.Single()
            .GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("has a built-in Arrow mapping");
    }

    [Fact]
    public void AMemberAdapterForTheWrongDomainTypeIsReported()
    {
        string target = """
            using Arrow.SourceGenerator;
            namespace App;

            [ArrowSerializable]
            public partial class Invoice
            {
                [ArrowAdapter(typeof(Lib.MoneyAdapter))]
                public decimal Total { get; set; }
            }
            """;

        GeneratorOutcome outcome = Run([Domain, Adapter("MoneyAdapter"), target]);
        outcome
            .GeneratorDiagnostics.Single(d => d.Id == "ARROW017")
            .GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .ShouldContain("it converts 'Lib.Money', but member 'Total' is 'decimal'");
    }
}
