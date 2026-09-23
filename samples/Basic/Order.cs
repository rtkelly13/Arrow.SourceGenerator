using Arrow.SourceGenerator;

namespace Basic;

[ArrowSerializable]
public partial record Order(
    long Id,
    string? Customer,
    [ArrowDecimal(18, 2)] decimal Total,
    DateOnly Placed
);
