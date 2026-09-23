using System;
using Arrow.SourceGenerator;
using Golden.Adapters;

[assembly: ArrowTypeAdapter(typeof(SkuAdapter))]

namespace Golden.Adapters;

public readonly record struct Sku(int Value);

public sealed record Tag(string Name);

public static class SkuAdapter
{
    public static int ToStorage(this Sku value) => value.Value;

    public static Sku FromStorage(int storage) => new(storage);
}

public static class TagAdapter
{
    public static string ToStorage(Tag value) => value.Name;

    public static Tag FromStorage(string storage) => new(storage);
}

public static class UnixSeconds
{
    public static long ToStorage(DateTimeOffset value) => value.ToUnixTimeSeconds();

    public static DateTimeOffset FromStorage(long storage) =>
        DateTimeOffset.FromUnixTimeSeconds(storage);
}

/// <summary>Registered, nullable, explicit, and built-in-overriding adapters.</summary>
[ArrowSerializable]
public partial class AdapterEvent
{
    public Sku Sku { get; set; }
    public Sku? Replaces { get; set; }

    [ArrowAdapter(typeof(TagAdapter))]
    public Tag Label { get; set; } = new("none");

    [ArrowAdapter(typeof(TagAdapter))]
    public Tag? Alias { get; set; }

    [ArrowAdapter(typeof(UnixSeconds))]
    public DateTimeOffset At { get; set; }
}
