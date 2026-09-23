using System;
using Arrow.SourceGenerator;

namespace Golden.Events;

public enum Priority : byte
{
    Low,
    High,
}

/// <summary>Every P0 scalar built-in, required and nullable.</summary>
[ArrowSerializable]
public partial class ScalarEvent
{
    public bool Flag { get; set; }
    public sbyte Tiny { get; set; }
    public byte UnsignedTiny { get; set; }
    public short Small { get; set; }
    public ushort UnsignedSmall { get; set; }
    public int Count { get; set; }
    public uint UnsignedCount { get; set; }
    public long Id { get; set; }
    public ulong UnsignedId { get; set; }
    public float Ratio { get; set; }
    public double Score { get; set; }
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public byte[] Payload { get; set; } = [];
    public byte[]? OptionalPayload { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly At { get; set; }
    public DateTime LocalWallClock { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public TimeSpan Elapsed { get; set; }
    public Guid CorrelationId { get; set; }
    public Priority Priority { get; set; }
    public int? MaybeCount { get; set; }
    public Guid? MaybeCorrelationId { get; set; }
    public DateTimeOffset? MaybeOccurredAt { get; set; }
    public Priority? MaybePriority { get; set; }
}
