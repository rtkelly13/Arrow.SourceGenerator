using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arrow.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Renders the externally reachable API of one generated source file as a signature-only
/// baseline (<c>.api.txt</c>) and a compact shape summary (<c>.api.shape.txt</c>).
/// </summary>
/// <remarks>
/// <para>The golden <c>.g.cs</c> holds the full emitted body, so a new public overload and a
/// retuned loop produce diffs of the same visual shape. The generated surface also lives in no
/// shipped assembly, so no API-diff tool can see it. The baseline makes it reviewable on its own
/// (docs/00-DESIGN-GOALS.md section 29).</para>
/// <para>The grammar follows <c>PublicAPI.Shipped.txt</c>: one member per line, each line carrying
/// its fully qualified container; <c>-&gt;</c> introduces the type; property accessors get one line
/// each; API-relevant modifiers are rendered in a fixed order. <c>global::</c> is stripped. Lines
/// are ordinal-sorted and de-duplicated, so the output is byte-identical across machines.</para>
/// <para>Derived from syntax, not symbols: the output is produced before it is compiled against a
/// consumer, and a syntax walk cannot disagree with the text the reviewer reads.</para>
/// </remarks>
internal static class GeneratedApiSurface
{
    public const string NullableHeader = "#nullable enable";
    public const string ShapeHeader = "# generated API shape summary";

    private static readonly string[] ApiModifiers =
    [
        "const",
        "static",
        "readonly",
        "required",
        "abstract",
        "virtual",
        "override",
        "sealed",
    ];

    /// <summary>Renders the signature-only baseline.</summary>
    public static string CreateBaseline(string emittedSource)
    {
        Surface surface = Walk(emittedSource);
        var builder = new StringBuilder();
        builder.Append(NullableHeader).Append('\n');
        foreach (string line in surface.Lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders the shape summary: externally visible types, members (every baseline line that is
    /// not a type) and parameter slots across callable members. These are the API budget numbers.
    /// </summary>
    public static string CreateShapeSummary(string emittedSource)
    {
        Surface surface = Walk(emittedSource);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ShapeHeader}\nTYPES={surface.Types} MEMBERS={surface.Lines.Count - surface.Types} PARAMETERS={surface.Parameters}\n"
        );
    }

    /// <summary>The rendered baseline lines, without the header, for budget assertions.</summary>
    public static IReadOnlyList<string> Lines(string emittedSource) => Walk(emittedSource).Lines;

    private static Surface Walk(string emittedSource)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree
            .ParseText(emittedSource)
            .GetCompilationUnitRoot();
        var walker = new Walker();
        foreach (MemberDeclarationSyntax member in root.Members)
        {
            walker.VisitContainerMember(member, container: null);
        }

        var lines = walker.Lines.Distinct(StringComparer.Ordinal).ToList();
        lines.Sort(StringComparer.Ordinal);
        return new Surface(lines, walker.TypeNames.Count, walker.Parameters);
    }

    private sealed record Surface(IReadOnlyList<string> Lines, int Types, int Parameters);

    private sealed class Walker
    {
        public List<string> Lines { get; } = [];
        public HashSet<string> TypeNames { get; } = new(StringComparer.Ordinal);
        public int Parameters { get; private set; }

        public void VisitContainerMember(MemberDeclarationSyntax member, string? container)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    string name = container is null
                        ? ns.Name.ToString()
                        : container + "." + ns.Name;
                    foreach (MemberDeclarationSyntax child in ns.Members)
                    {
                        VisitContainerMember(child, name);
                    }

                    break;

                case BaseTypeDeclarationSyntax type when IsVisible(type.Modifiers, owner: null):
                    VisitType(type, container);
                    break;

                case DelegateDeclarationSyntax del when IsVisible(del.Modifiers, owner: null):
                    string delegateName = Qualify(container, del.Identifier.Text);
                    TypeNames.Add(delegateName);
                    Lines.Add(delegateName);
                    Parameters += del.ParameterList.Parameters.Count;
                    break;
            }
        }

        private void VisitType(BaseTypeDeclarationSyntax type, string? container)
        {
            string typeName = Qualify(container, type.Identifier.Text + TypeParameters(type));
            TypeNames.Add(typeName);
            Lines.Add(Modifiers(type.Modifiers) + typeName);

            if (type is EnumDeclarationSyntax enumDeclaration)
            {
                foreach (EnumMemberDeclarationSyntax enumMember in enumDeclaration.Members)
                {
                    string value = enumMember.EqualsValue is null
                        ? ""
                        : " = " + enumMember.EqualsValue.Value;
                    Lines.Add($"{typeName}.{enumMember.Identifier.Text}{value} -> {typeName}");
                }

                return;
            }

            if (type is TypeDeclarationSyntax { ParameterList: { } primary } withPrimary)
            {
                Lines.Add(
                    $"{typeName}.{withPrimary.Identifier.Text}({Parameters_(primary)}) -> void"
                );
            }

            if (type is not TypeDeclarationSyntax declaration)
            {
                return;
            }

            foreach (MemberDeclarationSyntax member in declaration.Members)
            {
                VisitMember(member, typeName, declaration);
            }
        }

        private void VisitMember(
            MemberDeclarationSyntax member,
            string typeName,
            TypeDeclarationSyntax owner
        )
        {
            switch (member)
            {
                case BaseTypeDeclarationSyntax nested when IsVisible(nested.Modifiers, owner):
                    VisitType(nested, typeName);
                    break;

                case MethodDeclarationSyntax method when IsVisible(method.Modifiers, owner):
                    Lines.Add(
                        $"{Modifiers(method.Modifiers)}{typeName}.{method.Identifier.Text}{method.TypeParameterList}({Parameters_(method.ParameterList)}) -> {Type(method.ReturnType)}"
                    );
                    break;

                case ConstructorDeclarationSyntax ctor
                    when IsVisible(ctor.Modifiers, owner)
                        && !ctor.Modifiers.Any(SyntaxKind.StaticKeyword):
                    Lines.Add(
                        $"{typeName}.{ctor.Identifier.Text}({Parameters_(ctor.ParameterList)}) -> void"
                    );
                    break;

                case PropertyDeclarationSyntax property when IsVisible(property.Modifiers, owner):
                    VisitAccessors(
                        property.Modifiers,
                        $"{typeName}.{property.Identifier.Text}",
                        property.Type,
                        property.AccessorList,
                        property.ExpressionBody is not null
                    );
                    break;

                case IndexerDeclarationSyntax indexer when IsVisible(indexer.Modifiers, owner):
                    Parameters += indexer.ParameterList.Parameters.Count;
                    VisitAccessors(
                        indexer.Modifiers,
                        $"{typeName}.this[{string.Join(", ", indexer.ParameterList.Parameters.Select(Parameter))}]",
                        indexer.Type,
                        indexer.AccessorList,
                        indexer.ExpressionBody is not null
                    );
                    break;

                case FieldDeclarationSyntax field when IsVisible(field.Modifiers, owner):
                    foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                    {
                        string value =
                            field.Modifiers.Any(SyntaxKind.ConstKeyword)
                            && variable.Initializer is not null
                                ? " = " + variable.Initializer.Value
                                : "";
                        Lines.Add(
                            $"{Modifiers(field.Modifiers)}{typeName}.{variable.Identifier.Text}{value} -> {Type(field.Declaration.Type)}"
                        );
                    }

                    break;

                case OperatorDeclarationSyntax op when IsVisible(op.Modifiers, owner):
                    Lines.Add(
                        $"{Modifiers(op.Modifiers)}{typeName}.operator {op.OperatorToken.Text}({Parameters_(op.ParameterList)}) -> {Type(op.ReturnType)}"
                    );
                    break;

                case ConversionOperatorDeclarationSyntax conversion
                    when IsVisible(conversion.Modifiers, owner):
                    Lines.Add(
                        $"{Modifiers(conversion.Modifiers)}{typeName}.{conversion.ImplicitOrExplicitKeyword.Text} operator {Type(conversion.Type)}({Parameters_(conversion.ParameterList)}) -> {Type(conversion.Type)}"
                    );
                    break;
            }
        }

        private void VisitAccessors(
            SyntaxTokenList modifiers,
            string memberName,
            TypeSyntax type,
            AccessorListSyntax? accessors,
            bool expressionBodied
        )
        {
            string prefix = Modifiers(modifiers);
            if (expressionBodied || accessors is null)
            {
                Lines.Add($"{prefix}{memberName}.get -> {Type(type)}");
                return;
            }

            foreach (AccessorDeclarationSyntax accessor in accessors.Accessors)
            {
                if (IsHidden(accessor.Modifiers))
                {
                    continue;
                }

                string keyword = accessor.Keyword.Text;
                string result = keyword == "get" ? Type(type) : "void";
                Lines.Add($"{prefix}{memberName}.{keyword} -> {result}");
            }
        }

        private string Parameters_(ParameterListSyntax parameters)
        {
            Parameters += parameters.Parameters.Count;
            return string.Join(", ", parameters.Parameters.Select(Parameter));
        }
    }

    private static string Parameter(ParameterSyntax parameter)
    {
        var builder = new StringBuilder();
        foreach (SyntaxToken modifier in parameter.Modifiers)
        {
            builder.Append(modifier.Text).Append(' ');
        }

        if (parameter.Type is not null)
        {
            builder.Append(Type(parameter.Type)).Append(' ');
        }

        builder.Append(parameter.Identifier.Text);
        if (parameter.Default is not null)
        {
            builder.Append(" = ").Append(parameter.Default.Value);
        }

        return builder.ToString();
    }

    private static string Type(TypeSyntax type) =>
        type.WithoutTrivia().ToString().Replace("global::", "", StringComparison.Ordinal);

    private static string TypeParameters(BaseTypeDeclarationSyntax type) =>
        type is TypeDeclarationSyntax { TypeParameterList: { } list } ? list.ToString() : "";

    private static string Qualify(string? container, string name) =>
        container is null ? name : container + "." + name;

    private static string Modifiers(SyntaxTokenList modifiers)
    {
        var builder = new StringBuilder();
        foreach (string modifier in ApiModifiers)
        {
            if (modifiers.Any(token => token.Text == modifier))
            {
                builder.Append(modifier).Append(' ');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether a declaration is reachable from outside its assembly. Members of an interface are
    /// implicitly public; everything else needs <c>public</c> or <c>protected</c> (a top-level type
    /// with no modifier is internal).
    /// </summary>
    private static bool IsVisible(SyntaxTokenList modifiers, SyntaxNode? owner)
    {
        if (owner is InterfaceDeclarationSyntax && !IsHidden(modifiers))
        {
            return true;
        }

        bool isPublic = modifiers.Any(SyntaxKind.PublicKeyword);
        bool isProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        bool isPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);
        return isPublic || (isProtected && !isPrivate);
    }

    private static bool IsHidden(SyntaxTokenList modifiers) =>
        modifiers.Any(SyntaxKind.PrivateKeyword)
        || (
            modifiers.Any(SyntaxKind.InternalKeyword) && !modifiers.Any(SyntaxKind.ProtectedKeyword)
        );
}
