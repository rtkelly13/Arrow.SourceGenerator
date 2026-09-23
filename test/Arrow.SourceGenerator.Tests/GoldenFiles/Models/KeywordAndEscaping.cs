using Arrow.SourceGenerator;

namespace Golden.@namespace;

/// <summary>Keywords, renamed fields, hostile field names, nesting, positional construction.</summary>
public partial class Outer
{
    [ArrowSerializable]
    public partial record KeywordAndEscaping(
        [ArrowColumn("quote\"back\\slash")] int @class,
        [ArrowColumn("new\nline <xml> & */")] string? @event,
        [ArrowColumn("unicode-é-中")] long Plain
    );
}
