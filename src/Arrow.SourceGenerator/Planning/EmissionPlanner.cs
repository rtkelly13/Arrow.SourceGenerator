using System.Collections.Generic;
using System.Linq;
using Arrow.SourceGenerator.Diagnostics;
using Arrow.SourceGenerator.Emit;
using Arrow.SourceGenerator.Model;
using Arrow.SourceGenerator.Parsing;

namespace Arrow.SourceGenerator.Planning;

/// <summary>
/// Resolves a parsed model into its one <see cref="EmissionPlan"/>. Every member either gets a
/// complete Arrow plan or a diagnostic; a model with any unplannable member emits nothing, because
/// a partial mapping would only surface its gap at runtime.
/// </summary>
internal static class EmissionPlanner
{
    public static PlanResult Plan(ParseResult parse, bool arrowReferenced)
    {
        TargetModel model = parse.Model!;
        var diagnostics = new List<DiagnosticInfo>();

        if (!arrowReferenced)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.ApacheArrowNotReferenced,
                    parse.TargetLocation,
                    model.Name
                )
            );
            return new PlanResult(null, diagnostics.ToEquatableArray());
        }

        var fields = new List<FieldPlan>(model.Members.Count);
        for (int i = 0; i < model.Members.Count; i++)
        {
            MemberModel member = model.Members[i];
            LocationInfo? site = parse
                .Sites.FirstOrDefault(s => s.MemberName == member.Name)
                ?.Location;

            if (System.Array.IndexOf(TypedViewEmitter.ReservedMemberNames, member.Name) >= 0)
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.ReservedMemberName,
                        site,
                        member.Name,
                        model.Name,
                        model.Name + "ArrowView"
                    )
                );
                continue;
            }

            if (
                !TryResolveDecimal(
                    member,
                    model,
                    site,
                    diagnostics,
                    out int precision,
                    out int scale
                )
            )
            {
                continue;
            }

            ArrowLeafPlan? leaf = ArrowMappingTable.TryMap(member.Type, precision, scale);
            if (leaf is null)
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.UnsupportedMemberType,
                        site,
                        member.Name,
                        model.Name,
                        member.Type.FullyQualifiedName.Replace("global::", "")
                    )
                );
                continue;
            }

            fields.Add(
                new FieldPlan(
                    Ordinal: fields.Count,
                    MemberName: member.Name,
                    MemberIdentifier: SourceNames.Identifier(member.Name),
                    FieldName: member.FieldName,
                    FieldNameLiteral: SourceNames.StringLiteral(member.FieldName),
                    ClrType: member.Type.FullyQualifiedName,
                    IsValueType: member.Type.IsValueType,
                    Leaf: leaf,
                    Nulls: !member.IsNullable ? NullStrategy.Required
                        : member.Type.IsValueType ? NullStrategy.NullableValue
                        : NullStrategy.NullableReference
                )
            );
        }

        EmissionPlan? plan = diagnostics.Any(d => d.IsError)
            ? null
            : new EmissionPlan(model, fields.ToEquatableArray());
        return new PlanResult(plan, diagnostics.ToEquatableArray());
    }

    private static bool TryResolveDecimal(
        MemberModel member,
        TargetModel model,
        LocationInfo? site,
        List<DiagnosticInfo> diagnostics,
        out int precision,
        out int scale
    )
    {
        precision = ArrowMappingTable.DefaultDecimalPrecision;
        scale = ArrowMappingTable.DefaultDecimalScale;
        if (!member.Annotations.HasDecimal)
        {
            return true;
        }

        string? problem;
        if (member.Type.Kind != ClrTypeKind.Decimal)
        {
            problem = "[ArrowDecimal] applies only to decimal members";
        }
        else
        {
            precision = member.Annotations.DecimalPrecision ?? 0;
            scale = member.Annotations.DecimalScale ?? -1;
            problem =
                precision is < 1 or > ArrowMappingTable.MaxDecimal128Precision
                    ? $"precision {precision} is outside 1..{ArrowMappingTable.MaxDecimal128Precision}"
                : scale < 0 || scale > precision
                    ? $"scale {scale} is outside 0..{precision} (the precision)"
                : scale > ArrowMappingTable.MaxClrDecimalScale
                    ? $"scale {scale} exceeds {ArrowMappingTable.MaxClrDecimalScale}, the largest scale System.Decimal can represent"
                : null;
        }

        if (problem is null)
        {
            return true;
        }

        diagnostics.Add(
            DiagnosticInfo.Create(
                DiagnosticDescriptors.InvalidDecimal,
                site,
                member.Name,
                model.Name,
                problem
            )
        );
        return false;
    }
}
