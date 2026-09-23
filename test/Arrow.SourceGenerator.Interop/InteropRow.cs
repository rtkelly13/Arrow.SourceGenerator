using Arrow.SourceGenerator;

namespace Arrow.SourceGenerator.Interop;

public enum Level : byte
{
    Low = 0,
    High = 2,
}

/// <summary>
/// Every P0 kind, required and nullable. <c>scripts/pyarrow_interop.py</c> holds the same schema
/// and rows; the two halves check each other.
/// </summary>
[ArrowSerializable]
public sealed partial class InteropRow
{
    public long Id { get; init; }
    public int? Count { get; init; }
    public double Score { get; init; }
    public bool Flag { get; init; }
    public string Name { get; init; } = "";
    public string? Note { get; init; }
    public byte[] Payload { get; init; } = [];

    [ArrowDecimal(18, 4)]
    public decimal Amount { get; init; }
    public DateOnly Day { get; init; }
    public TimeOnly At { get; init; }
    public DateTime Local { get; init; }
    public DateTimeOffset Instant { get; init; }
    public TimeSpan Elapsed { get; init; }
    public Guid Key { get; init; }
    public Level Level { get; init; }
    public ulong Big { get; init; }

    /// <summary>The rows both sides agree on, boundary values included.</summary>
    public static InteropRow[] Expected() =>
        [
            new()
            {
                Id = 1,
                Count = 42,
                Score = 1.5,
                Flag = true,
                Name = "ascii",
                Note = "note",
                Payload = [0, 1, 255],
                Amount = 1234.5678m,
                Day = new DateOnly(2024, 2, 29),
                At = new TimeOnly(13, 14, 15).Add(TimeSpan.FromMicroseconds(123456)),
                Local = new DateTime(2024, 1, 2, 3, 4, 5).AddTicks(6789010),
                Instant = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)).AddTicks(
                    6789010
                ),
                Elapsed = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromMicroseconds(567891)),
                Key = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                Level = Level.High,
                Big = ulong.MaxValue,
            },
            new()
            {
                Id = -2,
                Count = null,
                Score = -2.25,
                Flag = false,
                Name = "unicode é中",
                Note = null,
                Payload = [],
                Amount = -0.0001m,
                Day = new DateOnly(1969, 12, 31),
                At = TimeOnly.MinValue,
                Local = new DateTime(1900, 1, 1),
                Instant = DateTimeOffset.UnixEpoch,
                Elapsed = TimeSpan.FromMilliseconds(-1500),
                Key = Guid.Empty,
                Level = Level.Low,
                Big = 0,
            },
            new()
            {
                Id = long.MaxValue,
                Count = int.MinValue,
                Score = 1e300,
                Flag = true,
                Name = "",
                Note = "",
                Payload = [42],
                Amount = 99999999999999.9999m,
                Day = new DateOnly(9999, 12, 31),
                At = new TimeOnly(23, 59, 59).Add(TimeSpan.FromMicroseconds(999999)),
                Local = new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9999990),
                Instant = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Elapsed = TimeSpan.Zero,
                Key = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                Level = Level.High,
                Big = 1UL << 63,
            },
        ];

    /// <summary>Field-by-field comparison with the documented read semantics applied.</summary>
    public static IEnumerable<string> Differences(InteropRow expected, InteropRow actual, int row)
    {
        IEnumerable<(string Field, object? Expected, object? Actual)> pairs =
        [
            ("Id", expected.Id, actual.Id),
            ("Count", expected.Count, actual.Count),
            ("Score", expected.Score, actual.Score),
            ("Flag", expected.Flag, actual.Flag),
            ("Name", expected.Name, actual.Name),
            ("Note", expected.Note, actual.Note),
            ("Payload", Convert.ToHexString(expected.Payload), Convert.ToHexString(actual.Payload)),
            ("Amount", expected.Amount, actual.Amount),
            ("Day", expected.Day, actual.Day),
            ("At", expected.At, actual.At),
            ("Local", expected.Local.Ticks, actual.Local.Ticks),
            ("Instant", expected.Instant.UtcTicks, actual.Instant.UtcTicks),
            ("Elapsed", expected.Elapsed, actual.Elapsed),
            ("Key", expected.Key, actual.Key),
            ("Level", expected.Level, actual.Level),
            ("Big", expected.Big, actual.Big),
        ];

        foreach ((string field, object? want, object? got) in pairs)
        {
            if (!Equals(want, got))
            {
                yield return $"row {row} {field}: expected {want ?? "null"}, got {got ?? "null"}";
            }
        }
    }
}
