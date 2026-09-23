namespace Arrow.SourceGenerator.Model;

/// <summary>
/// The compile-time meaning of one <c>[ArrowSerializable]</c> type, in C# terms only.
/// </summary>
/// <remarks>
/// Nothing here mentions Arrow. The parser records what the C# model <i>is</i>; Arrow planning
/// decides what it becomes (docs/00-DESIGN-GOALS.md section 11). No Roslyn symbol, syntax node,
/// compilation or <see cref="Microsoft.CodeAnalysis.Location"/> is reachable from this record, so
/// it is safe to cache and compares by value.
/// </remarks>
internal sealed record TargetModel(
    string Namespace,
    string Name,
    string FullyQualifiedName,
    string HintIdentity,
    TargetKind Kind,
    string Accessibility,
    EquatableArray<ContainingTypeModel> ContainingTypes,
    EquatableArray<MemberModel> Members,
    ConstructionModel Construction
)
{
    /// <summary>The companion type's simple name: <c>{Name}Arrow</c>.</summary>
    public string CompanionName => Name + "Arrow";

    public bool IsValueType => Kind is TargetKind.Struct or TargetKind.RecordStruct;
}

/// <summary>The declaration form of a target or containing type.</summary>
internal enum TargetKind
{
    Class,
    Struct,
    RecordClass,
    RecordStruct,

    /// <summary>Only ever a containing type: an interface can hold a nested target.</summary>
    Interface,
}

/// <summary>
/// A type the target is nested in. The companion is emitted inside the same containers, so each
/// is re-opened as a <c>partial</c> declaration.
/// </summary>
internal sealed record ContainingTypeModel(string Name, TargetKind Kind);

/// <summary>One mapped member, in field order.</summary>
internal sealed record MemberModel(
    string Name,
    string FieldName,
    TypeRef Type,
    bool IsNullable,
    bool IsAssignable,
    bool IsRequired,
    MemberAnnotations Annotations
);

/// <summary>
/// Member-level annotations that refine planning, recorded as written. Validation belongs to
/// planning, which knows what the member's type maps to.
/// </summary>
internal sealed record MemberAnnotations(int? DecimalPrecision, int? DecimalScale)
{
    public static MemberAnnotations None { get; } = new(null, null);

    public bool HasDecimal => DecimalPrecision.HasValue;
}

/// <summary>
/// Where a member is declared, kept beside the model rather than inside it so that moving a
/// declaration does not invalidate emission (docs/00-DESIGN-GOALS.md section 10).
/// </summary>
internal sealed record MemberSite(string MemberName, LocationInfo? Location);

/// <summary>
/// A member's CLR type, stripped of nullability (<see cref="MemberModel.IsNullable"/> carries it).
/// </summary>
/// <param name="FullyQualifiedName">
/// <c>global::</c>-qualified C# spelling of the non-nullable type, e.g. <c>global::System.Guid</c>.
/// </param>
/// <param name="Kind">The built-in the type is, or <see cref="ClrTypeKind.Other"/>.</param>
/// <param name="EnumUnderlying">For an enum, its underlying integral kind.</param>
/// <param name="IsValueType">Whether the type is a value type.</param>
internal sealed record TypeRef(
    string FullyQualifiedName,
    ClrTypeKind Kind,
    ClrTypeKind EnumUnderlying,
    bool IsValueType
);

/// <summary>The CLR built-ins the parser recognises. Arrow meaning is assigned by planning.</summary>
internal enum ClrTypeKind
{
    Other,
    Boolean,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Decimal,
    String,
    ByteArray,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
    TimeSpan,
    Guid,
    Enum,
    Char,
}

/// <summary>How the read path constructs an instance.</summary>
/// <param name="ConstructorParameters">
/// Member names bound to constructor parameters, in parameter order. Empty means the
/// parameterless constructor. Every other member is assigned in an object initializer.
/// </param>
internal sealed record ConstructionModel(EquatableArray<string> ConstructorParameters)
{
    public static ConstructionModel Parameterless { get; } = new(EquatableArray<string>.Empty);
}
