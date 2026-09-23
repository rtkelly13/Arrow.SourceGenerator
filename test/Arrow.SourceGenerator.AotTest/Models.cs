using Arrow.SourceGenerator;

namespace Arrow.SourceGenerator.AotTest;

/// <summary>The model the AOT matrix drives through every generated capability.</summary>
[ArrowSerializable]
public partial class AotOrder
{
    public long Id { get; set; }
    public string Customer { get; set; } = "";
    public int? Quantity { get; set; }
}
