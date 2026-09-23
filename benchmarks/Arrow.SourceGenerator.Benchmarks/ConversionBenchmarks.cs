using Apache.Arrow;
using BenchmarkDotNet.Attributes;

namespace Arrow.SourceGenerator.Benchmarks;

/// <summary>
/// POCO ⇄ RecordBatch in both directions. Generated counterparts join each category as the
/// corresponding capability lands, so every comparison is against the hand-written baseline.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ConversionBenchmarks
{
    private BenchmarkRow[] _rows = [];
    private RecordBatch? _batch;

    [Params(10_000)]
    public int Rows { get; set; }

    [Params(0.0, 0.5)]
    public double NullDensity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rows = BenchmarkRow.Create(Rows, NullDensity, stringLength: 16);
        _batch = HandWrittenArrow.ToRecordBatch(_rows);
    }

    [GlobalCleanup]
    public void Cleanup() => _batch?.Dispose();

    [Benchmark(Baseline = true), BenchmarkCategory("ToRecordBatch")]
    public RecordBatch HandWritten_ToRecordBatch() => HandWrittenArrow.ToRecordBatch(_rows);

    [Benchmark, BenchmarkCategory("ToRecordBatch")]
    public RecordBatch Generated_ToRecordBatch() => BenchmarkRowArrow.ToRecordBatch(_rows);

    [Benchmark(Baseline = true), BenchmarkCategory("FromRecordBatch")]
    public BenchmarkRow[] HandWritten_FromRecordBatch() =>
        HandWrittenArrow.FromRecordBatch(_batch!);
}
