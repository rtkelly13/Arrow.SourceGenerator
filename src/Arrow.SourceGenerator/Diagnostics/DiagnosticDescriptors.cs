using Microsoft.CodeAnalysis;

namespace Arrow.SourceGenerator.Diagnostics;

/// <summary>
/// Every diagnostic the generator reports. Identifiers are permanent: a retired rule keeps its
/// number, and new rules take the next free one. Release tracking lives in
/// <c>AnalyzerReleases.Shipped.md</c> / <c>AnalyzerReleases.Unshipped.md</c> (RS2008).
/// </summary>
/// <remarks>
/// ARROW004 (nested cycle), ARROW006 (nullability/schema mismatch) and ARROW007 (unsupported
/// configured capability) are reserved for the capabilities that will need them
/// (docs/00-DESIGN-GOALS.md section 25).
/// </remarks>
internal static class DiagnosticDescriptors
{
    private const string Category = "Arrow.SourceGenerator";

    public static readonly DiagnosticDescriptor MustBePartial = Error(
        "ARROW008",
        "Arrow target must be partial",
        "'{0}' is marked [ArrowSerializable] and must be declared partial"
    );

    public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = Error(
        "ARROW009",
        "Containing type of an Arrow target must be partial",
        "'{0}' contains the [ArrowSerializable] type '{1}' and must be declared partial so the generated companion can be nested beside it"
    );

    public static readonly DiagnosticDescriptor UnsupportedTargetShape = Error(
        "ARROW010",
        "Unsupported Arrow target",
        "'{0}' cannot be an [ArrowSerializable] target: {1}"
    );

    public static readonly DiagnosticDescriptor CompanionNameCollision = Error(
        "ARROW011",
        "Generated companion name is already taken",
        "The generated companion for '{0}' is named '{1}', but a member with that name already exists in the same scope"
    );

    public static readonly DiagnosticDescriptor NoMappedMembers = Warning(
        "ARROW012",
        "Arrow target has no mapped members",
        "'{0}' has no public instance properties to map to Arrow fields"
    );

    public static readonly DiagnosticDescriptor InvalidFieldName = Error(
        "ARROW013",
        "Invalid or duplicate Arrow field name",
        "Member '{0}' maps to the Arrow field name '{1}', which {2}"
    );

    public static readonly DiagnosticDescriptor MemberNotMaterialisable = Error(
        "ARROW014",
        "Member cannot be assigned when reading",
        "Member '{0}' of '{1}' has no accessible setter or init accessor and no matching constructor parameter; add one or mark it [ArrowIgnore]"
    );

    public static readonly DiagnosticDescriptor NoUsableConstructor = Error(
        "ARROW015",
        "No usable constructor",
        "'{0}' has no accessible constructor the generated reader can call: provide a parameterless constructor, or one whose parameters all match mapped members by name and type"
    );

    public static readonly DiagnosticDescriptor PublicFieldNotMapped = Warning(
        "ARROW016",
        "Public field is not mapped",
        "Public field '{0}' of '{1}' is not mapped; only properties become Arrow fields. Make it a property, or mark it [ArrowIgnore] to silence this warning."
    );

    public static readonly DiagnosticDescriptor RequiredMemberNotMapped = Error(
        "ARROW018",
        "Required member is not mapped",
        "Required member '{0}' of '{1}' is not mapped to an Arrow field, so the generated reader cannot satisfy it"
    );

    public static readonly DiagnosticDescriptor AmbiguousConstructor = Error(
        "ARROW019",
        "Ambiguous constructor",
        "'{0}' has more than one constructor with {1} parameters that bind to mapped members; the generated reader cannot choose between them"
    );

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static DiagnosticDescriptor Warning(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
