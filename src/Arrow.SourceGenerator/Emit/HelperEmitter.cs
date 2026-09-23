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
        writer.Line();
        EmitReadHelpers(writer, context);
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

    private static void EmitReadHelpers(SourceWriter writer, EmissionContext context)
    {
        bool Uses(ConversionStrategy conversion) =>
            context.Plan.Fields.Any(field => field.Leaf.Conversion == conversion);

        using (
            writer.Block(
                $"private static int FindField({Arrow}.RecordBatch batch, string name, ref global::System.Collections.Generic.List<string>? errors)"
            )
        )
        {
            writer.Line("int found = -1;");
            writer.Line("int matches = 0;");
            writer.Line("var fields = batch.Schema.FieldsList;");
            using (writer.Block("for (int i = 0; i < fields.Count; i++)"))
            {
                using (
                    writer.Block(
                        "if (string.Equals(fields[i].Name, name, global::System.StringComparison.Ordinal))"
                    )
                )
                {
                    writer.Line("found = i;");
                    writer.Line("matches++;");
                }
            }

            writer.Line("if (matches == 1) return found;");
            writer.Line(
                "AddError(ref errors, name, matches == 0 ? \"is missing\" : \"appears \" + matches + \" times, so it is ambiguous\");"
            );
            writer.Line("return -1;");
        }

        writer.Line();
        using (
            writer.Block(
                "private static void AddError(ref global::System.Collections.Generic.List<string>? errors, string field, string problem)"
            )
        )
        {
            writer.Line(
                "(errors ??= new global::System.Collections.Generic.List<string>()).Add(\"field '\" + field + \"' \" + problem);"
            );
        }

        writer.Line();
        using (
            writer.Block(
                $"private static string? CheckColumnShape({Arrow}.RecordBatch batch, int index, {Arrow}.IArrowArray column, {ArrowTypes.Types}.ArrowTypeId expected, string expectedName)"
            )
        )
        {
            writer.Line($"{ArrowTypes.Types}.IArrowType type = column.Data.DataType;");
            writer.Line(
                $"if (type.TypeId == {ArrowTypes.Types}.ArrowTypeId.Dictionary) return \"is dictionary-encoded, which is not supported; decode it first\";"
            );
            writer.Line(
                $"if (type.TypeId == {ArrowTypes.Types}.ArrowTypeId.Extension) return \"has an extension type, which is not supported; expected \" + expectedName;"
            );
            writer.Line(
                "if (type.TypeId != expected) return \"expected \" + expectedName + \", found \" + type.Name;"
            );
            writer.Line(
                "if (batch.Schema.FieldsList[index].DataType.TypeId != type.TypeId) return \"is declared as \" + batch.Schema.FieldsList[index].DataType.Name + \" by the schema but its column holds \" + type.Name;"
            );
            writer.Line(
                "if (column.Length != batch.Length) return \"has \" + column.Length + \" values but the batch has \" + batch.Length + \" rows\";"
            );
            writer.Line("return null;");
        }

        writer.Line();
        using (writer.Block($"private static string? CheckValidity({Arrow}.ArrayData data)"))
        {
            writer.Line("long bits = (long)data.Offset + data.Length;");
            writer.Line(
                "if (data.Offset < 0 || data.Length < 0) return \"has a negative offset or length\";"
            );
            writer.Line("if (data.Buffers.Length == 0) return \"has no buffers\";");
            writer.Line($"{Arrow}.ArrowBuffer validity = data.Buffers[0];");
            writer.Line(
                "if (data.NullCount > 0 && validity.IsEmpty) return \"declares nulls but has no validity bitmap\";"
            );
            writer.Line(
                "if (!validity.IsEmpty && validity.Length < (bits + 7) / 8) return \"has a validity bitmap shorter than its length\";"
            );
            // NullCount is only a claim: the reader decides null-ness from the bitmap, and a
            // non-nullable field is checked with NullCount, so the two must agree. A negative
            // NullCount means "not computed", which Apache.Arrow derives from the bitmap itself.
            using (writer.Block("if (!validity.IsEmpty && data.NullCount >= 0)"))
            {
                writer.Line("global::System.ReadOnlySpan<byte> map = validity.Span;");
                writer.Line("long unset = 0;");
                using (writer.Block("for (long i = data.Offset; i < bits; i++)"))
                {
                    writer.Line("if ((map[(int)(i >> 3)] & (1 << (int)(i & 7))) == 0) unset++;");
                }

                writer.Line(
                    "if (unset != data.NullCount) return \"declares \" + data.NullCount + \" null value(s) but its validity bitmap marks \" + unset;"
                );
            }

            writer.Line("return null;");
        }

        writer.Line();
        writer.Line("// byteWidth 0 means bit-packed (Boolean).");
        using (
            writer.Block(
                $"private static string? CheckFixedWidth({Arrow}.IArrowArray column, int byteWidth)"
            )
        )
        {
            writer.Line($"{Arrow}.ArrayData data = column.Data;");
            writer.Line("string? problem = CheckValidity(data);");
            writer.Line("if (problem is not null) return problem;");
            writer.Line("if (data.Buffers.Length < 2) return \"is missing its value buffer\";");
            writer.Line("long end = (long)data.Offset + data.Length;");
            writer.Line("long needed = byteWidth == 0 ? (end + 7) / 8 : end * byteWidth;");
            writer.Line(
                "return data.Buffers[1].Length < needed ? \"has a value buffer of \" + data.Buffers[1].Length + \" bytes where \" + needed + \" are required\" : null;"
            );
        }

        if (Uses(ConversionStrategy.Utf8) || Uses(ConversionStrategy.Binary))
        {
            writer.Line();
            using (writer.Block($"private static string? CheckOffsets({Arrow}.BinaryArray array)"))
            {
                writer.Line($"{Arrow}.ArrayData data = array.Data;");
                writer.Line("string? problem = CheckValidity(data);");
                writer.Line("if (problem is not null) return problem;");
                writer.Line(
                    "if (data.Buffers.Length < 3) return \"is missing its offset or value buffer\";"
                );
                writer.Line("long slots = (long)data.Offset + data.Length + 1;");
                writer.Line(
                    "if (data.Buffers[1].Length < slots * 4) return \"has an offset buffer shorter than its length\";"
                );
                writer.Line("global::System.ReadOnlySpan<int> offsets = array.ValueOffsets;");
                writer.Line("int valuesLength = array.ValueBuffer.Length;");
                writer.Line("int previous = offsets[0];");
                writer.Line(
                    "if (previous < 0 || previous > valuesLength) return \"has an offset outside its value buffer at slot 0\";"
                );
                using (writer.Block("for (int k = 1; k < offsets.Length; k++)"))
                {
                    writer.Line("int offset = offsets[k];");
                    writer.Line(
                        "if (offset < previous || offset > valuesLength) return \"has offsets that decrease or run past its value buffer at slot \" + (k - 1);"
                    );
                    writer.Line("previous = offset;");
                }

                writer.Line("return null;");
            }
        }

        writer.Line();
        using (
            writer.Block(
                "private static global::System.IO.InvalidDataException OutOfRange(int row, string field, string clrType, global::System.Exception? inner = null)"
            )
        )
        {
            writer.Line(
                "return new global::System.IO.InvalidDataException(\"Row \" + row + \": the value in Arrow field '\" + field + \"' is outside the range of \" + clrType + \".\", inner);"
            );
        }

        EmitConversionReaders(writer, Uses);
    }

    private static void EmitConversionReaders(
        SourceWriter writer,
        System.Func<ConversionStrategy, bool> uses
    )
    {
        if (uses(ConversionStrategy.Decimal128))
        {
            writer.Line();
            writer.Line(
                "// Exact by construction. Apache.Arrow's Decimal128Array.GetValue rounds a value with more"
            );
            writer.Line(
                "// significant digits than System.Decimal holds; this reads the 128-bit unscaled integer and"
            );
            writer.Line(
                "// accepts it only when it is exactly representable in System.Decimal, so nothing is ever rounded."
            );
            using (
                writer.Block(
                    $"private static decimal ReadDecimal({Arrow}.Decimal128Array array, int row, string field, byte scale)"
                )
            )
            {
                writer.Line(
                    "global::System.Int128 unscaled = global::System.Buffers.Binary.BinaryPrimitives.ReadInt128LittleEndian(array.GetBytes(row));"
                );
                writer.Line("bool negative = unscaled < 0;");
                writer.Line(
                    "if (unscaled == global::System.Int128.MinValue) throw OutOfRange(row, field, \"System.Decimal\");"
                );
                writer.Line(
                    "global::System.UInt128 magnitude = (global::System.UInt128)(negative ? -unscaled : unscaled);"
                );
                writer.Line(
                    "// A wide unscaled integer can still be exact: trailing decimal zeros are stripped (reducing"
                );
                writer.Line(
                    "// the scale) until it fits, so 10^19 at scale 18 reads as 10000000000000000000m."
                );
                using (writer.Block("while ((magnitude >> 96) != 0)"))
                {
                    writer.Line(
                        "if (scale == 0 || magnitude % 10 != 0) throw OutOfRange(row, field, \"System.Decimal\");"
                    );
                    writer.Line("magnitude /= 10;");
                    writer.Line("scale--;");
                }

                writer.Line(
                    "return new decimal((int)(uint)magnitude, (int)(uint)(magnitude >> 32), (int)(uint)(magnitude >> 64), negative, scale);"
                );
            }
        }

        if (uses(ConversionStrategy.DateOnlyDays))
        {
            writer.Line();
            using (
                writer.Block(
                    "private static global::System.DateOnly ToDateOnly(int days, int row, string field)"
                )
            )
            {
                writer.Line("long dayNumber = (long)days + UnixEpochDayNumber;");
                writer.Line(
                    "if (dayNumber < 0 || dayNumber > global::System.DateOnly.MaxValue.DayNumber) throw OutOfRange(row, field, \"System.DateOnly\");"
                );
                writer.Line("return global::System.DateOnly.FromDayNumber((int)dayNumber);");
            }
        }

        if (uses(ConversionStrategy.TimeOnlyMicroseconds))
        {
            writer.Line();
            using (
                writer.Block(
                    "private static global::System.TimeOnly ToTimeOnly(long microseconds, int row, string field)"
                )
            )
            {
                writer.Line(
                    "if (microseconds < 0 || microseconds >= 86400000000L) throw OutOfRange(row, field, \"System.TimeOnly\");"
                );
                writer.Line("return new global::System.TimeOnly(microseconds * 10L);");
            }
        }

        bool dateTime = uses(ConversionStrategy.DateTimeWallClockMicroseconds);
        bool dateTimeOffset = uses(ConversionStrategy.DateTimeOffsetUtcMicroseconds);
        if (dateTime || dateTimeOffset)
        {
            writer.Line();
            using (
                writer.Block(
                    "private static long ToTicks(long microseconds, int row, string field, string clrType)"
                )
            )
            {
                writer.Line(
                    "if (microseconds < -UnixEpochMicroseconds || microseconds > global::System.DateTime.MaxValue.Ticks / 10L - UnixEpochMicroseconds) throw OutOfRange(row, field, clrType);"
                );
                writer.Line("return (microseconds + UnixEpochMicroseconds) * 10L;");
            }
        }

        if (dateTime)
        {
            writer.Line();
            using (
                writer.Block(
                    "private static global::System.DateTime ToDateTime(long microseconds, int row, string field)"
                )
            )
            {
                writer.Line(
                    "return new global::System.DateTime(ToTicks(microseconds, row, field, \"System.DateTime\"), global::System.DateTimeKind.Unspecified);"
                );
            }
        }

        if (dateTimeOffset)
        {
            writer.Line();
            using (
                writer.Block(
                    "private static global::System.DateTimeOffset ToDateTimeOffset(long microseconds, int row, string field)"
                )
            )
            {
                writer.Line(
                    "return new global::System.DateTimeOffset(ToTicks(microseconds, row, field, \"System.DateTimeOffset\"), global::System.TimeSpan.Zero);"
                );
            }
        }

        if (uses(ConversionStrategy.TimeSpanMicroseconds))
        {
            writer.Line();
            using (
                writer.Block(
                    "private static global::System.TimeSpan ToTimeSpan(long microseconds, int row, string field)"
                )
            )
            {
                writer.Line(
                    "if (microseconds > global::System.TimeSpan.MaxValue.Ticks / 10L || microseconds < global::System.TimeSpan.MinValue.Ticks / 10L) throw OutOfRange(row, field, \"System.TimeSpan\");"
                );
                writer.Line("return new global::System.TimeSpan(microseconds * 10L);");
            }
        }

        if (uses(ConversionStrategy.GuidBigEndian))
        {
            writer.Line();
            using (
                writer.Block(
                    "private static global::System.Guid ReadGuidBigEndian(global::System.ReadOnlySpan<byte> source)"
                )
            )
            {
                writer.Line("global::System.Span<byte> bytes = stackalloc byte[16];");
                writer.Line("source.CopyTo(bytes);");
                writer.Line("global::System.MemoryExtensions.Reverse(bytes.Slice(0, 4));");
                writer.Line("global::System.MemoryExtensions.Reverse(bytes.Slice(4, 2));");
                writer.Line("global::System.MemoryExtensions.Reverse(bytes.Slice(6, 2));");
                writer.Line("return new global::System.Guid(bytes);");
            }
        }
    }
}
