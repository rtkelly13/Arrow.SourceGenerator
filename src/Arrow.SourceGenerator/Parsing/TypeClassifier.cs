using Arrow.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace Arrow.SourceGenerator.Parsing;

/// <summary>
/// Classifies a member type into a <see cref="TypeRef"/> and its nullability, in CLR terms.
/// </summary>
internal static class TypeClassifier
{
    /// <summary>
    /// <see cref="System.Nullable{T}"/> unwraps to <c>T</c> and is nullable. A reference type is
    /// non-nullable only when explicitly annotated as such; an oblivious (nullable-disabled)
    /// reference is treated as nullable, because nothing promises it is not.
    /// </summary>
    public static (TypeRef Type, bool IsNullable) Classify(
        ITypeSymbol type,
        NullableAnnotation annotation
    )
    {
        bool nullable;
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } wrapper
        )
        {
            type = wrapper.TypeArguments[0];
            nullable = true;
        }
        else
        {
            nullable = type.IsReferenceType && annotation != NullableAnnotation.NotAnnotated;
        }

        ClrTypeKind kind = KindOf(type);
        ClrTypeKind underlying =
            kind == ClrTypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } u }
                ? KindOf(u)
                : ClrTypeKind.Other;

        string name = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return (new TypeRef(name, kind, underlying, type.IsValueType), nullable);
    }

    private static ClrTypeKind KindOf(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return ClrTypeKind.Enum;
        }

        if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
        {
            return ClrTypeKind.ByteArray;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return ClrTypeKind.Boolean;
            case SpecialType.System_SByte:
                return ClrTypeKind.SByte;
            case SpecialType.System_Byte:
                return ClrTypeKind.Byte;
            case SpecialType.System_Int16:
                return ClrTypeKind.Int16;
            case SpecialType.System_UInt16:
                return ClrTypeKind.UInt16;
            case SpecialType.System_Int32:
                return ClrTypeKind.Int32;
            case SpecialType.System_UInt32:
                return ClrTypeKind.UInt32;
            case SpecialType.System_Int64:
                return ClrTypeKind.Int64;
            case SpecialType.System_UInt64:
                return ClrTypeKind.UInt64;
            case SpecialType.System_Single:
                return ClrTypeKind.Single;
            case SpecialType.System_Double:
                return ClrTypeKind.Double;
            case SpecialType.System_Decimal:
                return ClrTypeKind.Decimal;
            case SpecialType.System_String:
                return ClrTypeKind.String;
            case SpecialType.System_Char:
                return ClrTypeKind.Char;
            case SpecialType.System_DateTime:
                return ClrTypeKind.DateTime;
        }

        if (
            type.ContainingNamespace is
            { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
        )
        {
            switch (type.MetadataName)
            {
                case "DateTimeOffset":
                    return ClrTypeKind.DateTimeOffset;
                case "DateOnly":
                    return ClrTypeKind.DateOnly;
                case "TimeOnly":
                    return ClrTypeKind.TimeOnly;
                case "TimeSpan":
                    return ClrTypeKind.TimeSpan;
                case "Guid":
                    return ClrTypeKind.Guid;
            }
        }

        return ClrTypeKind.Other;
    }
}
