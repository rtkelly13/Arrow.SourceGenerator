using Arrow.SourceGenerator.Planning;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// Emits <c>ToRecordBatch</c> and <c>ToRecordBatches</c>: model rows to Arrow columns.
/// </summary>
/// <remarks>
/// <para>Row-oriented input must be transposed, so this is a conversion copy, never zero-copy
/// (docs/03-OWNERSHIP.md). The goal is the minimum necessary copying and allocation:</para>
/// <list type="bullet">
///   <item><description>one pass over the rows per column, into buffers reserved to the exact row
///     count, so builders never grow;</description></item>
///   <item><description>each column is finished before the next starts, so peak temporary memory is
///     one column's builder, not the sum of all columns (the lesson of Parquet PR #326);</description></item>
///   <item><description>validity bitmaps are built directly from nullability, and omitted when a
///     column has no nulls;</description></item>
///   <item><description>no per-row allocation and no reflection. The finished buffers are owned by
///     the returned <c>RecordBatch</c>.</description></item>
/// </list>
/// </remarks>
internal static class ToRecordBatchEmitter
{
    private const string Arrow = ArrowTypes.Arrow;

    public static void Emit(SourceWriter writer, EmissionContext context)
    {
        string rowsType =
            $"global::System.Collections.Generic.IReadOnlyCollection<{context.ModelType}>";
        string sequenceType =
            $"global::System.Collections.Generic.IEnumerable<{context.ModelType}>";

        writer.Line("/// <summary>");
        writer.Line(
            $"/// Converts <paramref name=\"rows\"/> into one <c>RecordBatch</c> with {context.ModelCref}'s"
        );
        writer.Line(
            "/// <see cref=\"Schema\"/>. An empty collection produces a valid zero-length batch."
        );
        writer.Line("/// </summary>");
        writer.Line(
            "/// <param name=\"rows\">The rows. Enumerated once per field; must not change meanwhile.</param>"
        );
        writer.Line(
            "/// <returns>A new batch that owns its buffers; the caller disposes it.</returns>"
        );
        writer.Line(
            "/// <exception cref=\"global::System.ArgumentException\">A row is null, a non-nullable"
        );
        writer.Line(
            "/// reference member is null, or a value does not fit its Arrow type.</exception>"
        );
        writer.Line(
            "/// <exception cref=\"global::System.InvalidOperationException\">The collection changed while"
        );
        writer.Line("/// it was being converted.</exception>");
        using (writer.Block($"public static {Arrow}.RecordBatch ToRecordBatch({rowsType} rows)"))
        {
            writer.Line(
                "if (rows is null) throw new global::System.ArgumentNullException(nameof(rows));"
            );
            writer.Line("return BuildRecordBatch(rows, rows.Count);");
        }

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line(
            $"/// Converts a sequence of {context.ModelCref} into consecutive batches of at most"
        );
        writer.Line(
            "/// <paramref name=\"batchSize\"/> rows. Lazy: each batch is built as it is requested, and"
        );
        writer.Line("/// an empty sequence yields no batches.");
        writer.Line("/// </summary>");
        writer.Line("/// <param name=\"rows\">The rows, enumerated once.</param>");
        writer.Line(
            "/// <param name=\"batchSize\">The maximum rows per batch; at least 1.</param>"
        );
        writer.Line("/// <returns>Batches the caller owns and disposes.</returns>");
        using (
            writer.Block(
                $"public static global::System.Collections.Generic.IEnumerable<{Arrow}.RecordBatch> ToRecordBatches({sequenceType} rows, int batchSize)"
            )
        )
        {
            writer.Line(
                "if (rows is null) throw new global::System.ArgumentNullException(nameof(rows));"
            );
            writer.Line(
                "if (batchSize < 1) throw new global::System.ArgumentOutOfRangeException(nameof(batchSize), batchSize, \"batchSize must be at least 1.\");"
            );
            writer.Line("return ToRecordBatchesIterator(rows, batchSize);");
        }

        writer.Line();
        using (
            writer.Block(
                $"private static global::System.Collections.Generic.IEnumerable<{Arrow}.RecordBatch> ToRecordBatchesIterator({sequenceType} rows, int batchSize)"
            )
        )
        {
            // Grows to the batch size rather than pre-allocating it, so a generous batchSize over a
            // short sequence costs nothing. The chunk only ever holds references (or copies of a
            // struct model). It is cleared once its batch is built and before that batch is
            // yielded: the iterator is suspended at a yield for as long as the caller likes, and
            // the batch has already copied everything it needs from the rows.
            writer.Line(
                $"var chunk = new global::System.Collections.Generic.List<{context.ModelType}>(global::System.Math.Min(batchSize, 1024));"
            );
            writer.Line($"{Arrow}.RecordBatch batch;");
            using (writer.Block("foreach (var row in rows)"))
            {
                writer.Line("chunk.Add(row);");
                using (writer.Block("if (chunk.Count == batchSize)"))
                {
                    writer.Line("batch = BuildRecordBatch(chunk, chunk.Count);");
                    writer.Line("chunk.Clear();");
                    writer.Line("yield return batch;");
                }
            }

            using (writer.Block("if (chunk.Count > 0)"))
            {
                writer.Line("batch = BuildRecordBatch(chunk, chunk.Count);");
                writer.Line("chunk.Clear();");
                writer.Line("yield return batch;");
            }
        }

        writer.Line();
        using (
            writer.Block(
                $"private static {Arrow}.RecordBatch BuildRecordBatch({rowsType} rows, int count)"
            )
        )
        {
            writer.Line($"var columns = new {Arrow}.IArrowArray[{context.Plan.Fields.Count}];");
            // A later column can throw (a null in a non-nullable member, a value out of range);
            // the columns already built are then owned by nobody, so release them here.
            using (writer.Block("try"))
            {
                foreach (FieldPlan field in context.Plan.Fields)
                {
                    writer.Line($"columns[{field.Ordinal}] = {ColumnMethod(field)}(rows, count);");
                }

                writer.Line($"return new {Arrow}.RecordBatch(Schema, columns, count);");
            }

            using (writer.Block("catch"))
            {
                using (writer.Block("foreach (var column in columns)"))
                {
                    writer.Line("column?.Dispose();");
                }

                writer.Line("throw;");
            }
        }

        foreach (FieldPlan field in context.Plan.Fields)
        {
            writer.Line();
            EmitColumn(writer, context, field, rowsType);
            if (field.Adapter is not null)
            {
                writer.Line();
                EmitStoreAccessor(writer, context, field);
            }
        }
    }

    private static string ColumnMethod(FieldPlan field) => "BuildColumn_" + field.MemberName;

    /// <summary>
    /// The expression producing a row's storage value: the member itself, or the member passed
    /// through its adapter's <c>ToStorage</c> (by the <c>Store_</c> accessor, which keeps nulls away
    /// from the adapter).
    /// </summary>
    private static string Access(FieldPlan field) =>
        field.Adapter is null
            ? $"row.{field.MemberIdentifier}"
            : $"{StoreMethod(field)}(row, index)";

    private static string StoreMethod(FieldPlan field) => "Store_" + field.MemberName;

    private static void EmitStoreAccessor(
        SourceWriter writer,
        EmissionContext context,
        FieldPlan field
    )
    {
        AdapterPlan adapter = field.Adapter!;
        string storage = field.IsNullable ? field.ClrType + "?" : field.ClrType;
        writer.Line(
            $"// {SourceNames.CommentText(field.FieldName)}: stored through {adapter.AdapterType}"
        );
        using (
            writer.Block(
                $"private static {storage} {StoreMethod(field)}({context.ModelType} row, int index)"
            )
        )
        {
            if (field.IsNullable)
            {
                writer.Line(
                    $"return row.{field.MemberIdentifier} is {{ }} domain ? ({field.ClrType}?){adapter.AdapterType}.ToStorage(domain) : null;"
                );
            }
            else if (adapter.DomainIsValueType)
            {
                writer.Line(
                    $"return {adapter.AdapterType}.ToStorage(row.{field.MemberIdentifier});"
                );
            }
            else
            {
                writer.Line(
                    $"if (row.{field.MemberIdentifier} is {{ }} domain) return {adapter.AdapterType}.ToStorage(domain);"
                );
                writer.Line($"ThrowRequiredNull(index, {field.FieldNameLiteral});");
                writer.Line("return default!;");
            }
        }
    }

    private static void EmitColumn(
        SourceWriter writer,
        EmissionContext context,
        FieldPlan field,
        string rowsType
    )
    {
        writer.Line(
            $"// {SourceNames.CommentText(field.FieldName)}: {ArrowTypes.Describe(field.Leaf)}"
        );
        using (
            writer.Block(
                $"private static {Arrow}.IArrowArray {ColumnMethod(field)}({rowsType} rows, int count)"
            )
        )
        {
            switch (field.Leaf.Conversion)
            {
                case ConversionStrategy.Utf8:
                    EmitBuilderColumn(
                        writer,
                        context,
                        field,
                        $"new {Arrow}.StringArray.Builder()",
                        "Append(value)"
                    );
                    break;
                case ConversionStrategy.Binary:
                    EmitBuilderColumn(
                        writer,
                        context,
                        field,
                        $"new {Arrow}.BinaryArray.Builder()",
                        "Append((global::System.ReadOnlySpan<byte>)value)"
                    );
                    break;
                case ConversionStrategy.Decimal128:
                    EmitBuilderColumn(
                        writer,
                        context,
                        field,
                        $"new {Arrow}.Decimal128Array.Builder(({ArrowTypes.Types}.Decimal128Type)Schema.FieldsList[{field.Ordinal}].DataType)",
                        "Append(value)"
                    );
                    break;
                default:
                    EmitBufferColumn(writer, context, field);
                    break;
            }
        }
    }

    /// <summary>
    /// Fixed-width columns (numbers, temporal values, booleans, GUIDs): values and validity go
    /// straight into Arrow buffers, and the array is assembled from them.
    /// </summary>
    private static void EmitBufferColumn(
        SourceWriter writer,
        EmissionContext context,
        FieldPlan field
    )
    {
        bool isBoolean = field.Leaf.Conversion == ConversionStrategy.Boolean;
        bool isGuid = field.Leaf.Conversion == ConversionStrategy.GuidBigEndian;
        string valuesBuilder =
            isBoolean ? $"new {Arrow}.ArrowBuffer.BitmapBuilder(count)"
            : isGuid ? $"new {Arrow}.ArrowBuffer.Builder<byte>(checked(count * 16))"
            : $"new {Arrow}.ArrowBuffer.Builder<{field.Leaf.StorageType}>(count)";

        writer.Line($"var values = {valuesBuilder};");
        if (field.IsNullable)
        {
            writer.Line($"var validity = new {Arrow}.ArrowBuffer.BitmapBuilder(count);");
            writer.Line("int nulls = 0;");
        }

        writer.Line("int index = 0;");
        using (writer.Block("foreach (var row in rows)"))
        {
            EmitRowPreamble(writer, context);
            if (field.IsNullable)
            {
                using (writer.Block($"if ({Access(field)} is {{ }} value)"))
                {
                    writer.Line(AppendValue(field, "value"));
                    writer.Line("validity.Append(true);");
                }

                using (writer.Block("else"))
                {
                    writer.Line(AppendDefault(field));
                    writer.Line("validity.Append(false);");
                    writer.Line("nulls++;");
                }
            }
            else
            {
                writer.Line($"var value = {Access(field)};");
                writer.Line(AppendValue(field, "value"));
            }

            writer.Line("index++;");
        }

        writer.Line("if (index != count) ThrowCollectionChanged();");
        string validityBuffer = field.IsNullable
            ? $"nulls == 0 ? {Arrow}.ArrowBuffer.Empty : validity.Build()"
            : $"{Arrow}.ArrowBuffer.Empty";
        string nullCount = field.IsNullable ? "nulls" : "0";
        writer.Line(
            $"var data = new {Arrow}.ArrayData(Schema.FieldsList[{field.Ordinal}].DataType, count, {nullCount}, 0, new[] {{ {validityBuffer}, values.Build() }});"
        );
        writer.Line($"return {Arrow}.ArrowArrayFactory.BuildArray(data);");
    }

    /// <summary>
    /// Variable-width and decimal columns go through Apache.Arrow's own builders, which own the
    /// offset and encoding rules; reserving the row count keeps them from growing their offsets.
    /// </summary>
    private static void EmitBuilderColumn(
        SourceWriter writer,
        EmissionContext context,
        FieldPlan field,
        string builder,
        string append
    )
    {
        writer.Line($"var builder = {builder};");
        writer.Line("builder.Reserve(count);");
        writer.Line("int index = 0;");
        using (writer.Block("foreach (var row in rows)"))
        {
            EmitRowPreamble(writer, context);
            if (!field.IsNullable && field.IsValueType)
            {
                writer.Line($"var value = {Access(field)};");
                EmitAppend(writer, field, append);
            }
            else
            {
                using (writer.Block($"if ({Access(field)} is {{ }} value)"))
                {
                    EmitAppend(writer, field, append);
                }

                using (writer.Block("else"))
                {
                    writer.Line(
                        field.IsNullable
                            ? "builder.AppendNull();"
                            : $"ThrowRequiredNull(index, {field.FieldNameLiteral});"
                    );
                }
            }

            writer.Line("index++;");
        }

        writer.Line("if (index != count) ThrowCollectionChanged();");
        writer.Line("return builder.Build();");
    }

    private static void EmitAppend(SourceWriter writer, FieldPlan field, string append)
    {
        if (field.Leaf.Conversion != ConversionStrategy.Decimal128)
        {
            writer.Line($"builder.{append};");
            return;
        }

        // Apache.Arrow refuses a decimal that does not fit rather than rounding it; this adds the
        // row and field to that refusal instead of letting a bare OverflowException escape.
        using (writer.Block("try"))
        {
            writer.Line($"builder.{append};");
        }

        using (writer.Block("catch (global::System.OverflowException exception)"))
        {
            writer.Line(
                $"ThrowDoesNotFit(index, {field.FieldNameLiteral}, {SourceNames.StringLiteral(ArrowTypes.Describe(field.Leaf))}, exception);"
            );
        }
    }

    private static void EmitRowPreamble(SourceWriter writer, EmissionContext context)
    {
        writer.Line("if (index >= count) ThrowCollectionChanged();");
        if (!context.Target.IsValueType)
        {
            writer.Line("if (row is null) ThrowNullRow(index);");
        }
    }

    private static string AppendValue(FieldPlan field, string value) =>
        field.Leaf.Conversion switch
        {
            ConversionStrategy.Direct => $"values.Append({value});",
            ConversionStrategy.EnumUnderlying =>
                $"values.Append(({field.Leaf.StorageType}){value});",
            ConversionStrategy.Boolean => $"values.Append({value});",
            ConversionStrategy.DateOnlyDays =>
                $"values.Append({value}.DayNumber - UnixEpochDayNumber);",
            ConversionStrategy.TimeOnlyMicroseconds => $"values.Append({value}.Ticks / 10L);",
            ConversionStrategy.DateTimeWallClockMicroseconds =>
                $"values.Append({value}.Ticks / 10L - UnixEpochMicroseconds);",
            ConversionStrategy.DateTimeOffsetUtcMicroseconds =>
                $"values.Append({value}.UtcTicks / 10L - UnixEpochMicroseconds);",
            ConversionStrategy.TimeSpanMicroseconds => $"values.Append({value}.Ticks / 10L);",
            ConversionStrategy.GuidBigEndian => $"AppendGuidBigEndian(values, {value});",
            _ => throw new System.ArgumentOutOfRangeException(nameof(field)),
        };

    private static string AppendDefault(FieldPlan field) =>
        field.Leaf.Conversion switch
        {
            ConversionStrategy.Boolean => "values.Append(false);",
            ConversionStrategy.GuidBigEndian => "values.Append(GuidZeroBytes);",
            _ => $"values.Append(default({field.Leaf.StorageType}));",
        };
}
