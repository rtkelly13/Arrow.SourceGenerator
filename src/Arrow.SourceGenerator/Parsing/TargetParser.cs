using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Arrow.SourceGenerator.Diagnostics;
using Arrow.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arrow.SourceGenerator.Parsing;

/// <summary>What parsing one target produced: a model when it can be emitted, and diagnostics.</summary>
/// <remarks>
/// <see cref="Sites"/> and <see cref="TargetLocation"/> carry locations for diagnostics raised by
/// later stages; they sit beside the model, never inside it.
/// </remarks>
internal sealed record ParseResult(
    TargetModel? Model,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<MemberSite> Sites,
    LocationInfo? TargetLocation
);

/// <summary>
/// Turns an <c>[ArrowSerializable]</c> type symbol into a <see cref="TargetModel"/>, reporting every
/// declaration-level problem as a diagnostic. Roslyn objects are read here and nowhere later.
/// </summary>
internal static class TargetParser
{
    private static readonly SymbolDisplayFormat NamespaceFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
    );

    /// <summary>
    /// Pipeline entry point. Returns <see langword="null"/> for every declaration of a type except
    /// the one that owns its <c>[ArrowSerializable]</c> application, so a type split across
    /// partial declarations yields exactly one result (the Parquet.SourceGenerator #368 failure).
    /// </summary>
    public static ParseResult? Parse(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || !OwnsAttribute(context, type))
        {
            return null;
        }

        return Parse(type, cancellationToken);
    }

    /// <summary>Parses one type symbol. Also the seam unit tests call directly.</summary>
    public static ParseResult Parse(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();
        LocationInfo? location = LocationInfo.From(IdentifierLocation(type));

        bool shapeOk = ValidateShape(type, location, diagnostics);
        EquatableArray<ContainingTypeModel> containing = ContainingTypes(
            type,
            diagnostics,
            ref shapeOk
        );

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<CollectedMember> members = MemberCollector.Collect(type, diagnostics);
        ConstructionModel? construction = shapeOk
            ? ConstructorBinder.Bind(type, members, location, diagnostics)
            : null;

        if (members.Count == 0 && !diagnostics.Any(d => d.IsError))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.NoMappedMembers, location, type.Name)
            );
        }

        TargetModel? model =
            construction is null || diagnostics.Any(d => d.IsError)
                ? null
                : new TargetModel(
                    Namespace: type.ContainingNamespace.IsGlobalNamespace
                        ? ""
                        : type.ContainingNamespace.ToDisplayString(NamespaceFormat),
                    Name: type.Name,
                    FullyQualifiedName: type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    ),
                    HintIdentity: HintIdentity(type),
                    Kind: KindOf(type),
                    Accessibility: CompanionAccessibility(type),
                    ContainingTypes: containing,
                    Members: members.Select(m => m.Model).ToEquatableArray(),
                    Construction: construction
                );

        EquatableArray<MemberSite> sites = members
            .Select(m => new MemberSite(
                m.Model.Name,
                LocationInfo.From(m.Symbol.Locations.FirstOrDefault())
            ))
            .ToEquatableArray();

        return new ParseResult(model, diagnostics.ToEquatableArray(), sites, location);
    }

    private static bool OwnsAttribute(
        GeneratorAttributeSyntaxContext context,
        INamedTypeSymbol type
    )
    {
        AttributeData? first = type.GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == AttributeNames.Serializable
                && a.ApplicationSyntaxReference is not null
            );
        if (first?.ApplicationSyntaxReference is not { } owner)
        {
            return true;
        }

        return context.Attributes.Any(a =>
            a.ApplicationSyntaxReference is { } reference
            && reference.SyntaxTree == owner.SyntaxTree
            && reference.Span == owner.Span
        );
    }

    private static bool ValidateShape(
        INamedTypeSymbol type,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics
    )
    {
        string? problem = type switch
        {
            { TypeParameters.Length: > 0 } => "generic types are not supported",
            { IsFileLocal: true } => "file-local types cannot be named from generated code",
            { IsRefLikeType: true } => "ref structs cannot be stored in collections",
            { IsStatic: true } => "static classes have no instances to map",
            { IsAbstract: true } => "abstract types cannot be constructed by the generated reader",
            _ => null,
        };

        if (problem is not null)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.UnsupportedTargetShape,
                    location,
                    type.Name,
                    problem
                )
            );
            return false;
        }

        if (!IsPartial(type))
        {
            diagnostics.Add(
                DiagnosticInfo.Create(DiagnosticDescriptors.MustBePartial, location, type.Name)
            );
            return false;
        }

        ISymbol scope = (ISymbol?)type.ContainingType ?? type.ContainingNamespace;
        string companion = type.Name + "Arrow";
        // Only a non-generic declaration can conflict: C# lets OrderArrow and OrderArrow<T> share a
        // declaration space, so a generic namesake is no collision with the arity-zero companion.
        IEnumerable<ISymbol> namesakes = scope switch
        {
            INamespaceSymbol ns => ns.GetMembers(companion),
            INamedTypeSymbol owner => owner.GetMembers(companion),
            _ => Enumerable.Empty<ISymbol>(),
        };
        bool taken = namesakes.Any(symbol => symbol is not INamedTypeSymbol { Arity: > 0 });
        if (taken)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.CompanionNameCollision,
                    location,
                    type.Name,
                    companion
                )
            );
            return false;
        }

        return true;
    }

    private static EquatableArray<ContainingTypeModel> ContainingTypes(
        INamedTypeSymbol type,
        List<DiagnosticInfo> diagnostics,
        ref bool shapeOk
    )
    {
        var chain = new List<ContainingTypeModel>();
        for (
            INamedTypeSymbol? outer = type.ContainingType;
            outer is not null;
            outer = outer.ContainingType
        )
        {
            LocationInfo? location = LocationInfo.From(IdentifierLocation(outer));
            if (outer.TypeParameters.Length > 0)
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.UnsupportedTargetShape,
                        LocationInfo.From(IdentifierLocation(type)),
                        type.Name,
                        $"its containing type '{outer.Name}' is generic, which is not supported"
                    )
                );
                shapeOk = false;
            }
            else if (!IsPartial(outer))
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.ContainingTypeMustBePartial,
                        location,
                        outer.Name,
                        type.Name
                    )
                );
                shapeOk = false;
            }

            chain.Insert(0, new ContainingTypeModel(outer.Name, KindOf(outer)));
        }

        return chain.ToEquatableArray();
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
        );

    private static Location? IdentifierLocation(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Select(d => d.Identifier.GetLocation())
            .FirstOrDefault()
        ?? type.Locations.FirstOrDefault();

    private static TargetKind KindOf(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Interface
            ? TargetKind.Interface
            : (type.IsRecord, type.IsValueType) switch
            {
                (true, true) => TargetKind.RecordStruct,
                (true, false) => TargetKind.RecordClass,
                (false, true) => TargetKind.Struct,
                _ => TargetKind.Class,
            };

    /// <summary>
    /// A top-level companion is as visible as the target can be outside its assembly; a nested
    /// companion mirrors the target's declared accessibility exactly, which is always legal because
    /// the companion lives in the same containing type.
    /// </summary>
    private static string CompanionAccessibility(INamedTypeSymbol type)
    {
        if (type.ContainingType is null)
        {
            return type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        }

        return type.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "private",
        };
    }

    /// <summary>
    /// Stable, collision-free identity for the hint name: namespace plus the metadata names of the
    /// type and its containers joined with <c>+</c>, exactly as the CLR names nested types. Two
    /// distinct types can never share it, which a flattened name (<c>A.BC</c> vs <c>AB.C</c>) could.
    /// </summary>
    private static string HintIdentity(INamedTypeSymbol type)
    {
        var parts = new List<string>();
        for (
            INamedTypeSymbol? current = type;
            current is not null;
            current = current.ContainingType
        )
        {
            parts.Insert(0, current.MetadataName);
        }

        for (
            INamespaceSymbol ns = type.ContainingNamespace;
            !ns.IsGlobalNamespace;
            ns = ns.ContainingNamespace
        )
        {
            parts.Insert(0, ns.MetadataName + ".");
        }

        // Namespace segments carry their trailing '.', type segments are joined by '+'.
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            bool previousIsType =
                i > 0 && !parts[i - 1].EndsWith(".", System.StringComparison.Ordinal);
            builder.Append(previousIsType ? "+" : "").Append(parts[i]);
        }

        return builder.ToString();
    }
}
