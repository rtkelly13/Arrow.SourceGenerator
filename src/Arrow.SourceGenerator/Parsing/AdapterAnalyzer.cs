using System.Collections.Generic;
using System.Linq;
using Arrow.SourceGenerator.Diagnostics;
using Arrow.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace Arrow.SourceGenerator.Parsing;

/// <summary>
/// Validates an adapter type and extracts its <see cref="AdapterModel"/>. The generator owns the
/// whole adapter contract here — shape, accessibility, pairing, nullability — so a malformed
/// adapter is a compile-time diagnostic, never a generated call that fails to bind.
/// </summary>
internal static class AdapterAnalyzer
{
    /// <summary>Returns the model, or a human-readable reason it is not a valid adapter.</summary>
    /// <param name="adapter">The adapter class.</param>
    /// <param name="fromOtherAssembly">Whether generated code will call it across an assembly boundary.</param>
    public static (AdapterModel? Model, string? Problem) Analyze(
        INamedTypeSymbol adapter,
        bool fromOtherAssembly
    )
    {
        if (adapter.TypeParameters.Length > 0 || adapter.ContainingType?.IsGenericType == true)
        {
            return (null, "generic adapters are not supported");
        }

        if (!IsCallable(adapter, fromOtherAssembly))
        {
            return (
                null,
                fromOtherAssembly ? "it is not public" : "it is not accessible from generated code"
            );
        }

        (IMethodSymbol? toStorage, string? toProblem) = Single(
            adapter,
            "ToStorage",
            fromOtherAssembly
        );
        if (toStorage is null)
        {
            return (null, toProblem);
        }

        (IMethodSymbol? fromStorage, string? fromProblem) = Single(
            adapter,
            "FromStorage",
            fromOtherAssembly
        );
        if (fromStorage is null)
        {
            return (null, fromProblem);
        }

        ITypeSymbol domain = toStorage.Parameters[0].Type;
        ITypeSymbol surrogate = toStorage.ReturnType;
        if (
            !SymbolEqualityComparer.Default.Equals(fromStorage.ReturnType, domain)
            || !SymbolEqualityComparer.Default.Equals(fromStorage.Parameters[0].Type, surrogate)
        )
        {
            return (
                null,
                $"ToStorage converts '{Display(domain)}' to '{Display(surrogate)}' but FromStorage converts '{Display(fromStorage.Parameters[0].Type)}' to '{Display(fromStorage.ReturnType)}'; they must be inverses"
            );
        }

        if (IsNullableValue(domain) || IsNullableValue(surrogate))
        {
            return (
                null,
                "neither the domain nor the surrogate type may be Nullable<T>; the generator handles nulls before calling the adapter"
            );
        }

        (TypeRef surrogateRef, _) = TypeClassifier.Classify(
            surrogate,
            NullableAnnotation.NotAnnotated
        );
        (TypeRef domainRef, _) = TypeClassifier.Classify(domain, NullableAnnotation.NotAnnotated);
        return (
            new AdapterModel(
                adapter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                domainRef,
                surrogateRef
            ),
            null
        );
    }

    /// <summary>
    /// Reads every <c>[assembly: ArrowTypeAdapter]</c> in the compilation (tier 0) and in each
    /// referenced assembly that itself references the Attributes assembly (tier 1).
    /// </summary>
    public static AdapterRegistry ReadRegistry(Compilation compilation)
    {
        var registrations = new List<AdapterRegistration>();
        var diagnostics = new List<DiagnosticInfo>();

        Collect(compilation.Assembly, tier: 0, registrations, diagnostics);
        foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            bool mayRegister = referenced.Modules.Any(module =>
                module.ReferencedAssemblies.Any(identity =>
                    identity.Name == AttributeNames.AttributesAssembly
                )
            );
            if (mayRegister)
            {
                Collect(referenced, tier: 1, registrations, diagnostics);
            }
        }

        return registrations.Count == 0 && diagnostics.Count == 0
            ? AdapterRegistry.Empty
            : new AdapterRegistry(registrations.ToEquatableArray(), diagnostics.ToEquatableArray());
    }

    private static void Collect(
        IAssemblySymbol assembly,
        int tier,
        List<AdapterRegistration> registrations,
        List<DiagnosticInfo> diagnostics
    )
    {
        foreach (AttributeData attribute in assembly.GetAttributes())
        {
            if (
                attribute.AttributeClass?.ToDisplayString() != AttributeNames.TypeAdapter
                || attribute.ConstructorArguments.Length != 1
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol adapter
            )
            {
                continue;
            }

            LocationInfo? location = LocationInfo.From(
                attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            );
            (AdapterModel? model, string? problem) = Analyze(adapter, fromOtherAssembly: tier > 0);
            if (model is null)
            {
                // A broken registration in someone else's assembly is theirs to fix; it simply does
                // not participate. One in this compilation is reported.
                if (tier == 0)
                {
                    diagnostics.Add(
                        DiagnosticInfo.Create(
                            DiagnosticDescriptors.InvalidAdapter,
                            location,
                            adapter.Name,
                            problem!
                        )
                    );
                }

                continue;
            }

            if (tier == 0 && Planning.ArrowMappingTable.HasBuiltIn(model.Domain))
            {
                diagnostics.Add(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.InvalidAdapter,
                        location,
                        adapter.Name,
                        $"it is registered for '{model.DomainType.Replace("global::", "")}', which has a built-in Arrow mapping; registrations apply only to types without one, so use [ArrowAdapter] on a member to override a built-in"
                    )
                );
                continue;
            }

            registrations.Add(new AdapterRegistration(model, tier, assembly.Name));
        }
    }

    private static (IMethodSymbol? Method, string? Problem) Single(
        INamedTypeSymbol adapter,
        string name,
        bool fromOtherAssembly
    )
    {
        var candidates = adapter
            .GetMembers(name)
            .OfType<IMethodSymbol>()
            .Where(m =>
                m.IsStatic
                && m.TypeParameters.Length == 0
                && m.Parameters.Length == 1
                && m.Parameters[0].RefKind == RefKind.None
                && !m.ReturnsVoid
                && IsCallable(m, fromOtherAssembly)
            )
            .ToList();

        return candidates.Count switch
        {
            1 => (candidates[0], null),
            0 => (
                null,
                $"it has no accessible, non-generic 'static {name}(value)' method returning a value"
            ),
            _ => (
                null,
                $"it has {candidates.Count} '{name}' overloads; an adapter converts exactly one pair of types"
            ),
        };
    }

    private static bool IsCallable(ISymbol symbol, bool fromOtherAssembly)
    {
        for (
            ISymbol? current = symbol;
            current is not null and not INamespaceSymbol;
            current = current.ContainingSymbol
        )
        {
            bool ok = fromOtherAssembly
                ? current.DeclaredAccessibility == Accessibility.Public
                : MemberCollector.IsReachable(current);
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNullableValue(ITypeSymbol type) =>
        type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static string Display(ITypeSymbol type) => type.ToDisplayString();
}
