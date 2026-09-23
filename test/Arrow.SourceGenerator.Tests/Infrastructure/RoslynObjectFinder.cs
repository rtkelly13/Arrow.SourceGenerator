using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Arrow.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Walks an object graph looking for Roslyn objects that must never be cached in incremental
/// state: symbols, syntax, compilations, semantic models and locations.
/// </summary>
internal static class RoslynObjectFinder
{
    private static readonly Type[] Forbidden =
    [
        typeof(ISymbol),
        typeof(SyntaxNode),
        typeof(SyntaxTree),
        typeof(Compilation),
        typeof(SemanticModel),
        typeof(Location),
    ];

    public static string? Find(object? root) =>
        Find(root, new HashSet<object>(ReferenceEqualityComparer.Instance), "root");

    private static string? Find(object? value, HashSet<object> seen, string path)
    {
        if (
            value is null
            || value is string
            || value.GetType().IsPrimitive
            || value.GetType().IsEnum
        )
        {
            return null;
        }

        Type type = value.GetType();
        if (Forbidden.Any(f => f.IsAssignableFrom(type)))
        {
            return $"{path}: {type.FullName}";
        }

        // Diagnostic descriptors are static singletons and root nothing.
        if (value is DiagnosticDescriptor || !type.IsValueType && !seen.Add(value))
        {
            return null;
        }

        if (value is IEnumerable sequence)
        {
            int index = 0;
            foreach (object? item in sequence)
            {
                if (Find(item, seen, $"{path}[{index++}]") is { } found)
                {
                    return found;
                }
            }
        }

        foreach (
            FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
        )
        {
            if (Find(field.GetValue(value), seen, $"{path}.{field.Name}") is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
