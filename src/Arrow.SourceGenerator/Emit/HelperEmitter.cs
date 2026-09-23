using System.Linq;
using Arrow.SourceGenerator.Planning;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// Emits the private constants and helpers the other emitters call, and only those a plan needs.
/// Helpers are implementation detail: always <c>private</c>, never part of the budgeted surface.
/// </summary>
internal static class HelperEmitter
{
    private const string Arrow = ArrowTypes.Arrow;

    // Lets nullable flow analysis in the generated code see that a throw helper never returns.
    private const string DoesNotReturn = "[global::System.Diagnostics.CodeAnalysis.DoesNotReturn]";

    public static void Emit(SourceWriter writer, EmissionContext context)
    {
        bool Uses(ConversionStrategy conversion) =>
            context.Plan.Fields.Any(field => field.Leaf.Conversion == conversion);

        if (Uses(ConversionStrategy.DateOnlyDays))
        {
            writer.Line("// DateOnly.DayNumber of 1970-01-01, the Date32 epoch.");
            writer.Line("private const int UnixEpochDayNumber = 719162;");
            writer.Line();
        }

        if (
            Uses(ConversionStrategy.DateTimeWallClockMicroseconds)
            || Uses(ConversionStrategy.DateTimeOffsetUtcMicroseconds)
        )
        {
            writer.Line("// Microseconds from 0001-01-01 (DateTime tick zero) to 1970-01-01.");
            writer.Line("private const long UnixEpochMicroseconds = 62135596800000000L;");
            writer.Line();
        }

        if (Uses(ConversionStrategy.GuidBigEndian))
        {
            EmitGuidHelpers(writer);
        }

        EmitThrowHelpers(writer, context);
    }

    private static void EmitGuidHelpers(SourceWriter writer)
    {
        writer.Line("private static readonly byte[] GuidZeroBytes = new byte[16];");
        writer.Line();
        writer.Line(
            "// Guid's in-memory layout stores its first three groups little-endian; Arrow (and"
        );
        writer.Line(
            "// RFC 4122, uuid.bytes, the arrow.uuid extension) use big-endian order throughout."
        );
        using (
            writer.Block(
                $"private static void AppendGuidBigEndian({Arrow}.ArrowBuffer.Builder<byte> values, global::System.Guid value)"
            )
        )
        {
            writer.Line("global::System.Span<byte> bytes = stackalloc byte[16];");
            writer.Line("value.TryWriteBytes(bytes);");
            writer.Line("global::System.MemoryExtensions.Reverse(bytes.Slice(0, 4));");
            writer.Line("global::System.MemoryExtensions.Reverse(bytes.Slice(4, 2));");
            writer.Line("global::System.MemoryExtensions.Reverse(bytes.Slice(6, 2));");
            writer.Line("values.Append((global::System.ReadOnlySpan<byte>)bytes);");
        }

        writer.Line();
    }

    private static void EmitThrowHelpers(SourceWriter writer, EmissionContext context)
    {
        string model = SourceNames.StringLiteral(context.Target.Name);

        writer.Line(DoesNotReturn);
        using (writer.Block("private static void ThrowCollectionChanged()"))
        {
            writer.Line(
                "throw new global::System.InvalidOperationException(\"The row collection changed while it was being converted: it no longer yields Count rows.\");"
            );
        }

        if (!context.Target.IsValueType)
        {
            writer.Line();
            writer.Line(DoesNotReturn);
            using (writer.Block("private static void ThrowNullRow(int index)"))
            {
                writer.Line(
                    $"throw new global::System.ArgumentException(\"Row \" + index + \" is null; \" + {model} + \" rows must not be null.\", \"rows\");"
                );
            }
        }

        if (context.Plan.Fields.Any(f => !f.IsNullable && !f.IsValueType))
        {
            writer.Line();
            writer.Line(DoesNotReturn);
            using (writer.Block("private static void ThrowRequiredNull(int index, string field)"))
            {
                writer.Line(
                    "throw new global::System.ArgumentException(\"Row \" + index + \": the value for non-nullable Arrow field '\" + field + \"' is null.\", \"rows\");"
                );
            }
        }

        if (context.Plan.Fields.Any(f => f.Leaf.Conversion == ConversionStrategy.Decimal128))
        {
            writer.Line();
            writer.Line(DoesNotReturn);
            using (
                writer.Block(
                    "private static void ThrowDoesNotFit(int index, string field, string arrowType, global::System.Exception inner)"
                )
            )
            {
                writer.Line(
                    "throw new global::System.ArgumentException(\"Row \" + index + \": the value for Arrow field '\" + field + \"' does not fit \" + arrowType + \"; nothing is rounded or truncated.\", \"rows\", inner);"
                );
            }
        }
    }
}
