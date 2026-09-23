using System.Globalization;
using Arrow.SourceGenerator.Planning;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// The C# spelling of Apache.Arrow API for each planned kind. Emitters ask this table; none spells
/// an Arrow type name itself, so one row changes every emitter consistently.
/// </summary>
internal static class ArrowTypes
{
    public const string Arrow = "global::Apache.Arrow";
    public const string Types = "global::Apache.Arrow.Types";

    /// <summary>An expression producing the field's <c>IArrowType</c>.</summary>
    public static string TypeExpression(ArrowLeafPlan leaf) =>
        leaf.Kind switch
        {
            ArrowLogicalKind.Boolean => $"{Types}.BooleanType.Default",
            ArrowLogicalKind.Int8 => $"{Types}.Int8Type.Default",
            ArrowLogicalKind.UInt8 => $"{Types}.UInt8Type.Default",
            ArrowLogicalKind.Int16 => $"{Types}.Int16Type.Default",
            ArrowLogicalKind.UInt16 => $"{Types}.UInt16Type.Default",
            ArrowLogicalKind.Int32 => $"{Types}.Int32Type.Default",
            ArrowLogicalKind.UInt32 => $"{Types}.UInt32Type.Default",
            ArrowLogicalKind.Int64 => $"{Types}.Int64Type.Default",
            ArrowLogicalKind.UInt64 => $"{Types}.UInt64Type.Default",
            ArrowLogicalKind.Float => $"{Types}.FloatType.Default",
            ArrowLogicalKind.Double => $"{Types}.DoubleType.Default",
            ArrowLogicalKind.Utf8 => $"{Types}.StringType.Default",
            ArrowLogicalKind.Binary => $"{Types}.BinaryType.Default",
            ArrowLogicalKind.Decimal128 => string.Format(
                CultureInfo.InvariantCulture,
                "new {0}.Decimal128Type({1}, {2})",
                Types,
                leaf.Precision,
                leaf.Scale
            ),
            ArrowLogicalKind.Date32 => $"{Types}.Date32Type.Default",
            ArrowLogicalKind.Time64Microsecond =>
                $"new {Types}.Time64Type({Types}.TimeUnit.Microsecond)",
            ArrowLogicalKind.TimestampMicrosecond =>
                $"new {Types}.TimestampType({Types}.TimeUnit.Microsecond, (string?)null)",
            ArrowLogicalKind.TimestampMicrosecondUtc =>
                $"new {Types}.TimestampType({Types}.TimeUnit.Microsecond, \"UTC\")",
            ArrowLogicalKind.DurationMicrosecond => $"{Types}.DurationType.Microsecond",
            ArrowLogicalKind.FixedSizeBinary16 => $"new {Types}.FixedSizeBinaryType(16)",
            _ => throw new System.ArgumentOutOfRangeException(nameof(leaf)),
        };

    /// <summary>The concrete Apache.Arrow array class holding the field's values.</summary>
    public static string ArrayClass(ArrowLeafPlan leaf) =>
        leaf.Kind switch
        {
            ArrowLogicalKind.Boolean => $"{Arrow}.BooleanArray",
            ArrowLogicalKind.Int8 => $"{Arrow}.Int8Array",
            ArrowLogicalKind.UInt8 => $"{Arrow}.UInt8Array",
            ArrowLogicalKind.Int16 => $"{Arrow}.Int16Array",
            ArrowLogicalKind.UInt16 => $"{Arrow}.UInt16Array",
            ArrowLogicalKind.Int32 => $"{Arrow}.Int32Array",
            ArrowLogicalKind.UInt32 => $"{Arrow}.UInt32Array",
            ArrowLogicalKind.Int64 => $"{Arrow}.Int64Array",
            ArrowLogicalKind.UInt64 => $"{Arrow}.UInt64Array",
            ArrowLogicalKind.Float => $"{Arrow}.FloatArray",
            ArrowLogicalKind.Double => $"{Arrow}.DoubleArray",
            ArrowLogicalKind.Utf8 => $"{Arrow}.StringArray",
            ArrowLogicalKind.Binary => $"{Arrow}.BinaryArray",
            ArrowLogicalKind.Decimal128 => $"{Arrow}.Decimal128Array",
            ArrowLogicalKind.Date32 => $"{Arrow}.Date32Array",
            ArrowLogicalKind.Time64Microsecond => $"{Arrow}.Time64Array",
            ArrowLogicalKind.TimestampMicrosecond => $"{Arrow}.TimestampArray",
            ArrowLogicalKind.TimestampMicrosecondUtc => $"{Arrow}.TimestampArray",
            ArrowLogicalKind.DurationMicrosecond => $"{Arrow}.DurationArray",
            ArrowLogicalKind.FixedSizeBinary16 => $"{Arrow}.Arrays.FixedSizeBinaryArray",
            _ => throw new System.ArgumentOutOfRangeException(nameof(leaf)),
        };

    /// <summary>Human-readable Arrow type for messages, e.g. <c>Decimal128(38, 18)</c>.</summary>
    public static string Describe(ArrowLeafPlan leaf) =>
        leaf.Kind switch
        {
            ArrowLogicalKind.Decimal128 => string.Format(
                CultureInfo.InvariantCulture,
                "Decimal128({0}, {1})",
                leaf.Precision,
                leaf.Scale
            ),
            ArrowLogicalKind.Time64Microsecond => "Time64(Microsecond)",
            ArrowLogicalKind.TimestampMicrosecond => "Timestamp(Microsecond, no timezone)",
            ArrowLogicalKind.TimestampMicrosecondUtc => "Timestamp(Microsecond, with timezone)",
            ArrowLogicalKind.DurationMicrosecond => "Duration(Microsecond)",
            ArrowLogicalKind.FixedSizeBinary16 => "FixedSizeBinary(16)",
            _ => leaf.Kind.ToString(),
        };
}
