using System.Globalization;
using System.Linq;
using Arrow.SourceGenerator.Planning;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// Emits <c>FromRecordBatch</c> and <c>FromRecordBatches</c>: strict validation once per batch,
/// then typed access and construction of each row.
/// </summary>
/// <remarks>
/// A batch may come from outside the process (IPC, Flight, files), so nothing about it is trusted
/// (docs/00-DESIGN-GOALS.md section 33). Every assumption the reader makes is checked before any
/// value is read: field presence and uniqueness, the array's actual type and type parameters,
/// agreement between schema and array, column length, nulls in non-nullable fields, and the
/// structural soundness of buffers and offsets. All problems are reported together in one
/// <c>InvalidDataException</c>; nothing is coerced. Values that are individually out of range for
/// their CLR type (a Date32 beyond <c>DateOnly.MaxValue</c>) fail with the row and field named.
/// </remarks>
internal static class FromRecordBatchEmitter
{
    private const string Arrow = ArrowTypes.Arrow;
    private const string Types = ArrowTypes.Types;

    public static void Emit(SourceWriter writer, EmissionContext context)
    {
        string model = context.ModelType;

        writer.Line("/// <summary>");
        writer.Line(
            $"/// Materialises every row of <paramref name=\"batch\"/> as {context.ModelCref}."
        );
        writer.Line(
            "/// Fields are matched by name, so extra fields and field order do not matter."
        );
        writer.Line("/// </summary>");
        writer.Line(
            "/// <param name=\"batch\">The batch. It is read, never retained or disposed.</param>"
        );
        writer.Line(
            "/// <exception cref=\"global::System.IO.InvalidDataException\">The batch does not match"
        );
        writer.Line(
            "/// <see cref=\"Schema\"/> (every mismatch is listed), is structurally malformed, or holds a"
        );
        writer.Line("/// value outside the range of its CLR type.</exception>");
        using (writer.Block($"public static {model}[] FromRecordBatch({Arrow}.RecordBatch batch)"))
        {
            writer.Line(
                "if (batch is null) throw new global::System.ArgumentNullException(nameof(batch));"
            );
            writer.Line("return ReadRecordBatch(batch);");
        }

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line(
            $"/// Materialises the rows of each batch in turn, lazily. Each batch is validated when it"
        );
        writer.Line("/// is reached; batches are read, never retained or disposed.");
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"batches\">The batches, enumerated once.</param>");
        using (
            writer.Block(
                $"public static global::System.Collections.Generic.IEnumerable<{model}> FromRecordBatches(global::System.Collections.Generic.IEnumerable<{Arrow}.RecordBatch> batches)"
            )
        )
        {
            writer.Line(
                "if (batches is null) throw new global::System.ArgumentNullException(nameof(batches));"
            );
            writer.Line("return FromRecordBatchesIterator(batches);");
        }

        writer.Line();
        using (
            writer.Block(
                $"private static global::System.Collections.Generic.IEnumerable<{model}> FromRecordBatchesIterator(global::System.Collections.Generic.IEnumerable<{Arrow}.RecordBatch> batches)"
            )
        )
        {
            using (writer.Block("foreach (var batch in batches)"))
            {
                writer.Line(
                    "if (batch is null) throw new global::System.ArgumentException(\"The batch sequence contains a null batch.\", nameof(batches));"
                );
                using (writer.Block($"foreach (var row in ReadRecordBatch(batch))"))
                {
                    writer.Line("yield return row;");
                }
            }
        }

        writer.Line();
        EmitRead(writer, context);
        writer.Line();
        EmitValidation(writer, context);
    }

    private static void EmitRead(SourceWriter writer, EmissionContext context)
    {
        using (
            writer.Block(
                $"private static {context.ModelType}[] ReadRecordBatch({Arrow}.RecordBatch batch)"
            )
        )
        {
            writer.Line("int[] ordinals = ResolveColumns(batch);");
            foreach (FieldPlan field in context.Plan.Fields)
            {
                string array = ArrowTypes.ArrayClass(field.Leaf);
                writer.Line(
                    $"var c{field.Ordinal} = ({array})batch.Column(ordinals[{field.Ordinal}]);"
                );
                if (HoistsValues(field))
                {
                    writer.Line(
                        $"global::System.ReadOnlySpan<{field.Leaf.StorageType}> v{field.Ordinal} = c{field.Ordinal}.Values;"
                    );
                }
            }

            writer.Line("int length = batch.Length;");
            writer.Line($"var rows = new {context.ModelType}[length];");
            using (writer.Block("for (int i = 0; i < length; i++)"))
            {
                writer.Line($"rows[i] = {Construction(context)};");
            }

            writer.Line("return rows;");
        }
    }

    /// <summary>
    /// Fixed-width columns read through their typed value span, taken once per batch; the span is
    /// already sliced to the array's offset and length and was bounds-validated beforehand.
    /// </summary>
    private static bool HoistsValues(FieldPlan field) =>
        field.Leaf.StorageType.Length > 0 && field.Leaf.Conversion != ConversionStrategy.Boolean;

    private static string Construction(EmissionContext context)
    {
        var bound = context.Target.Construction.ConstructorParameters;
        string arguments = string.Join(
            ", ",
            bound.Select(name =>
                ReadExpression(context.Plan.Fields.Single(f => f.MemberName == name))
            )
        );
        string[] initializers = context
            .Plan.Fields.Where(f => !bound.Contains(f.MemberName, System.StringComparer.Ordinal))
            .Select(f => $"{f.MemberIdentifier} = {ReadExpression(f)}")
            .ToArray();

        string construct = $"new {context.ModelType}({arguments})";
        return initializers.Length == 0
            ? construct
            : construct + " { " + string.Join(", ", initializers) + " }";
    }

    /// <summary>The expression reading row <c>i</c> of a field as its CLR member type.</summary>
    private static string ReadExpression(FieldPlan field)
    {
        string c = "c" + field.Ordinal.ToString(CultureInfo.InvariantCulture);
        string v = "v" + field.Ordinal.ToString(CultureInfo.InvariantCulture);
        string name = field.FieldNameLiteral;
        string value = field.Leaf.Conversion switch
        {
            ConversionStrategy.Direct => $"{v}[i]",
            ConversionStrategy.EnumUnderlying => $"({field.ClrType}){v}[i]",
            ConversionStrategy.Boolean => $"{c}.GetValue(i).GetValueOrDefault()",
            ConversionStrategy.Utf8 => $"{c}.GetString(i)!",
            ConversionStrategy.Binary => $"{c}.GetBytes(i).ToArray()",
            ConversionStrategy.Decimal128 =>
                $"ReadDecimal({c}, i, {name}, {field.Leaf.Scale.ToString(CultureInfo.InvariantCulture)})",
            ConversionStrategy.DateOnlyDays => $"ToDateOnly({v}[i], i, {name})",
            ConversionStrategy.TimeOnlyMicroseconds => $"ToTimeOnly({v}[i], i, {name})",
            ConversionStrategy.DateTimeWallClockMicroseconds => $"ToDateTime({v}[i], i, {name})",
            ConversionStrategy.DateTimeOffsetUtcMicroseconds =>
                $"ToDateTimeOffset({v}[i], i, {name})",
            ConversionStrategy.TimeSpanMicroseconds => $"ToTimeSpan({v}[i], i, {name})",
            ConversionStrategy.GuidBigEndian => $"ReadGuidBigEndian({c}.GetBytes(i))",
            _ => throw new System.ArgumentOutOfRangeException(nameof(field)),
        };

        return field.Nulls switch
        {
            NullStrategy.Required => value,
            NullStrategy.NullableValue => $"{c}.IsNull(i) ? null : ({field.ClrType}?){value}",
            _ => $"{c}.IsNull(i) ? null : {value}",
        };
    }

    private static void EmitValidation(SourceWriter writer, EmissionContext context)
    {
        writer.Line("/// <summary>");
        writer.Line(
            "/// Checks every assumption the reader makes about <paramref name=\"batch\"/> and returns the"
        );
        writer.Line("/// column ordinal of each field. Throws once, listing every problem found.");
        writer.Line("/// </summary>");
        using (writer.Block($"private static int[] ResolveColumns({Arrow}.RecordBatch batch)"))
        {
            writer.Line($"var ordinals = new int[{context.Plan.Fields.Count}];");
            writer.Line("global::System.Collections.Generic.List<string>? errors = null;");
            foreach (FieldPlan field in context.Plan.Fields)
            {
                writer.Line();
                writer.Line($"// {SourceNames.CommentText(field.FieldName)}");
                using (writer.Block(""))
                {
                    EmitFieldValidation(writer, field);
                }
            }

            writer.Line();
            using (writer.Block("if (errors is not null)"))
            {
                writer.Line(
                    $"throw new global::System.IO.InvalidDataException(\"The RecordBatch does not match the generated Arrow schema of \" + {SourceNames.StringLiteral(context.Target.Name)} + \": \" + string.Join(\"; \", errors) + \".\");"
                );
            }

            writer.Line("return ordinals;");
        }
    }

    private static void EmitFieldValidation(SourceWriter writer, FieldPlan field)
    {
        string name = field.FieldNameLiteral;
        string expected = SourceNames.StringLiteral(ArrowTypes.Describe(field.Leaf));
        writer.Line($"int index = FindField(batch, {name}, ref errors);");
        writer.Line($"ordinals[{field.Ordinal}] = index;");
        using (writer.Block("if (index >= 0)"))
        {
            writer.Line($"{Arrow}.IArrowArray column = batch.Column(index);");
            string? parameterCheck = ParameterCheck(field.Leaf);
            if (parameterCheck is not null)
            {
                writer.Line($"{Types}.IArrowType type = column.Data.DataType;");
            }

            writer.Line(
                $"string? problem = CheckColumnShape(batch, index, column, {Types}.ArrowTypeId.{TypeId(field.Leaf)}, {expected})"
            );
            if (parameterCheck is not null)
            {
                writer.Line($"    ?? {parameterCheck}");
            }

            writer.Line($"    ?? {StructureCheck(field)}");
            if (!field.IsNullable)
            {
                writer.Line(
                    "    ?? (column.NullCount > 0 ? \"is non-nullable but holds \" + column.NullCount + \" null value(s)\" : null)"
                );
            }

            writer.Line("    ;");
            writer.Line($"if (problem is not null) AddError(ref errors, {name}, problem);");
        }
    }

    private static string TypeId(ArrowLeafPlan leaf) =>
        leaf.Kind switch
        {
            ArrowLogicalKind.Utf8 => "String",
            ArrowLogicalKind.Time64Microsecond => "Time64",
            ArrowLogicalKind.TimestampMicrosecond => "Timestamp",
            ArrowLogicalKind.TimestampMicrosecondUtc => "Timestamp",
            ArrowLogicalKind.DurationMicrosecond => "Duration",
            ArrowLogicalKind.FixedSizeBinary16 => "FixedSizedBinary",
            _ => leaf.Kind.ToString(),
        };

    /// <summary>Type-parameter checks: evaluated only once the type id is known to match.</summary>
    private static string? ParameterCheck(ArrowLeafPlan leaf) =>
        leaf.Kind switch
        {
            ArrowLogicalKind.Decimal128 => string.Format(
                CultureInfo.InvariantCulture,
                "(type is {0}.Decimal128Type d && (d.Precision != {1} || d.Scale != {2}) ? \"expected Decimal128({1}, {2}), found Decimal128(\" + d.Precision + \", \" + d.Scale + \")\" : null)",
                Types,
                leaf.Precision,
                leaf.Scale
            ),
            ArrowLogicalKind.Time64Microsecond =>
                $"(type is {Types}.Time64Type t && t.Unit != {Types}.TimeUnit.Microsecond ? \"expected unit Microsecond, found \" + t.Unit : null)",
            ArrowLogicalKind.TimestampMicrosecond =>
                $"(type is {Types}.TimestampType t && t.Unit != {Types}.TimeUnit.Microsecond ? \"expected unit Microsecond, found \" + t.Unit : type is {Types}.TimestampType z && !string.IsNullOrEmpty(z.Timezone) ? \"expected a wall-clock timestamp with no timezone, found timezone '\" + z.Timezone + \"'; map an instant to DateTimeOffset\" : null)",
            ArrowLogicalKind.TimestampMicrosecondUtc =>
                $"(type is {Types}.TimestampType t && t.Unit != {Types}.TimeUnit.Microsecond ? \"expected unit Microsecond, found \" + t.Unit : type is {Types}.TimestampType z && string.IsNullOrEmpty(z.Timezone) ? \"expected an instant (a timestamp with a timezone), found a wall-clock timestamp; map it to DateTime\" : null)",
            ArrowLogicalKind.DurationMicrosecond =>
                $"(type is {Types}.DurationType t && t.Unit != {Types}.TimeUnit.Microsecond ? \"expected unit Microsecond, found \" + t.Unit : null)",
            ArrowLogicalKind.FixedSizeBinary16 =>
                $"(type is {Types}.FixedSizeBinaryType f && f.ByteWidth != 16 ? \"expected FixedSizeBinary(16), found FixedSizeBinary(\" + f.ByteWidth + \")\" : null)",
            _ => null,
        };

    private static string StructureCheck(FieldPlan field) =>
        field.Leaf.Conversion switch
        {
            ConversionStrategy.Utf8 or ConversionStrategy.Binary =>
                $"CheckOffsets(({Arrow}.BinaryArray)column)",
            ConversionStrategy.Boolean => "CheckFixedWidth(column, 0)",
            ConversionStrategy.Decimal128 or ConversionStrategy.GuidBigEndian =>
                "CheckFixedWidth(column, 16)",
            _ => $"CheckFixedWidth(column, sizeof({field.Leaf.StorageType}))",
        };
}
