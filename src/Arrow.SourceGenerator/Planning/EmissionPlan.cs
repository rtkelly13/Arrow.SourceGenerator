using Arrow.SourceGenerator.Model;

namespace Arrow.SourceGenerator.Planning;

/// <summary>
/// The one immutable plan for a target. Schema, write, read and view emission all consume this
/// same plan, which is what stops them drifting apart (docs/00-DESIGN-GOALS.md section 23).
/// </summary>
internal sealed record EmissionPlan(TargetModel Target, EquatableArray<FieldPlan> Fields);

/// <summary>One Arrow field, fully resolved.</summary>
/// <param name="Ordinal">Position in the schema.</param>
/// <param name="MemberName">The C# member name as the model records it.</param>
/// <param name="MemberIdentifier">The member as written in source (keyword-escaped).</param>
/// <param name="FieldName">The Arrow field name.</param>
/// <param name="FieldNameLiteral">The field name as an escaped C# string literal.</param>
/// <param name="ClrType">
/// The non-nullable CLR type the Arrow conversion works on, <c>global::</c>-qualified: the member's
/// type, or the adapter's surrogate when <paramref name="Adapter"/> is set.
/// </param>
/// <param name="IsValueType">Whether <paramref name="ClrType"/> is a value type.</param>
/// <param name="Leaf">The resolved Arrow type and conversion.</param>
/// <param name="Nulls">How nulls are represented, judged on the member's own type.</param>
/// <param name="Adapter">The adapter the member is stored through, if any.</param>
internal sealed record FieldPlan(
    int Ordinal,
    string MemberName,
    string MemberIdentifier,
    string FieldName,
    string FieldNameLiteral,
    string ClrType,
    bool IsValueType,
    ArrowLeafPlan Leaf,
    NullStrategy Nulls,
    AdapterPlan? Adapter = null
)
{
    public bool IsNullable => Nulls != NullStrategy.Required;
}

/// <summary>A direct, statically bound call pair: <c>ToStorage</c> on write, <c>FromStorage</c> on read.</summary>
/// <param name="AdapterType">The adapter class, <c>global::</c>-qualified.</param>
/// <param name="DomainType">The member's non-nullable type, <c>global::</c>-qualified.</param>
/// <param name="DomainIsValueType">Whether <paramref name="DomainType"/> is a value type.</param>
internal sealed record AdapterPlan(string AdapterType, string DomainType, bool DomainIsValueType);

/// <summary>What planning produced: the plan when emission can proceed, and diagnostics.</summary>
internal sealed record PlanResult(EmissionPlan? Plan, EquatableArray<DiagnosticInfo> Diagnostics);
