using Arrow.SourceGenerator;

namespace Arrow.SourceGenerator.AotTest;

/// <summary>
/// The model the AOT matrix drives through every generated capability: nullable values,
/// string/binary, decimal and temporal types (docs/00-DESIGN-GOALS.md section 27).
/// </summary>
[ArrowSerializable]
public partial class AotOrder
{
    public long Id { get; set; }
    public string Customer { get; set; } = "";
    public int? Quantity { get; set; }
    public byte[]? Attachment { get; set; }

    [ArrowDecimal(18, 4)]
    public decimal Total { get; set; }
    public DateOnly Day { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public TimeSpan? Latency { get; set; }
    public Guid Reference { get; set; }
}
