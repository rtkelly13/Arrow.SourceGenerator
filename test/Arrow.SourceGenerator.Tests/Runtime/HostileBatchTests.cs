using Apache.Arrow;
using Apache.Arrow.Types;
using Arrow.SourceGenerator.Tests.Models;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Runtime;

/// <summary>
/// Batches that do not match the generated schema, or are structurally malformed, fail with a
/// clear <see cref="InvalidDataException"/> — never a cast exception, an index exception deep in
/// Apache.Arrow, or a silently coerced value (docs/00-DESIGN-GOALS.md sections 16 and 33).
/// </summary>
public sealed class HostileBatchTests
{
    private static readonly Decimal128Type Money = new(18, 4);

    private static Int64Array Id(params long[] values) =>
        new Int64Array.Builder().AppendRange(values).Build();

    private static StringArray Customer(params string?[] values)
    {
        var builder = new StringArray.Builder();
        foreach (string? value in values)
        {
            if (value is null)
            {
                builder.AppendNull();
            }
            else
            {
                builder.Append(value);
            }
        }

        return builder.Build();
    }

    private static Decimal128Array Total(params decimal[] values)
    {
        var builder = new Decimal128Array.Builder(Money);
        foreach (decimal value in values)
        {
            builder.Append(value);
        }

        return builder.Build();
    }

    private static RecordBatch Batch(
        params (string Name, IArrowType Type, IArrowArray Array)[] columns
    )
    {
        var schema = new Schema.Builder();
        foreach (var column in columns)
        {
            schema.Field(f => f.Name(column.Name).DataType(column.Type));
        }

        return new RecordBatch(
            schema.Build(),
            columns.Select(c => c.Array),
            columns.Length == 0 ? 0 : columns[0].Array.Length
        );
    }

    private static string Rejection(RecordBatch batch) =>
        Should.Throw<InvalidDataException>(() => OrderArrow.FromRecordBatch(batch)).Message;

    [Fact]
    public void MissingField()
    {
        using RecordBatch batch = Batch(
            ("Id", Int64Type.Default, Id(1)),
            ("Customer", StringType.Default, Customer("a"))
        );
        Rejection(batch).ShouldContain("field 'Total' is missing");
    }

    [Fact]
    public void DuplicateFieldIsAmbiguous()
    {
        using RecordBatch batch = Batch(
            ("Id", Int64Type.Default, Id(1)),
            ("Id", Int64Type.Default, Id(2)),
            ("Customer", StringType.Default, Customer("a")),
            ("Total", Money, Total(1m))
        );
        Rejection(batch).ShouldContain("field 'Id' appears 2 times");
    }

    [Fact]
    public void WrongTypeIsNotCoerced()
    {
        using RecordBatch batch = Batch(
            ("Id", Int32Type.Default, new Int32Array.Builder().Append(1).Build()),
            ("Customer", StringType.Default, Customer("a")),
            ("Total", Money, Total(1m))
        );
        Rejection(batch).ShouldContain("field 'Id' expected Int64, found int32");
    }

    [Fact]
    public void DecimalParametersMustMatchExactly()
    {
        var other = new Decimal128Type(18, 2);
        using RecordBatch batch = Batch(
            ("Id", Int64Type.Default, Id(1)),
            ("Customer", StringType.Default, Customer("a")),
            ("Total", other, new Decimal128Array.Builder(other).Append(1m).Build())
        );
        Rejection(batch).ShouldContain("expected Decimal128(18, 4), found Decimal128(18, 2)");
    }

    [Fact]
    public void NullsInANonNullableFieldAreRejected()
    {
        var ids = new Int64Array.Builder().Append(1).AppendNull().Build();
        using RecordBatch batch = Batch(
            ("Id", Int64Type.Default, ids),
            ("Customer", StringType.Default, Customer("a", "b")),
            ("Total", Money, Total(1m, 2m))
        );
        Rejection(batch).ShouldContain("field 'Id' is non-nullable but holds 1 null value(s)");
    }

    /// <summary>
    /// Regression: a non-nullable field was checked with NullCount alone, so an array whose
    /// bitmap marks a null while claiming NullCount 0 was read, and the undefined value slot
    /// materialised as a legitimate value.
    /// </summary>
    [Fact]
    public void NullCountThatDisagreesWithTheValidityBitmapIsRejected()
    {
        // Bit 0 unset (row 0 null), bit 1 set; the array nevertheless claims no nulls.
        var validity = new ArrowBuffer(new byte[] { 0b10, 0, 0, 0, 0, 0, 0, 0 });
        var values = new ArrowBuffer(new byte[16]);
        var hostile = new Int64Array(values, validity, 2, nullCount: 0, offset: 0);
        using RecordBatch batch = Batch(
            ("Id", Int64Type.Default, hostile),
            ("Customer", StringType.Default, Customer("a", "b")),
            ("Total", Money, Total(1m, 2m))
        );

        Rejection(batch)
            .ShouldContain("field 'Id' declares 0 null value(s) but its validity bitmap marks 1");
    }

    [Fact]
    public void SchemaFieldWithDifferentTypeParametersIsRejected()
    {
        // Same type id, different parameters: the schema claims Decimal128(18, 2) over the
        // mapped (18, 4) data.
        var schema = new Schema.Builder()
            .Field(f => f.Name("Id").DataType(Int64Type.Default))
            .Field(f => f.Name("Customer").DataType(StringType.Default))
            .Field(f => f.Name("Total").DataType(new Decimal128Type(18, 2)))
            .Build();
        using var batch = new RecordBatch(schema, [Id(1), Customer("a"), Total(1m)], 1);

        Rejection(batch)
            .ShouldContain(
                "field 'Total' has a schema field whose type disagrees with its column: expected Decimal128(18, 4), found Decimal128(18, 2)"
            );
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        using RecordBatch batch = Batch(("Id", StringType.Default, Customer("x")));
        string message = Rejection(batch);

        message.ShouldContain("field 'Id' expected Int64");
        message.ShouldContain("field 'Customer' is missing");
        message.ShouldContain("field 'Total' is missing");
    }

    [Fact]
    public void DictionaryEncodedColumnsAreRejectedWithGuidance()
    {
        var dictionaryType = new DictionaryType(Int32Type.Default, StringType.Default, false);
        var indices = new Int32Array.Builder().Append(0).Build();
        var dictionaryArray = new DictionaryArray(dictionaryType, indices, Customer("a"));
        using RecordBatch batch = Batch(
            ("Id", Int64Type.Default, Id(1)),
            ("Customer", dictionaryType, dictionaryArray),
            ("Total", Money, Total(1m))
        );
        Rejection(batch).ShouldContain("is dictionary-encoded");
    }

    [Fact]
    public void SchemaAndArrayThatDisagreeAreRejected()
    {
        // The schema claims Int64 but the column is a string array: a cast would throw deep inside
        // the reader without this check.
        var schema = new Schema.Builder()
            .Field(f => f.Name("Id").DataType(StringType.Default))
            .Field(f => f.Name("Customer").DataType(StringType.Default))
            .Field(f => f.Name("Total").DataType(Money))
            .Build();
        using var batch = new RecordBatch(schema, [Id(1), Customer("a"), Total(1m)], 1);
        Rejection(batch)
            .ShouldContain("is declared as utf8 by the schema but its column holds int64");
    }

    [Fact]
    public void ColumnLengthMustMatchTheBatch()
    {
        var schema = OrderArrow.Schema;
        using var batch = new RecordBatch(schema, [Id(1, 2), Customer("a"), Total(1m, 2m)], 2);
        Rejection(batch).ShouldContain("field 'Customer' has 1 values but the batch has 2 rows");
    }

    [Fact]
    public void MalformedOffsetsAreRejectedBeforeAnyValueIsRead()
    {
        // Offsets 0, 5, 2: decreasing, and 5 runs past a 3-byte value buffer.
        var offsets = new ArrowBuffer.Builder<int>().Append(0).Append(5).Append(2).Build();
        var values = new ArrowBuffer.Builder<byte>()
            .Append((byte)'a')
            .Append((byte)'b')
            .Append((byte)'c')
            .Build();
        var hostile = new StringArray(
            new ArrayData(StringType.Default, 2, 0, 0, [ArrowBuffer.Empty, offsets, values])
        );
        using var batch = new RecordBatch(OrderArrow.Schema, [Id(1, 2), hostile, Total(1m, 2m)], 2);

        Rejection(batch).ShouldContain("has offsets that decrease or run past its value buffer");
    }

    [Fact]
    public void ShortValueBufferIsRejected()
    {
        // Eight bytes — one Int64 — for a two-row column. (Builders pad to 64 bytes, so the buffer
        // is made directly.)
        var values = new ArrowBuffer(new byte[8]);
        var hostile = new Int64Array(values, ArrowBuffer.Empty, 2, 0, 0);
        using var batch = new RecordBatch(
            OrderArrow.Schema,
            [hostile, Customer("a", "b"), Total(1m, 2m)],
            2
        );

        Rejection(batch).ShouldContain("has a value buffer of 8 bytes where 16 are required");
    }

    [Theory]
    [InlineData("WallClock", "with a timezone", true)]
    [InlineData("Instant", "wall-clock timestamp", false)]
    public void TimestampTimezoneSemanticsAreEnforced(
        string field,
        string fragment,
        bool withTimezone
    )
    {
        using RecordBatch valid = ScalarRowArrow.ToRecordBatch([ToRecordBatchTests.Sample(1)]);
        int index = valid.Schema.GetFieldIndex(field);
        var swapped = new TimestampType(TimeUnit.Microsecond, withTimezone ? "UTC" : null);
        using RecordBatch batch = ReplaceColumn(
            valid,
            index,
            swapped,
            new TimestampArray(
                swapped,
                ((TimestampArray)valid.Column(index)).ValueBuffer,
                ArrowBuffer.Empty,
                1,
                0,
                0
            )
        );

        string message = Should
            .Throw<InvalidDataException>(() => ScalarRowArrow.FromRecordBatch(batch))
            .Message;
        message.ShouldContain($"field '{field}'");
        message.ShouldContain(
            fragment.Contains("wall", StringComparison.Ordinal)
                ? "found a wall-clock timestamp"
                : "found timezone 'UTC'"
        );
    }

    [Fact]
    public void TimestampUnitMustMatch()
    {
        using RecordBatch valid = ScalarRowArrow.ToRecordBatch([ToRecordBatchTests.Sample(1)]);
        int index = valid.Schema.GetFieldIndex("Instant");
        var millis = new TimestampType(TimeUnit.Millisecond, "UTC");
        using RecordBatch batch = ReplaceColumn(
            valid,
            index,
            millis,
            new TimestampArray(
                millis,
                ((TimestampArray)valid.Column(index)).ValueBuffer,
                ArrowBuffer.Empty,
                1,
                0,
                0
            )
        );

        Should
            .Throw<InvalidDataException>(() => ScalarRowArrow.FromRecordBatch(batch))
            .Message.ShouldContain("expected unit Microsecond, found Millisecond");
    }

    [Theory]
    [InlineData("Day", int.MaxValue, "System.DateOnly")]
    [InlineData("Time", -1L, "System.TimeOnly")]
    [InlineData("WallClock", long.MaxValue, "System.DateTime")]
    [InlineData("Instant", long.MinValue, "System.DateTimeOffset")]
    [InlineData("Elapsed", long.MaxValue, "System.TimeSpan")]
    public void OutOfRangeTemporalValuesNameTheRowAndField(string field, long raw, string clrType)
    {
        using RecordBatch valid = ScalarRowArrow.ToRecordBatch([ToRecordBatchTests.Sample(1)]);
        int index = valid.Schema.GetFieldIndex(field);
        IArrowType type = valid.Schema.FieldsList[index].DataType;
        ArrowBuffer buffer =
            type.TypeId == ArrowTypeId.Date32
                ? new ArrowBuffer.Builder<int>().Append((int)raw).Build()
                : new ArrowBuffer.Builder<long>().Append(raw).Build();
        IArrowArray hostile = ArrowArrayFactory.BuildArray(
            new ArrayData(type, 1, 0, 0, [ArrowBuffer.Empty, buffer])
        );
        using RecordBatch batch = ReplaceColumn(valid, index, type, hostile);

        string message = Should
            .Throw<InvalidDataException>(() => ScalarRowArrow.FromRecordBatch(batch))
            .Message;
        message.ShouldBe(
            $"Row 0: the value in Arrow field '{field}' is outside the range of {clrType}."
        );
    }

    /// <summary>
    /// Regression: Apache.Arrow's <c>Decimal128Array.GetValue</c> rounds a 38-digit value into
    /// System.Decimal without complaint. The generated reader must refuse it instead.
    /// </summary>
    [Fact]
    public void Decimal128BeyondSystemDecimalIsReportedNotRounded()
    {
        var wide = new Decimal128Type(38, 18);
        var builder = new Decimal128Array.Builder(wide);
        builder.Append(
            System.Data.SqlTypes.SqlDecimal.Parse("99999999999999999999.999999999999999999")
        );
        using RecordBatch valid = ScalarRowArrow.ToRecordBatch([ToRecordBatchTests.Sample(1)]);
        int index = valid.Schema.GetFieldIndex("Money");
        using RecordBatch batch = ReplaceColumn(valid, index, wide, builder.Build());

        Should
            .Throw<InvalidDataException>(() => ScalarRowArrow.FromRecordBatch(batch))
            .Message.ShouldBe(
                "Row 0: the value in Arrow field 'Money' is outside the range of System.Decimal."
            );
    }

    /// <summary>
    /// Regression: an unscaled integer wider than 96 bits can still be exact once its trailing
    /// decimal zeros are stripped. At the default (38, 18), 10^19 is stored as 10^37.
    /// </summary>
    [Theory]
    [InlineData("10000000000000000000.000000000000000000", "10000000000000000000")]
    [InlineData("-12345678901234567890.000000000000000000", "-12345678901234567890")]
    [InlineData("12345678901234567890.123400000000000000", "12345678901234567890.1234")]
    public void Decimal128WiderThan96BitsWithTrailingZerosIsExact(string stored, string expected)
    {
        using RecordBatch valid = ScalarRowArrow.ToRecordBatch([ToRecordBatchTests.Sample(1)]);
        using RecordBatch batch = WithMoney(valid, stored);

        ScalarRowArrow
            .FromRecordBatch(batch)[0]
            .Money.ShouldBe(
                decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)
            );
    }

    [Fact]
    public void Decimal128StillTooWideAfterStrippingZerosIsReportedNotRounded()
    {
        // 30 significant digits after the trailing zeros go: one more than System.Decimal holds.
        using RecordBatch valid = ScalarRowArrow.ToRecordBatch([ToRecordBatchTests.Sample(1)]);
        using RecordBatch batch = WithMoney(valid, "12345678901234567890.123456789100000000");

        Should
            .Throw<InvalidDataException>(() => ScalarRowArrow.FromRecordBatch(batch))
            .Message.ShouldBe(
                "Row 0: the value in Arrow field 'Money' is outside the range of System.Decimal."
            );
    }

    /// <summary>Replaces the Money column; the result shares <paramref name="valid"/>'s other columns.</summary>
    private static RecordBatch WithMoney(RecordBatch valid, string value)
    {
        var type = new Decimal128Type(38, 18);
        var builder = new Decimal128Array.Builder(type);
        builder.Append(System.Data.SqlTypes.SqlDecimal.Parse(value));
        return ReplaceColumn(valid, valid.Schema.GetFieldIndex("Money"), type, builder.Build());
    }

    [Fact]
    public void Decimal128AtTheSystemDecimalLimitsIsExact()
    {
        var row = ToRecordBatchTests.Sample(1);
        var type = new Decimal128Type(38, 0);
        var builder = new Decimal128Array.Builder(type)
            .Append(decimal.MaxValue)
            .Append(decimal.MinValue);
        using RecordBatch valid = ScalarRowArrow.ToRecordBatch([row, row]);
        int index = valid.Schema.GetFieldIndex("Money");

        // Reuse the column bytes of a (38, 0) array under the model's (38, 18) type: the unscaled
        // integers are 2^96 - 1 in magnitude, the largest the reader accepts.
        Decimal128Array limits = builder.Build();
        var retyped = new Decimal128Array(
            new ArrayData(
                valid.Schema.FieldsList[index].DataType,
                2,
                0,
                0,
                [ArrowBuffer.Empty, limits.ValueBuffer]
            )
        );
        using RecordBatch batch = ReplaceColumn(
            valid,
            index,
            valid.Schema.FieldsList[index].DataType,
            retyped
        );

        ScalarRow[] read = ScalarRowArrow.FromRecordBatch(batch);
        read[0].Money.ShouldBe(decimal.MaxValue / 1_000_000_000_000_000_000m);
        read[1].Money.ShouldBe(decimal.MinValue / 1_000_000_000_000_000_000m);
    }

    private static RecordBatch ReplaceColumn(
        RecordBatch batch,
        int index,
        IArrowType type,
        IArrowArray array
    )
    {
        var schema = new Schema.Builder();
        var columns = new List<IArrowArray>();
        for (int i = 0; i < batch.ColumnCount; i++)
        {
            Field field = batch.Schema.FieldsList[i];
            schema.Field(f =>
                f.Name(field.Name)
                    .DataType(i == index ? type : field.DataType)
                    .Nullable(field.IsNullable)
            );
            columns.Add(i == index ? array : batch.Column(i));
        }

        return new RecordBatch(schema.Build(), columns, batch.Length);
    }
}
