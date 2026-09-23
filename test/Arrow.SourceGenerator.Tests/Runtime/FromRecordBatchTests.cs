using Apache.Arrow;
using Apache.Arrow.Types;
using Arrow.SourceGenerator.Tests.Models;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Runtime;

/// <summary>RecordBatch → POCO: faithful round trips across every P0 kind and construction shape.</summary>
public sealed class FromRecordBatchTests
{
    internal static void ShouldRoundTrip(ScalarRow expected, ScalarRow actual)
    {
        actual.Flag.ShouldBe(expected.Flag);
        (actual.I8, actual.U8, actual.I16, actual.U16).ShouldBe(
            (expected.I8, expected.U8, expected.I16, expected.U16)
        );
        (actual.I32, actual.U32, actual.I64, actual.U64).ShouldBe(
            (expected.I32, expected.U32, expected.I64, expected.U64)
        );
        (actual.F32, actual.F64).ShouldBe((expected.F32, expected.F64));
        actual.Text.ShouldBe(expected.Text);
        actual.Bytes.ShouldBe(expected.Bytes);
        (actual.Money, actual.Price).ShouldBe((expected.Money, expected.Price));
        (actual.Day, actual.Time).ShouldBe((expected.Day, expected.Time));
        // Wall clock: the written value comes back exactly; Kind is not stored.
        actual.WallClock.Ticks.ShouldBe(expected.WallClock.Ticks);
        actual.WallClock.Kind.ShouldBe(DateTimeKind.Unspecified);
        // Instant: the same moment comes back, at offset zero.
        actual.Instant.UtcTicks.ShouldBe(expected.Instant.UtcTicks);
        actual.Instant.Offset.ShouldBe(TimeSpan.Zero);
        actual.Elapsed.ShouldBe(expected.Elapsed);
        actual.Id.ShouldBe(expected.Id);
        actual.Level.ShouldBe(expected.Level);
        actual.MaybeFlag.ShouldBe(expected.MaybeFlag);
        actual.MaybeI32.ShouldBe(expected.MaybeI32);
        actual.MaybeF64.ShouldBe(expected.MaybeF64);
        actual.MaybeText.ShouldBe(expected.MaybeText);
        actual.MaybeBytes.ShouldBe(expected.MaybeBytes);
        actual.MaybeMoney.ShouldBe(expected.MaybeMoney);
        actual.MaybeDay.ShouldBe(expected.MaybeDay);
        actual.MaybeTime.ShouldBe(expected.MaybeTime);
        ((long?)actual.MaybeWallClock?.Ticks).ShouldBe(expected.MaybeWallClock?.Ticks);
        ((long?)actual.MaybeInstant?.UtcTicks).ShouldBe(expected.MaybeInstant?.UtcTicks);
        actual.MaybeElapsed.ShouldBe(expected.MaybeElapsed);
        actual.MaybeId.ShouldBe(expected.MaybeId);
        actual.MaybeLevel.ShouldBe(expected.MaybeLevel);
    }

    [Fact]
    public void EveryP0KindRoundTripsIncludingNulls()
    {
        ScalarRow[] rows = [.. Enumerable.Range(0, 30).Select(ToRecordBatchTests.Sample)];
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch(rows);

        ScalarRow[] read = ScalarRowArrow.FromRecordBatch(batch);

        read.Length.ShouldBe(rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            ShouldRoundTrip(rows[i], read[i]);
        }
    }

    [Fact]
    public void IpcOriginatingBatchesMaterialise()
    {
        ScalarRow[] rows = [.. Enumerable.Range(0, 12).Select(ToRecordBatchTests.Sample)];
        using RecordBatch written = ScalarRowArrow.ToRecordBatch(rows);
        using RecordBatch fromIpc = IpcRoundTripTests.RoundTrip(written);

        ScalarRow[] read = ScalarRowArrow.FromRecordBatch(fromIpc);
        for (int i = 0; i < rows.Length; i++)
        {
            ShouldRoundTrip(rows[i], read[i]);
        }
    }

    [Fact]
    public void SubMicrosecondTicksTruncateAsDocumented()
    {
        ScalarRow row = ToRecordBatchTests.Sample(1);
        row.WallClock = new DateTime(2024, 1, 1).AddTicks(17);
        row.Elapsed = TimeSpan.FromTicks(-17);

        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([row]);
        ScalarRow read = ScalarRowArrow.FromRecordBatch(batch)[0];

        read.WallClock.ShouldBe(new DateTime(2024, 1, 1).AddTicks(10));
        read.Elapsed.ShouldBe(TimeSpan.FromTicks(-10));
    }

    [Fact]
    public void ConstructionShapesMaterialise()
    {
        using RecordBatch orders = OrderArrow.ToRecordBatch([
            new Order(1, "a", 2.5m),
            new Order(2, null, 0m),
        ]);
        OrderArrow
            .FromRecordBatch(orders)
            .ShouldBe([new Order(1, "a", 2.5m), new Order(2, null, 0m)]);

        using RecordBatch points = PointArrow.ToRecordBatch([new Point { X = 1, Y = 2 }]);
        PointArrow.FromRecordBatch(points).ShouldBe([new Point { X = 1, Y = 2 }]);

        using RecordBatch initOnly = InitOnlyRowArrow.ToRecordBatch([
            new InitOnlyRow { Name = "n", Rank = null },
        ]);
        InitOnlyRow read = InitOnlyRowArrow.FromRecordBatch(initOnly).Single();
        (read.Name, read.Rank).ShouldBe(("n", (int?)null));
    }

    [Fact]
    public void FieldsAreMatchedByNameSoOrderAndExtraFieldsDoNotMatter()
    {
        var schema = new Schema.Builder()
            .Field(f => f.Name("extra").DataType(BooleanType.Default))
            .Field(f => f.Name("Total").DataType(new Decimal128Type(18, 4)).Nullable(false))
            .Field(f => f.Name("Customer").DataType(StringType.Default))
            .Field(f => f.Name("Id").DataType(Int64Type.Default).Nullable(false))
            .Build();
        using var batch = new RecordBatch(
            schema,
            [
                new BooleanArray.Builder().Append(true).Build(),
                new Decimal128Array.Builder(new Decimal128Type(18, 4)).Append(9.5m).Build(),
                new StringArray.Builder().Append("x").Build(),
                new Int64Array.Builder().Append(42).Build(),
            ],
            1
        );

        OrderArrow.FromRecordBatch(batch).ShouldBe([new Order(42, "x", 9.5m)]);
    }

    [Fact]
    public void ZeroRowBatchYieldsAnEmptyArray()
    {
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([]);
        ScalarRowArrow.FromRecordBatch(batch).ShouldBeEmpty();
    }

    [Fact]
    public void FromRecordBatchesStreamsLazilyAndRejectsNullBatches()
    {
        using RecordBatch first = OrderArrow.ToRecordBatch([new Order(1, "a", 1m)]);
        using RecordBatch second = OrderArrow.ToRecordBatch([
            new Order(2, "b", 2m),
            new Order(3, "c", 3m),
        ]);

        OrderArrow.FromRecordBatches([first, second]).Select(o => o.Id).ShouldBe([1L, 2L, 3L]);

        IEnumerable<Order> lazy = OrderArrow.FromRecordBatches([first, null!]);
        Should.Throw<ArgumentException>(() => lazy.ToList());
        Should.Throw<ArgumentNullException>(() => OrderArrow.FromRecordBatches(null!));
        Should.Throw<ArgumentNullException>(() => OrderArrow.FromRecordBatch(null!));
    }

    [Fact]
    public void ReadingNeitherDisposesNorRetainsTheBatch()
    {
        RecordBatch batch = OrderArrow.ToRecordBatch([new Order(1, "a", 1m)]);
        OrderArrow.FromRecordBatch(batch);

        // Still usable afterwards: the reader copied what it needed.
        ((Int64Array)batch.Column(0))
            .GetValue(0)
            .ShouldBe(1);
        batch.Dispose();
    }
}
