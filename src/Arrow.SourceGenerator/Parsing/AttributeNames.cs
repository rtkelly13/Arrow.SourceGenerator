namespace Arrow.SourceGenerator.Parsing;

/// <summary>
/// Metadata names of the contract attributes. The generator matches by name and never binds to
/// the Attributes assembly, so it loads without it beside the analyzer.
/// </summary>
internal static class AttributeNames
{
    public const string Serializable = "Arrow.SourceGenerator.ArrowSerializableAttribute";
    public const string Column = "Arrow.SourceGenerator.ArrowColumnAttribute";
    public const string Ignore = "Arrow.SourceGenerator.ArrowIgnoreAttribute";
    public const string Decimal = "Arrow.SourceGenerator.ArrowDecimalAttribute";
    public const string Adapter = "Arrow.SourceGenerator.ArrowAdapterAttribute";
    public const string TypeAdapter = "Arrow.SourceGenerator.ArrowTypeAdapterAttribute";
    public const string AttributesAssembly = "Arrow.SourceGenerator.Attributes";
}
