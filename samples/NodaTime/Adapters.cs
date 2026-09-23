using Arrow.SourceGenerator;
using NodaTime;
using NodaTimeSample;

[assembly: ArrowTypeAdapter(typeof(LocalDateAdapter))]
[assembly: ArrowTypeAdapter(typeof(InstantAdapter))]
[assembly: ArrowTypeAdapter(typeof(DurationAdapter))]

namespace NodaTimeSample;

// NodaTime is the first stress test of the adapter model (docs/00-DESIGN-GOALS.md section 21). These
// are *interoperability* adapters onto native Arrow temporal types. Where NodaTime's range or
// precision is wider than the surrogate's, they throw rather than silently narrow; a lossless
// default package needs structural surrogates (docs/04-ADAPTERS.md).

/// <summary>LocalDate ↔ Date32 via DateOnly. Narrowing: DateOnly has no years before 1.</summary>
public static class LocalDateAdapter
{
    public static DateOnly ToStorage(LocalDate value) =>
        value.Year >= 1
            ? value.ToDateOnly()
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Dates before year 1 have no DateOnly (Date32 interop) form."
            );

    public static LocalDate FromStorage(DateOnly storage) => LocalDate.FromDateOnly(storage);
}

/// <summary>
/// Instant ↔ Timestamp(µs, UTC) via DateTimeOffset. Narrowing: sub-microsecond precision and
/// instants outside years 1-9999 are rejected instead of truncated.
/// </summary>
public static class InstantAdapter
{
    public static DateTimeOffset ToStorage(Instant value)
    {
        long ticks = value.ToUnixTimeTicks();
        if (value != Instant.FromUnixTimeTicks(ticks) || ticks % 10 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Timestamp(µs) cannot hold sub-microsecond precision."
            );
        }

        // DateTimeOffset itself rejects instants outside years 1-9999.
        return value.ToDateTimeOffset();
    }

    public static Instant FromStorage(DateTimeOffset storage) =>
        Instant.FromDateTimeOffset(storage);
}

/// <summary>Duration ↔ Duration(µs) via TimeSpan, rejecting sub-microsecond precision.</summary>
public static class DurationAdapter
{
    public static TimeSpan ToStorage(Duration value) =>
        value.SubsecondNanoseconds % 1000 == 0
            ? value.ToTimeSpan()
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Duration(µs) cannot hold sub-microsecond precision."
            );

    public static Duration FromStorage(TimeSpan storage) => Duration.FromTimeSpan(storage);
}
