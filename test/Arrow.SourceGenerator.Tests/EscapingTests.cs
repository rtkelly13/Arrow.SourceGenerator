using Arrow.SourceGenerator.Emit;
using Arrow.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// User-controlled strings reach generated source only through <see cref="SourceNames"/>.
/// Parquet.SourceGenerator shipped several source-injection defects from raw interpolation; each
/// form here is tested with the characters that caused them.
/// </summary>
public sealed class EscapingTests
{
    public static TheoryData<string> HostileStrings() =>
        [
            "plain",
            "with \"quotes\"",
            "back\\slash",
            "new\nline",
            "carriage\r\nreturn",
            "<xml & 'chars'>",
            "*/ comment terminator /*",
            "unicode é中ß",
            "\0 nul",
        ];

    [Theory]
    [MemberData(nameof(HostileStrings))]
    public void StringLiteralRoundTripsThroughTheCompiler(string value)
    {
        string literal = SourceNames.StringLiteral(value);
        var tree = CSharpSyntaxTree.ParseText($"class C {{ const string S = {literal}; }}");
        tree.GetDiagnostics().ShouldBeEmpty();
        var token = tree.GetRoot()
            .DescendantTokens()
            .Single(t => t.IsKind(SyntaxKind.StringLiteralToken));
        token.ValueText.ShouldBe(value);
    }

    [Theory]
    [MemberData(nameof(HostileStrings))]
    public void CommentAndXmlTextStayOnOneLineAndParse(string value)
    {
        string comment = SourceNames.CommentText(value);
        string xml = SourceNames.XmlText(value);
        comment.ShouldNotContain('\n');
        xml.ShouldNotContain('\n');
        xml.ShouldNotContain('<');

        var tree = CSharpSyntaxTree.ParseText(
            $"// {comment}\n/// <summary>{xml}</summary>\nclass C {{ }}",
            new CSharpParseOptions(
                documentationMode: Microsoft.CodeAnalysis.DocumentationMode.Diagnose
            )
        );
        tree.GetDiagnostics().ShouldBeEmpty();
        tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Count()
            .ShouldBe(1);
    }

    [Theory]
    [InlineData("class", "@class")]
    [InlineData("event", "@event")]
    [InlineData("Order", "Order")]
    [InlineData("var", "var")]
    public void IdentifiersEscapeReservedKeywordsOnly(string name, string expected) =>
        SourceNames.Identifier(name).ShouldBe(expected);

    [Fact]
    public void HintNamesKeepDistinctIdentitiesDistinct()
    {
        SourceNames.HintName("A+BC", ".g.cs").ShouldNotBe(SourceNames.HintName("AB+C", ".g.cs"));
        SourceNames.HintName("N.é", ".g.cs").ShouldBe("N.-00e9.g.cs");
    }

    [Fact]
    public void KeywordNamedTargetsAndNamespacesCompile()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            using Arrow.SourceGenerator;
            namespace @namespace.@class;

            public partial class @event
            {
                [ArrowSerializable]
                public partial class @record { public int @int { get; set; } }
            }
            """
        );

        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();
        outcome
            .GeneratedSources.Single()
            .HintName.ShouldBe("namespace.class.event+record.Arrow.g.cs");
    }
}
