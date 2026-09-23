using Arrow.SourceGenerator;

namespace Arrow.SourceGenerator.Tests.Models;

public enum Level : short
{
    Low = 1,
    High = 2,
}

/// <summary>Every P0 scalar, required and nullable, compiled by the real analyzer.</summary>
[ArrowSerializable]
public partial class ScalarRow
{
    public bool Flag { get; set; }
    public sbyte I8 { get; set; }
    public byte U8 { get; set; }
    public short I16 { get; set; }
    public ushort U16 { get; set; }
    public int I32 { get; set; }
    public uint U32 { get; set; }
    public long I64 { get; set; }
    public ulong U64 { get; set; }
    public float F32 { get; set; }
    public double F64 { get; set; }
    public string Text { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
    public decimal Money { get; set; }

    [ArrowDecimal(10, 2)]
    public decimal Price { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly Time { get; set; }
    public DateTime WallClock { get; set; }
    public DateTimeOffset Instant { get; set; }
    public TimeSpan Elapsed { get; set; }
    public Guid Id { get; set; }
    public Level Level { get; set; }

    public bool? MaybeFlag { get; set; }
    public int? MaybeI32 { get; set; }
    public double? MaybeF64 { get; set; }
    public string? MaybeText { get; set; }
    public byte[]? MaybeBytes { get; set; }
    public decimal? MaybeMoney { get; set; }
    public DateOnly? MaybeDay { get; set; }
    public TimeOnly? MaybeTime { get; set; }
    public DateTime? MaybeWallClock { get; set; }
    public DateTimeOffset? MaybeInstant { get; set; }
    public TimeSpan? MaybeElapsed { get; set; }
    public Guid? MaybeId { get; set; }
    public Level? MaybeLevel { get; set; }
}

/// <summary>Positional record: the design doc's founding sketch.</summary>
[ArrowSerializable]
public partial record Order(long Id, string? Customer, [ArrowDecimal(18, 4)] decimal Total);

/// <summary>A struct target: no null-row checks, copied by value into chunks.</summary>
[ArrowSerializable]
public partial struct Point
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>Required and init-only members materialise through the object initializer.</summary>
[ArrowSerializable]
public sealed partial class InitOnlyRow
{
    public required string Name { get; init; }
    public int? Rank { get; init; }
}
