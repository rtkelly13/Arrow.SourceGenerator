using Apache.Arrow;
using Apache.Arrow.Arrays;
using Arrow.SourceGenerator.Tests.Models;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Runtime;

/// <summary>
/// POCO → RecordBatch, inspected through Apache.Arrow's own typed arrays: exact stored values,
/// validity, and the documented failure modes.
/// </summary>
public sealed class ToRecordBatchTests
{
    internal static ScalarRow Sample(int seed) =>
        new()
        {
            Flag = seed % 2 == 0,
            I8 = (sbyte)-seed,
            U8 = (byte)seed,
            I16 = (short)(-1000 * seed),
            U16 = (ushort)(1000 * seed),
            I32 = int.MinValue + seed,
            U32 = uint.MaxValue - (uint)seed,
            I64 = long.MinValue + seed,
            U64 = ulong.MaxValue - (ulong)seed,
            F32 = 1.5f * seed,
            F64 = -2.25 * seed,
            Text = "text-" + seed + "-é中",
            Bytes = [1, 2, (byte)seed],
            Money = 12345.678901234567890123m + seed,
            Price = 99.99m + seed,
            Day = new DateOnly(2024, 2, 29).AddDays(seed),
            Time = new TimeOnly(13, 14, 15, 16, 17).Add(TimeSpan.FromMicroseconds(seed)),
            WallClock = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local).AddTicks(10 * seed),
            Instant = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(
                10 * seed
            ),
            Elapsed = TimeSpan.FromMicroseconds(-123456789 - seed),
            Id = Guid.Parse($"00112233-4455-6677-8899-aabbccddee{seed:x2}"),
            Level = seed % 2 == 0 ? Level.Low : Level.High,
            MaybeFlag = seed % 3 == 0 ? null : true,
            MaybeI32 = seed % 3 == 0 ? null : seed,
            MaybeF64 = seed % 3 == 0 ? null : seed / 3.0,
            MaybeText = seed % 3 == 0 ? null : "maybe" + seed,
            MaybeBytes = seed % 3 == 0 ? null : [(byte)seed],
            MaybeMoney = seed % 3 == 0 ? null : -seed,
            MaybeDay = seed % 3 == 0 ? null : new DateOnly(1969, 12, 31),
            MaybeTime = seed % 3 == 0 ? null : TimeOnly.MinValue,
            MaybeWallClock = seed % 3 == 0 ? null : DateTime.MinValue,
            MaybeInstant = seed % 3 == 0 ? null : DateTimeOffset.UnixEpoch,
            MaybeElapsed = seed % 3 == 0 ? null : TimeSpan.Zero,
            MaybeId = seed % 3 == 0 ? null : Guid.Empty,
            MaybeLevel = seed % 3 == 0 ? null : Level.High,
        };

    private static T Column<T>(RecordBatch batch, string name)
        where T : IArrowArray => (T)batch.Column(batch.Schema.GetFieldIndex(name));

    [Fact]
    public void BatchUsesTheGeneratedSchemaAndRowCount()
    {
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([Sample(1), Sample(2), Sample(3)]);

        batch.Length.ShouldBe(3);
        ReferenceEquals(batch.Schema, ScalarRowArrow.Schema).ShouldBeTrue();
        for (int i = 0; i < batch.ColumnCount; i++)
        {
            batch.Column(i).Length.ShouldBe(3);
            batch
                .Column(i)
                .Data.DataType.TypeId.ShouldBe(batch.Schema.FieldsList[i].DataType.TypeId);
        }
    }

    [Fact]
    public void FixedWidthValuesAreStoredExactly()
    {
        ScalarRow row = Sample(2);
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([row]);

        Column<BooleanArray>(batch, "Flag").GetValue(0).ShouldBe(true);
        Column<Int8Array>(batch, "I8").GetValue(0).ShouldBe(row.I8);
        Column<UInt8Array>(batch, "U8").GetValue(0).ShouldBe(row.U8);
        Column<Int16Array>(batch, "I16").GetValue(0).ShouldBe(row.I16);
        Column<UInt16Array>(batch, "U16").GetValue(0).ShouldBe(row.U16);
        Column<Int32Array>(batch, "I32").GetValue(0).ShouldBe(row.I32);
        Column<UInt32Array>(batch, "U32").GetValue(0).ShouldBe(row.U32);
        Column<Int64Array>(batch, "I64").GetValue(0).ShouldBe(row.I64);
        Column<UInt64Array>(batch, "U64").GetValue(0).ShouldBe(row.U64);
        Column<FloatArray>(batch, "F32").GetValue(0).ShouldBe(row.F32);
        Column<DoubleArray>(batch, "F64").GetValue(0).ShouldBe(row.F64);
        Column<Int16Array>(batch, "Level").GetValue(0).ShouldBe((short)row.Level);
    }

    [Fact]
    public void VariableWidthAndDecimalValuesAreStoredExactly()
    {
        ScalarRow row = Sample(5);
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([row]);

        Column<StringArray>(batch, "Text").GetString(0).ShouldBe(row.Text);
        Column<BinaryArray>(batch, "Bytes").GetBytes(0).ToArray().ShouldBe(row.Bytes);
        Column<Decimal128Array>(batch, "Money").GetValue(0).ShouldBe(row.Money);
        Column<Decimal128Array>(batch, "Price").GetValue(0).ShouldBe(row.Price);
    }

    [Fact]
    public void TemporalValuesFollowTheDocumentedEncoding()
    {
        ScalarRow row = Sample(0);
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([row]);

        // Date32: days since 1970-01-01.
        Column<Date32Array>(batch, "Day")
            .Values[0]
            .ShouldBe(row.Day.DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber);
        // Time64(µs): microseconds since midnight.
        Column<Time64Array>(batch, "Time").Values[0].ShouldBe(row.Time.Ticks / 10);
        // DateTime is wall clock: stored as written, Local kind notwithstanding.
        Column<TimestampArray>(batch, "WallClock")
            .Values[0]
            .ShouldBe((row.WallClock.Ticks - DateTime.UnixEpoch.Ticks) / 10);
        // DateTimeOffset is an instant: stored as UTC.
        Column<TimestampArray>(batch, "Instant")
            .Values[0]
            .ShouldBe(row.Instant.ToUnixTimeMilliseconds() * 1000);
        Column<DurationArray>(batch, "Elapsed").Values[0].ShouldBe(row.Elapsed.Ticks / 10);
    }

    [Fact]
    public void PreEpochAndBoundaryTemporalValuesEncode()
    {
        ScalarRow row = Sample(1);
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([row]);

        Column<Date32Array>(batch, "MaybeDay").Values[0].ShouldBe(-1);
        Column<TimestampArray>(batch, "MaybeWallClock").Values[0].ShouldBe(-62135596800000000L);
        Column<TimestampArray>(batch, "MaybeInstant").Values[0].ShouldBe(0);
    }

    [Fact]
    public void GuidIsStoredInRfc4122ByteOrder()
    {
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([Sample(0xff)]);

        Column<FixedSizeBinaryArray>(batch, "Id")
            .GetBytes(0)
            .ToArray()
            .ShouldBe(Convert.FromHexString("00112233445566778899aabbccddeeff"));
    }

    [Fact]
    public void NullsReachTheValidityBitmapAndAllValidColumnsOmitIt()
    {
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([Sample(1), Sample(3), Sample(4)]);

        foreach (
            string name in new[]
            {
                "MaybeFlag",
                "MaybeI32",
                "MaybeText",
                "MaybeBytes",
                "MaybeMoney",
                "MaybeId",
                "MaybeLevel",
                "MaybeInstant",
            }
        )
        {
            IArrowArray column = batch.Column(batch.Schema.GetFieldIndex(name));
            column.NullCount.ShouldBe(1, name);
            column.IsNull(1).ShouldBeTrue(name);
            column.IsValid(0).ShouldBeTrue(name);
        }

        Int32Array required = Column<Int32Array>(batch, "I32");
        required.NullCount.ShouldBe(0);
        required
            .Data.Buffers[0]
            .IsEmpty.ShouldBeTrue("a column with no nulls carries no validity bitmap");
    }

    [Fact]
    public void EmptyCollectionProducesAValidZeroLengthBatch()
    {
        using RecordBatch batch = ScalarRowArrow.ToRecordBatch([]);

        batch.Length.ShouldBe(0);
        batch.ColumnCount.ShouldBe(ScalarRowArrow.Schema.FieldsList.Count);
    }

    [Fact]
    public void NullRowIsRejectedWithItsIndex()
    {
        var error = Should.Throw<ArgumentException>(() =>
            ScalarRowArrow.ToRecordBatch([Sample(1), null!])
        );
        error.Message.ShouldContain("Row 1 is null");
    }

    [Fact]
    public void NullInANonNullableReferenceMemberIsRejected()
    {
        ScalarRow row = Sample(1);
        row.Text = null!;

        var error = Should.Throw<ArgumentException>(() => ScalarRowArrow.ToRecordBatch([row]));
        error.Message.ShouldContain("non-nullable Arrow field 'Text'");
    }

    [Fact]
    public void DecimalThatDoesNotFitIsRejectedNotRounded()
    {
        ScalarRow tooPrecise = Sample(1);
        tooPrecise.Price = 1.234m; // Decimal128(10, 2)
        ScalarRow tooLarge = Sample(1);
        tooLarge.Price = 123456789012m;

        Should
            .Throw<ArgumentException>(() => ScalarRowArrow.ToRecordBatch([tooPrecise]))
            .Message.ShouldContain("does not fit Decimal128(10, 2)");
        Should
            .Throw<ArgumentException>(() => ScalarRowArrow.ToRecordBatch([tooLarge]))
            .InnerException.ShouldBeOfType<OverflowException>();
    }

    [Fact]
    public void CollectionWhoseCountDisagreesWithItsContentsIsRejected()
    {
        Should.Throw<InvalidOperationException>(() =>
            ScalarRowArrow.ToRecordBatch(new LyingCollection([Sample(1)], claimedCount: 2))
        );
        Should.Throw<InvalidOperationException>(() =>
            ScalarRowArrow.ToRecordBatch(
                new LyingCollection([Sample(1), Sample(2)], claimedCount: 1)
            )
        );
    }

    [Fact]
    public void ToRecordBatchesChunksLazilyAndYieldsNothingForAnEmptySequence()
    {
        int produced = 0;
        IEnumerable<ScalarRow> Rows()
        {
            for (int i = 0; i < 7; i++)
            {
                produced++;
                yield return Sample(i);
            }
        }

        IEnumerable<RecordBatch> batches = ScalarRowArrow.ToRecordBatches(Rows(), batchSize: 3);
        produced.ShouldBe(0, "nothing is read until a batch is requested");

        var lengths = new List<int>();
        foreach (RecordBatch batch in batches)
        {
            lengths.Add(batch.Length);
            batch.Dispose();
        }

        lengths.ShouldBe([3, 3, 1]);
        ScalarRowArrow.ToRecordBatches([], 3).ShouldBeEmpty();
    }

    /// <summary>
    /// Regression: the chunk was cleared only when the caller asked for the next batch, so rows
    /// already copied into a yielded batch stayed reachable while the caller held that batch.
    /// </summary>
    [Fact]
    public void RowsOfAYieldedBatchAreNotKeptAliveByTheIterator()
    {
        var tracked = new List<WeakReference>();

        using IEnumerator<RecordBatch> batches = ScalarRowArrow
            .ToRecordBatches(TrackedRows(tracked, count: 3), batchSize: 2)
            .GetEnumerator();
        batches.MoveNext().ShouldBeTrue();
        using RecordBatch first = batches.Current;
        first.Length.ShouldBe(2);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Row 1 is still the source iterator's (and foreach's) current element; row 0 is only
        // reachable through the chunk, which must already be empty.
        tracked[0].IsAlive.ShouldBeFalse();
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static IEnumerable<ScalarRow> TrackedRows(List<WeakReference> tracked, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return Track(tracked, i);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    private static ScalarRow Track(List<WeakReference> tracked, int seed)
    {
        ScalarRow row = Sample(seed);
        tracked.Add(new WeakReference(row));
        return row;
    }

    [Fact]
    public void ArgumentsAreValidatedEagerly()
    {
        Should.Throw<ArgumentNullException>(() => ScalarRowArrow.ToRecordBatch(null!));
        Should.Throw<ArgumentNullException>(() => ScalarRowArrow.ToRecordBatches(null!, 1));
        Should.Throw<ArgumentOutOfRangeException>(() => ScalarRowArrow.ToRecordBatches([], 0));
    }

    [Fact]
    public void PositionalRecordAndStructModelsConvert()
    {
        using RecordBatch orders = OrderArrow.ToRecordBatch([
            new Order(1, "a", 2.5m),
            new Order(2, null, 0m),
        ]);
        Column<StringArray>(orders, "Customer").IsNull(1).ShouldBeTrue();
        Column<Decimal128Array>(orders, "Total").GetValue(0).ShouldBe(2.5m);

        using RecordBatch points = PointArrow.ToRecordBatch([new Point { X = 1, Y = 2 }]);
        Column<Int32Array>(points, "Y").GetValue(0).ShouldBe(2);
    }

    private sealed class LyingCollection(IReadOnlyList<ScalarRow> rows, int claimedCount)
        : IReadOnlyCollection<ScalarRow>
    {
        public int Count => claimedCount;

        public IEnumerator<ScalarRow> GetEnumerator() => rows.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
