using System.Collections.Generic;
using System.Linq;
using Arrow.SourceGenerator.Diagnostics;
using Arrow.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arrow.SourceGenerator.Parsing;

/// <summary>A mapped member plus the symbol it came from. Lives only for the duration of a parse.</summary>
internal sealed record CollectedMember(MemberModel Model, IPropertySymbol Symbol);

/// <summary>
/// Decides which members of a target map to Arrow fields, in which order, under which names.
/// </summary>
/// <remarks>
/// Mapped: public instance, non-indexer properties with a public getter, including inherited ones
/// (base members first). An override or <c>new</c> redeclaration replaces the base member in place.
/// Excluded: <c>[ArrowIgnore]</c> members. Public instance fields are never mapped and are reported
/// (ARROW016) rather than silently skipped.
/// </remarks>
internal static class MemberCollector
{
    public static IReadOnlyList<CollectedMember> Collect(
        INamedTypeSymbol type,
        List<DiagnosticInfo> diagnostics
    )
    {
        var positional = PositionalParameters(type);
        var slots = new List<IPropertySymbol>();
        var slotByName = new Dictionary<string, int>(System.StringComparer.Ordinal);
        var ignoredRequired = new List<ISymbol>();

        foreach (INamedTypeSymbol level in HierarchyBaseFirst(type))
        {
            foreach (ISymbol member in level.GetMembers())
            {
                switch (member)
                {
                    case IPropertySymbol property when IsCandidate(property):
                        if (IsIgnored(property, positional))
                        {
                            RemoveSlot(property.Name, slots, slotByName);
                            if (property.IsRequired)
                            {
                                ignoredRequired.Add(property);
                            }

                            continue;
                        }

                        if (slotByName.TryGetValue(property.Name, out int index))
                        {
                            slots[index] = property;
                        }
                        else
                        {
                            slotByName[property.Name] = slots.Count;
                            slots.Add(property);
                        }

                        break;

                    case IPropertySymbol { IsRequired: true, IsStatic: false } unmappedProperty:
                        ignoredRequired.Add(unmappedProperty);
                        break;

                    // A required field of any accessibility must be initialised, and the generated
                    // reader never maps fields, so it is always reported (ARROW018) below.
                    case IFieldSymbol { IsRequired: true, IsStatic: false } requiredField:
                        ignoredRequired.Add(requiredField);
                        break;

                    case IFieldSymbol field
                        when !field.IsStatic
                            && !field.IsConst
                            && !field.IsImplicitlyDeclared
                            && field.DeclaredAccessibility == Accessibility.Public:
                        if (!HasAttribute(field, AttributeNames.Ignore))
                        {
                            diagnostics.Add(
                                DiagnosticInfo.Create(
                                    DiagnosticDescriptors.PublicFieldNotMapped,
                                    LocationInfo.From(field.Locations.FirstOrDefault()),
                                    field.Name,
                                    type.Name
                                )
                            );
                        }

                        break;
                }
            }
        }

        foreach (ISymbol required in ignoredRequired)
        {
            if (!slotByName.ContainsKey(required.Name))
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.RequiredMemberNotMapped,
                        LocationInfo.From(required.Locations.FirstOrDefault()),
                        required.Name,
                        type.Name
                    )
                );
            }
        }

        var collected = slots
            .Select(
                (property, declarationIndex) =>
                    (
                        Property: property,
                        Column: ReadColumn(property, positional),
                        DeclarationIndex: declarationIndex
                    )
            )
            .OrderBy(item => item.Column.Order.HasValue ? 0 : 1)
            .ThenBy(item => item.Column.Order ?? 0)
            .ThenBy(item => item.DeclarationIndex)
            .Select(item => new CollectedMember(
                CreateModel(
                    item.Property,
                    item.Column.Name ?? item.Property.Name,
                    ReadAnnotations(item.Property, positional, type, diagnostics)
                ),
                item.Property
            ))
            .ToList();

        ValidateFieldNames(collected, diagnostics);
        return collected;
    }

    private static List<INamedTypeSymbol> HierarchyBaseFirst(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (
            INamedTypeSymbol? current = type;
            current is not null
                && current.SpecialType
                    is not SpecialType.System_Object
                        and not SpecialType.System_ValueType;
            current = current.BaseType
        )
        {
            chain.Insert(0, current);
        }

        return chain;
    }

    private static bool IsCandidate(IPropertySymbol property) =>
        !property.IsStatic
        && !property.IsIndexer
        && property.DeclaredAccessibility == Accessibility.Public
        && property.GetMethod is { DeclaredAccessibility: Accessibility.Public };

    private static void RemoveSlot(
        string name,
        List<IPropertySymbol> slots,
        Dictionary<string, int> slotByName
    )
    {
        if (!slotByName.TryGetValue(name, out int index))
        {
            return;
        }

        slots.RemoveAt(index);
        slotByName.Remove(name);
        foreach (string key in slotByName.Keys.ToList())
        {
            if (slotByName[key] > index)
            {
                slotByName[key]--;
            }
        }
    }

    private static MemberModel CreateModel(
        IPropertySymbol property,
        string fieldName,
        MemberAnnotations annotations
    )
    {
        (TypeRef type, bool nullable) = TypeClassifier.Classify(
            property.Type,
            property.NullableAnnotation
        );

        return new MemberModel(
            Name: property.Name,
            FieldName: fieldName,
            Type: type,
            IsNullable: nullable,
            IsAssignable: property.SetMethod is { } setter && IsReachable(setter),
            IsRequired: property.IsRequired,
            Annotations: annotations
        );
    }

    private static MemberAnnotations ReadAnnotations(
        IPropertySymbol property,
        Dictionary<string, IParameterSymbol> positional,
        INamedTypeSymbol target,
        List<DiagnosticInfo> diagnostics
    )
    {
        AdapterModel? adapter = ReadAdapter(property, positional, target, diagnostics);
        AttributeData? decimalAttribute =
            FindAttribute(property, AttributeNames.Decimal)
            ?? (
                positional.TryGetValue(property.Name, out IParameterSymbol? parameter)
                    ? FindAttribute(parameter, AttributeNames.Decimal)
                    : null
            );
        if (decimalAttribute is not { ConstructorArguments.Length: 2 } found)
        {
            return adapter is null
                ? MemberAnnotations.None
                : new MemberAnnotations(null, null, adapter);
        }

        return new MemberAnnotations(
            found.ConstructorArguments[0].Value as int?,
            found.ConstructorArguments[1].Value as int?,
            adapter
        );
    }

    /// <summary>
    /// Resolves and validates an explicit <c>[ArrowAdapter]</c>. Its domain type must be exactly
    /// the member's type (nullability aside: nulls never reach an adapter).
    /// </summary>
    private static AdapterModel? ReadAdapter(
        IPropertySymbol property,
        Dictionary<string, IParameterSymbol> positional,
        INamedTypeSymbol target,
        List<DiagnosticInfo> diagnostics
    )
    {
        AttributeData? attribute =
            FindAttribute(property, AttributeNames.Adapter)
            ?? (
                positional.TryGetValue(property.Name, out IParameterSymbol? parameter)
                    ? FindAttribute(parameter, AttributeNames.Adapter)
                    : null
            );
        if (
            attribute is not { ConstructorArguments.Length: 1 }
            || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol adapterType
        )
        {
            return null;
        }

        LocationInfo? location = LocationInfo.From(property.Locations.FirstOrDefault());
        bool external = !SymbolEqualityComparer.Default.Equals(
            adapterType.ContainingAssembly,
            target.ContainingAssembly
        );
        (AdapterModel? adapter, string? problem) = AdapterAnalyzer.Analyze(adapterType, external);
        if (adapter is not null)
        {
            (TypeRef memberType, _) = TypeClassifier.Classify(
                property.Type,
                property.NullableAnnotation
            );
            if (memberType.FullyQualifiedName != adapter.DomainType)
            {
                problem =
                    $"it converts '{adapter.DomainType.Replace("global::", "")}', but member '{property.Name}' is '{memberType.FullyQualifiedName.Replace("global::", "")}'";
                adapter = null;
            }
        }

        if (adapter is null)
        {
            diagnostics.Add(
                DiagnosticInfo.Create(
                    DiagnosticDescriptors.InvalidAdapter,
                    location,
                    adapterType.Name,
                    problem!
                )
            );
        }

        return adapter;
    }

    /// <summary>Accessible from generated code in the same assembly but outside the type.</summary>
    internal static bool IsReachable(ISymbol symbol) =>
        symbol.DeclaredAccessibility
            is Accessibility.Public
                or Accessibility.Internal
                or Accessibility.ProtectedOrInternal;

    private static void ValidateFieldNames(
        IReadOnlyList<CollectedMember> members,
        List<DiagnosticInfo> diagnostics
    )
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (CollectedMember member in members)
        {
            string? problem =
                member.Model.FieldName.Length == 0 ? "is empty"
                : !seen.Add(member.Model.FieldName) ? "is already used by another member"
                : null;
            if (problem is not null)
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.InvalidFieldName,
                        LocationInfo.From(member.Symbol.Locations.FirstOrDefault()),
                        member.Model.Name,
                        member.Model.FieldName,
                        problem
                    )
                );
            }
        }
    }

    private static (string? Name, int? Order) ReadColumn(
        IPropertySymbol property,
        Dictionary<string, IParameterSymbol> positional
    )
    {
        AttributeData? column =
            FindAttribute(property, AttributeNames.Column)
            ?? (
                positional.TryGetValue(property.Name, out IParameterSymbol? parameter)
                    ? FindAttribute(parameter, AttributeNames.Column)
                    : null
            );
        if (column is null)
        {
            return (null, null);
        }

        string? name =
            column.ConstructorArguments.Length == 1
                ? column.ConstructorArguments[0].Value as string ?? ""
                : null;
        int? order = column
            .NamedArguments.Where(pair => pair.Key == "Order")
            .Select(pair => pair.Value.Value as int?)
            .FirstOrDefault();
        return (name, order);
    }

    private static bool IsIgnored(
        IPropertySymbol property,
        Dictionary<string, IParameterSymbol> positional
    ) =>
        HasAttribute(property, AttributeNames.Ignore)
        || (
            positional.TryGetValue(property.Name, out IParameterSymbol? parameter)
            && HasAttribute(parameter, AttributeNames.Ignore)
        );

    /// <summary>
    /// Primary-constructor parameters of a positional record, by name. Attributes written in
    /// parameter position (<c>record R([ArrowColumn("id")] int Id)</c>) land on the parameter, so
    /// they are read from here too.
    /// </summary>
    private static Dictionary<string, IParameterSymbol> PositionalParameters(INamedTypeSymbol type)
    {
        var result = new Dictionary<string, IParameterSymbol>(System.StringComparer.Ordinal);
        if (!type.IsRecord)
        {
            return result;
        }

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            bool isPrimary = constructor.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is RecordDeclarationSyntax { ParameterList: not null }
            );
            if (!isPrimary)
            {
                continue;
            }

            foreach (IParameterSymbol parameter in constructor.Parameters)
            {
                result[parameter.Name] = parameter;
            }
        }

        return result;
    }

    private static bool HasAttribute(ISymbol symbol, string fullName) =>
        FindAttribute(symbol, fullName) is not null;

    /// <summary>
    /// The attribute on <paramref name="symbol"/>, or for a property override the nearest one on
    /// the property it overrides. The Arrow member attributes are <c>Inherited = true</c>, but
    /// Roslyn's <c>GetAttributes()</c> returns only those written on the symbol itself, and member
    /// collection keeps the override in place of the base declaration.
    /// </summary>
    private static AttributeData? FindAttribute(ISymbol symbol, string fullName)
    {
        for (
            ISymbol? current = symbol;
            current is not null;
            current = (current as IPropertySymbol)?.OverriddenProperty
        )
        {
            AttributeData? found = current
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fullName);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
