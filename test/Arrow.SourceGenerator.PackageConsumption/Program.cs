using Arrow.SourceGenerator;

// Package consumption smoke test. Grows with each generated capability; it proves the packages
// restore from the local feed, the analyzer loads, and generated code compiles warning-free and
// behaves as documented when consumed the way an outside project consumes it.
string[] fields = ConsumerOrderArrow.Schema.FieldsList.Select(f => f.Name).ToArray();
if (!fields.SequenceEqual(["Id", "Customer"]))
{
    Console.Error.WriteLine($"Unexpected schema: {string.Join(", ", fields)}");
    return 1;
}

Console.WriteLine(
    $"Arrow.SourceGenerator package consumption: schema ({string.Join(", ", fields)}) OK"
);
return 0;

[ArrowSerializable]
public partial class ConsumerOrder
{
    public long Id { get; set; }
    public string? Customer { get; set; }
}
