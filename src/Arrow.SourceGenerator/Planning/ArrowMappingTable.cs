using Arrow.SourceGenerator.Model;

namespace Arrow.SourceGenerator.Planning;

/// <summary>
/// The central CLR → Arrow mapping for built-in scalar types (docs/02-TYPE-MAPPING.md). Anything
/// without a row here has no Arrow representation and is diagnosed, never silently dropped.
/// </summary>
internal static class ArrowMappingTable
{
    public const int DefaultDecimalPrecision = 38;
    public const int DefaultDecimalScale = 18;

    /// <summary>The largest scale a <see cref="decimal"/> can represent.</summary>
    public const int MaxClrDecimalScale = 28;

    public const int MaxDecimal128Precision = 38;

    /// <summary>Maps a built-in kind, or returns null when it has no Arrow representation.</summary>
    public static ArrowLeafPlan? TryMap(TypeRef type, int precision, int scale) =>
        type.Kind switch
        {
            ClrTypeKind.Boolean => new(
                ArrowLogicalKind.Boolean,
                ConversionStrategy.Boolean,
                "bool"
            ),
            ClrTypeKind.SByte => Direct(ArrowLogicalKind.Int8, "sbyte"),
            ClrTypeKind.Byte => Direct(ArrowLogicalKind.UInt8, "byte"),
            ClrTypeKind.Int16 => Direct(ArrowLogicalKind.Int16, "short"),
            ClrTypeKind.UInt16 => Direct(ArrowLogicalKind.UInt16, "ushort"),
            ClrTypeKind.Int32 => Direct(ArrowLogicalKind.Int32, "int"),
            ClrTypeKind.UInt32 => Direct(ArrowLogicalKind.UInt32, "uint"),
            ClrTypeKind.Int64 => Direct(ArrowLogicalKind.Int64, "long"),
            ClrTypeKind.UInt64 => Direct(ArrowLogicalKind.UInt64, "ulong"),
            ClrTypeKind.Single => Direct(ArrowLogicalKind.Float, "float"),
            ClrTypeKind.Double => Direct(ArrowLogicalKind.Double, "double"),
            ClrTypeKind.String => new(ArrowLogicalKind.Utf8, ConversionStrategy.Utf8, ""),
            ClrTypeKind.ByteArray => new(ArrowLogicalKind.Binary, ConversionStrategy.Binary, ""),
            ClrTypeKind.Decimal => new(
                ArrowLogicalKind.Decimal128,
                ConversionStrategy.Decimal128,
                "",
                precision,
                scale
            ),
            ClrTypeKind.DateOnly => new(
                ArrowLogicalKind.Date32,
                ConversionStrategy.DateOnlyDays,
                "int"
            ),
            ClrTypeKind.TimeOnly => new(
                ArrowLogicalKind.Time64Microsecond,
                ConversionStrategy.TimeOnlyMicroseconds,
                "long"
            ),
            ClrTypeKind.DateTime => new(
                ArrowLogicalKind.TimestampMicrosecond,
                ConversionStrategy.DateTimeWallClockMicroseconds,
                "long"
            ),
            ClrTypeKind.DateTimeOffset => new(
                ArrowLogicalKind.TimestampMicrosecondUtc,
                ConversionStrategy.DateTimeOffsetUtcMicroseconds,
                "long"
            ),
            ClrTypeKind.TimeSpan => new(
                ArrowLogicalKind.DurationMicrosecond,
                ConversionStrategy.TimeSpanMicroseconds,
                "long"
            ),
            ClrTypeKind.Guid => new(
                ArrowLogicalKind.FixedSizeBinary16,
                ConversionStrategy.GuidBigEndian,
                ""
            ),
            ClrTypeKind.Enum => MapEnum(type.EnumUnderlying),
            _ => null,
        };

    private static ArrowLeafPlan? MapEnum(ClrTypeKind underlying)
    {
        ArrowLeafPlan? storage = TryMap(
            new TypeRef("", underlying, ClrTypeKind.Other, IsValueType: true),
            0,
            0
        );
        return storage is { Conversion: ConversionStrategy.Direct }
            ? storage with
            {
                Conversion = ConversionStrategy.EnumUnderlying,
            }
            : null;
    }

    private static ArrowLeafPlan Direct(ArrowLogicalKind kind, string storage) =>
        new(kind, ConversionStrategy.Direct, storage);
}
