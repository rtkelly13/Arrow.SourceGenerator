using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Arrow.SourceGenerator.Model;

/// <summary>
/// A value-equatable stand-in for <see cref="Location"/>. A <see cref="Location"/> holds its
/// syntax tree, which would root the whole compilation in cached pipeline state.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location)
    {
        if (location is null || location.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span
        );
    }

    public static LocationInfo? From(SyntaxNode? node) => From(node?.GetLocation());
}
