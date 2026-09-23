using System.Collections.Generic;
using System.Linq;
using Arrow.SourceGenerator.Diagnostics;
using Arrow.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace Arrow.SourceGenerator.Parsing;

/// <summary>
/// Chooses how the generated reader constructs an instance, so that positional records,
/// init-only classes and plain settable classes all materialise without reflection.
/// </summary>
/// <remarks>
/// A constructor is usable when it is reachable from generated code and every parameter binds to
/// a distinct mapped member (name compared case-insensitively, type exactly), and every mapped
/// member it leaves unbound is assignable. Required members always go through the object
/// initializer, never a parameter. The usable constructor with the most parameters wins; a tie is
/// ambiguous and reported. A struct's implicit parameterless constructor always participates.
/// </remarks>
internal static class ConstructorBinder
{
    public static ConstructionModel? Bind(
        INamedTypeSymbol type,
        IReadOnlyList<CollectedMember> members,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics
    )
    {
        var usable = new List<(IMethodSymbol Constructor, List<string> Bound)>();
        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (!MemberCollector.IsReachable(constructor))
            {
                continue;
            }

            List<string>? bound = TryBind(constructor, members);
            if (bound is not null)
            {
                usable.Add((constructor, bound));
            }
        }

        if (usable.Count == 0)
        {
            ReportUnusable(type, members, location, diagnostics);
            return null;
        }

        int most = usable.Max(candidate => candidate.Bound.Count);
        var best = usable.Where(candidate => candidate.Bound.Count == most).ToList();
        if (best.Count > 1)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.AmbiguousConstructor,
                    location,
                    type.Name,
                    most.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            );
            return null;
        }

        return new ConstructionModel(best[0].Bound.ToEquatableArray());
    }

    private static List<string>? TryBind(
        IMethodSymbol constructor,
        IReadOnlyList<CollectedMember> members
    )
    {
        var bound = new List<string>();
        foreach (IParameterSymbol parameter in constructor.Parameters)
        {
            CollectedMember? match = members.FirstOrDefault(member =>
                !member.Model.IsRequired
                && string.Equals(
                    member.Model.Name,
                    parameter.Name,
                    System.StringComparison.OrdinalIgnoreCase
                )
                && SymbolEqualityComparer.Default.Equals(member.Symbol.Type, parameter.Type)
                && !bound.Contains(member.Model.Name)
            );
            if (match is null)
            {
                return null;
            }

            bound.Add(match.Model.Name);
        }

        bool restAssignable = members.All(member =>
            bound.Contains(member.Model.Name) || member.Model.IsAssignable
        );
        return restAssignable ? bound : null;
    }

    private static void ReportUnusable(
        INamedTypeSymbol type,
        IReadOnlyList<CollectedMember> members,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics
    )
    {
        bool hasParameterless = type.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && MemberCollector.IsReachable(c)
        );
        if (!hasParameterless)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.NoUsableConstructor,
                    location,
                    type.Name
                )
            );
            return;
        }

        foreach (CollectedMember member in members.Where(m => !m.Model.IsAssignable))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.MemberNotMaterialisable,
                    LocationInfo.From(member.Symbol.Locations.FirstOrDefault()),
                    member.Model.Name,
                    type.Name
                )
            );
        }
    }
}
