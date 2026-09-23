using Arrow.SourceGenerator.Model;
using Arrow.SourceGenerator.Parsing;
using Arrow.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// The parsed model records C# meaning: names, order, nullability, CLR kind, construction.
/// </summary>
public sealed class ParserTests
{
    private static TargetModel Parse(string source, string typeName = "Demo.Target")
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation([
            "using Arrow.SourceGenerator;\nnamespace Demo;\n" + source,
        ]);
        INamedTypeSymbol symbol = compilation.GetTypeByMetadataName(typeName)!;
        ParseResult result = TargetParser.Parse(symbol, CancellationToken.None);
        result.Diagnostics.Where(d => d.IsError).ShouldBeEmpty();
        return result.Model.ShouldNotBeNull();
    }

    [Fact]
    public void MembersFollowDeclarationOrderWithBaseMembersFirst()
    {
        TargetModel model = Parse(
            """
            public class Base { public int B1 { get; set; } public virtual int Shared { get; set; } }
            [ArrowSerializable]
            public partial class Target : Base
            {
                public int T1 { get; set; }
                public override int Shared { get; set; }
                public int T2 { get; set; }
            }
            """
        );

        model.Members.Select(m => m.Name).ShouldBe(["B1", "Shared", "T1", "T2"]);
    }

    [Fact]
    public void ExplicitOrderComesFirstThenDeclarationOrder()
    {
        TargetModel model = Parse(
            """
            [ArrowSerializable]
            public partial class Target
            {
                public int A { get; set; }
                [ArrowColumn(Order = 2)] public int B { get; set; }
                public int C { get; set; }
                [ArrowColumn(Order = 1)] public int D { get; set; }
            }
            """
        );

        model.Members.Select(m => m.Name).ShouldBe(["D", "B", "A", "C"]);
    }

    [Fact]
    public void NullabilityFollowsAnnotationsAndObliviousReferencesAreNullable()
    {
        TargetModel model = Parse(
            """
            [ArrowSerializable]
            public partial class Target
            {
                public int Required { get; set; }
                public int? Optional { get; set; }
                public string Name { get; set; } = "";
                public string? Nickname { get; set; }
            #nullable disable
                public string Oblivious { get; set; }
            #nullable restore
            }
            """
        );

        model
            .Members.Select(m => (m.Name, m.IsNullable))
            .ShouldBe([
                ("Required", false),
                ("Optional", true),
                ("Name", false),
                ("Nickname", true),
                ("Oblivious", true),
            ]);
        model.Members.Single(m => m.Name == "Optional").Type.Kind.ShouldBe(ClrTypeKind.Int32);
    }

    [Fact]
    public void BuiltInKindsAreRecognised()
    {
        TargetModel model = Parse(
            """
            public enum Status : short { A, B }
            [ArrowSerializable]
            public partial class Target
            {
                public bool A { get; set; }
                public byte[] B { get; set; } = [];
                public System.DateTimeOffset C { get; set; }
                public System.DateOnly D { get; set; }
                public System.TimeOnly E { get; set; }
                public System.TimeSpan F { get; set; }
                public System.Guid G { get; set; }
                public Status H { get; set; }
                public System.Collections.Generic.List<int> I { get; set; } = [];
            }
            """
        );

        model
            .Members.Select(m => m.Type.Kind)
            .ShouldBe([
                ClrTypeKind.Boolean,
                ClrTypeKind.ByteArray,
                ClrTypeKind.DateTimeOffset,
                ClrTypeKind.DateOnly,
                ClrTypeKind.TimeOnly,
                ClrTypeKind.TimeSpan,
                ClrTypeKind.Guid,
                ClrTypeKind.Enum,
                ClrTypeKind.Other,
            ]);
        model.Members.Single(m => m.Name == "H").Type.EnumUnderlying.ShouldBe(ClrTypeKind.Int16);
        model
            .Members.Single(m => m.Name == "H")
            .Type.FullyQualifiedName.ShouldBe("global::Demo.Status");
    }

    [Fact]
    public void PositionalRecordReadsAttributesWrittenInParameterPosition()
    {
        TargetModel model = Parse(
            """
            [ArrowSerializable]
            public partial record Target([ArrowColumn("id")] long Id, string? Customer);
            """
        );

        model
            .Members.Select(m => (m.Name, m.FieldName))
            .ShouldBe([("Id", "id"), ("Customer", "Customer")]);
        model.Construction.ConstructorParameters.ShouldBe(["Id", "Customer"]);
    }

    [Fact]
    public void IgnoringAPositionalParameterLeavesNoCallableConstructor()
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation([
            "using Arrow.SourceGenerator;\nnamespace Demo;\n"
                + "[ArrowSerializable] public partial record Target(long Id, [ArrowIgnore] int Skipped);",
        ]);
        ParseResult result = TargetParser.Parse(
            compilation.GetTypeByMetadataName("Demo.Target")!,
            CancellationToken.None
        );

        result.Model.ShouldBeNull();
        result.Diagnostics.Select(d => d.Descriptor.Id).ShouldBe(["ARROW015"]);
    }

    [Fact]
    public void PositionalRecordWithAllParametersMappedUsesTheConstructor()
    {
        TargetModel model = Parse(
            "[ArrowSerializable] public partial record Target(long Id, string? Customer, decimal Total);"
        );

        model.Construction.ConstructorParameters.ShouldBe(["Id", "Customer", "Total"]);
        model.Kind.ShouldBe(TargetKind.RecordClass);
    }

    [Fact]
    public void InitOnlyAndRequiredMembersAreAssignableThroughTheInitializer()
    {
        TargetModel model = Parse(
            """
            [ArrowSerializable]
            public partial class Target
            {
                public required int A { get; init; }
                public int B { get; init; }
            }
            """
        );

        model.Members.ShouldAllBe(m => m.IsAssignable);
        model.Members[0].IsRequired.ShouldBeTrue();
        model.Construction.ShouldBe(ConstructionModel.Parameterless);
    }

    [Fact]
    public void ModelEqualityIsByValue()
    {
        const string source =
            "[ArrowSerializable] public partial class Target { public int A { get; set; } }";
        Parse(source).ShouldBe(Parse(source));
    }
}
