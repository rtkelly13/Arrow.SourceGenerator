using Arrow.SourceGenerator;

// Package consumption smoke test. Grows with each generated capability; it proves the packages
// restore from the local feed, the analyzer loads, and generated code compiles warning-free.
Console.WriteLine(
    $"Arrow.SourceGenerator package consumption: {nameof(ConsumerOrderArrow)} generated"
);
return 0;

[ArrowSerializable]
public partial class ConsumerOrder
{
    public long Id { get; set; }
    public string? Customer { get; set; }
}
