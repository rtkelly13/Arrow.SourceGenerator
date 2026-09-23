using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// The only path from model strings to generated source. Every emitter goes through one of these
/// forms; none interpolates raw member or field names (docs/00-DESIGN-GOALS.md section 24).
/// </summary>
internal static class SourceNames
{
    /// <summary>
    /// A C# identifier as written in source: <c>@</c>-prefixed when it is a reserved keyword.
    /// Symbol names reach the model without the verbatim prefix, so <c>@class</c> arrives as
    /// <c>class</c>.
    /// </summary>
    public static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>A dotted name (namespace) with each segment escaped.</summary>
    public static string DottedIdentifier(string dotted)
    {
        if (dotted.Length == 0)
        {
            return dotted;
        }

        string[] parts = dotted.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i].StartsWith("@", System.StringComparison.Ordinal)
                ? parts[i]
                : Identifier(parts[i]);
        }

        return string.Join(".", parts);
    }

    /// <summary>A quoted, fully escaped C# string literal.</summary>
    public static string StringLiteral(string value) =>
        SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>Text safe inside an XML documentation comment element.</summary>
    public static string XmlText(string value) =>
        SingleLine(value)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    /// <summary>
    /// Text safe inside a <c>//</c> comment: one line, so a newline in user data cannot end the
    /// comment and start code.
    /// </summary>
    public static string CommentText(string value) => SingleLine(value);

    /// <summary>
    /// A hint name for <c>AddSource</c> derived from a stable type identity. Characters outside
    /// the conservative set are replaced by their code point, which keeps distinct identities
    /// distinct.
    /// </summary>
    public static string HintName(string identity, string suffix)
    {
        var builder = new StringBuilder(identity.Length + suffix.Length);
        foreach (char c in identity)
        {
            if (
                c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_'
            )
            {
                builder.Append(c);
            }
            else if (c == '+')
            {
                builder.Append('+');
            }
            else
            {
                builder
                    .Append('-')
                    .Append(
                        ((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)
                    );
            }
        }

        return builder.Append(suffix).ToString();
    }

    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    private static string SingleLine(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            builder.Append(
                c is '\r' or '\n' or '\u0085' or LineSeparator or ParagraphSeparator ? ' ' : c
            );
        }

        return builder.ToString();
    }
}
