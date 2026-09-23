namespace Arrow.SourceGenerator.Benchmarks;

/// <summary>
/// A representative flat row: fixed-width, nullable, string and decimal columns. Its generated
/// schema is identical to <see cref="HandWrittenArrow.Schema"/>, so both sides do the same work.
/// </summary>
[ArrowSerializable]
public sealed partial class BenchmarkRow
{
    public long Id { get; init; }
    public int? Quantity { get; init; }
    public double Price { get; init; }
    public string Customer { get; init; } = "";
    public string? Note { get; init; }
    public decimal Total { get; init; }

    public static BenchmarkRow[] Create(int count, double nullDensity, int stringLength)
    {
        var random = new Random(20260923);
        var rows = new BenchmarkRow[count];
        string pad = new('x', Math.Max(0, stringLength - 8));
        for (int i = 0; i < count; i++)
        {
            bool isNull = random.NextDouble() < nullDensity;
            rows[i] = new BenchmarkRow
            {
                Id = i,
                Quantity = isNull ? null : random.Next(1, 1000),
                Price = random.NextDouble() * 100,
                Customer = $"c{i:D7}{pad}",
                Note = isNull ? null : "note",
                Total = Math.Round((decimal)(random.NextDouble() * 10_000), 2),
            };
        }

        return rows;
    }
}
