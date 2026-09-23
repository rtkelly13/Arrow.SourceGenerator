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
    public static PlanResult Plan(ParseResult parse, bool arrowReferenced) =>
        Plan(parse, arrowReferenced, AdapterRegistry.Empty);

    public static PlanResult Plan(ParseResult parse, bool arrowReferenced, AdapterRegistry registry)
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
                !TryResolveAdapter(
                    member,
                    model,
                    registry,
                    site,
                    diagnostics,
                    out AdapterModel? adapter
                )
            )
            {
                continue;
            }

            TypeRef storage = adapter?.Surrogate ?? member.Type;
            if (
                !TryResolveDecimal(
                    member,
                    storage,
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

            ArrowLeafPlan? leaf = ArrowMappingTable.TryMap(storage, precision, scale);
            if (leaf is null)
            {
                diagnostics.Add(
                    adapter is null
                        ? DiagnosticInfo.Create(
                            DiagnosticDescriptors.UnsupportedMemberType,
                            site,
                            member.Name,
                            model.Name,
                            Display(member.Type.FullyQualifiedName)
                        )
                        : DiagnosticInfo.Create(
                            DiagnosticDescriptors.UnsupportedSurrogate,
                            site,
                            Display(adapter.AdapterType),
                            member.Name,
                            Display(storage.FullyQualifiedName)
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
                    ClrType: storage.FullyQualifiedName,
                    IsValueType: storage.IsValueType,
                    Leaf: leaf,
                    Nulls: !member.IsNullable ? NullStrategy.Required
                        : member.Type.IsValueType ? NullStrategy.NullableValue
                        : NullStrategy.NullableReference,
                    Adapter: adapter is null
                        ? null
                        : new AdapterPlan(
                            adapter.AdapterType,
                            member.Type.FullyQualifiedName,
                            member.Type.IsValueType
                        )
                )
            );
        }

        EmissionPlan? plan = diagnostics.Any(d => d.IsError)
            ? null
            : new EmissionPlan(model, fields.ToEquatableArray());
        return new PlanResult(plan, diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// Adapter precedence (docs/04-ADAPTERS.md): the member's own <c>[ArrowAdapter]</c>; otherwise,
    /// for a type with no built-in mapping, a registration in the compiling assembly, then one in a
    /// referenced assembly. Two registrations at the deciding tier are ambiguous, never resolved by
    /// order.
    /// </summary>
    private static bool TryResolveAdapter(
        MemberModel member,
        TargetModel model,
        AdapterRegistry registry,
        LocationInfo? site,
        List<DiagnosticInfo> diagnostics,
        out AdapterModel? adapter
    )
    {
        adapter = member.Annotations.ExplicitAdapter;
        if (adapter is not null || ArrowMappingTable.HasBuiltIn(member.Type))
        {
            return true;
        }

        for (int tier = 0; tier <= 1; tier++)
        {
            var matches = registry
                .Registrations.Where(r =>
                    r.Tier == tier && r.Adapter.DomainType == member.Type.FullyQualifiedName
                )
                .ToList();
            if (matches.Count == 1)
            {
                adapter = matches[0].Adapter;
                return true;
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.AmbiguousAdapter,
                        site,
                        member.Name,
                        model.Name,
                        Display(member.Type.FullyQualifiedName),
                        string.Join(
                            ", ",
                            matches.Select(m => $"'{Display(m.Adapter.AdapterType)}' ({m.Source})")
                        )
                    )
                );
                return false;
            }
        }

        return true;
    }

    private static string Display(string fullyQualified) => fullyQualified.Replace("global::", "");

    private static bool TryResolveDecimal(
        MemberModel member,
        TypeRef storage,
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
        if (storage.Kind != ClrTypeKind.Decimal)
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
