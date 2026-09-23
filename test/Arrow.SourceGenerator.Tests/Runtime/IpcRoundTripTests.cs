using Apache.Arrow;
using Apache.Arrow.Ipc;
using Arrow.SourceGenerator.Tests.Infrastructure;
using Arrow.SourceGenerator.Tests.Models;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Runtime;

/// <summary>
/// Independent validation (docs/00-DESIGN-GOALS.md section 28): Apache.Arrow's IPC writer and
/// reader reject malformed offsets, validity bitmaps and schema mismatches that in-memory
/// inspection can miss.
/// </summary>
public sealed class IpcRoundTripTests
{
    internal static RecordBatch RoundTrip(RecordBatch batch)
    {
        using var stream = new MemoryStream();
        using (var writer = new ArrowStreamWriter(stream, batch.Schema, leaveOpen: true))
        {
            writer.WriteRecordBatch(batch);
            writer.WriteEnd();
        }

        stream.Position = 0;
        using var reader = new ArrowStreamReader(stream);
        return reader.ReadNextRecordBatch().ShouldNotBeNull();
    }

    [Fact]
    public void GeneratedBatchSurvivesIpcWithEveryValueAndNullIntact()
    {
        ScalarRow[] rows = [.. Enumerable.Range(0, 50).Select(ToRecordBatchTests.Sample)];
        using RecordBatch original = ScalarRowArrow.ToRecordBatch(rows);
        using RecordBatch copy = RoundTrip(original);

        copy.Length.ShouldBe(original.Length);
        copy.Schema.FieldsList.Select(f => (f.Name, f.DataType.TypeId, f.IsNullable))
            .ShouldBe(
                original.Schema.FieldsList.Select(f => (f.Name, f.DataType.TypeId, f.IsNullable))
            );

        for (int c = 0; c < original.ColumnCount; c++)
        {
            IArrowArray expected = original.Column(c);
            IArrowArray actual = copy.Column(c);
            actual.NullCount.ShouldBe(expected.NullCount);
            for (int r = 0; r < rows.Length; r++)
            {
                actual
                    .IsNull(r)
                    .ShouldBe(expected.IsNull(r), $"{original.Schema.FieldsList[c].Name}[{r}]");
            }

            ArrowArrayEquality
                .ValuesEqual(expected, actual)
                .ShouldBeTrue(original.Schema.FieldsList[c].Name);
        }
    }

    [Fact]
    public void ZeroRowBatchSurvivesIpc()
    {
        using RecordBatch empty = ScalarRowArrow.ToRecordBatch([]);
        using RecordBatch copy = RoundTrip(empty);

        copy.Length.ShouldBe(0);
        copy.Schema.FieldsList.Count.ShouldBe(ScalarRowArrow.Schema.FieldsList.Count);
    }
}
