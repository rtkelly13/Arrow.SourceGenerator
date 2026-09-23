using Arrow.SourceGenerator;
using Arrow.SourceGenerator.Tests.Models;

[assembly: ArrowTypeAdapter(typeof(MoneyAdapter))]
[assembly: ArrowTypeAdapter(typeof(EmailAddressAdapter))]
[assembly: ArrowTypeAdapter(typeof(TemperatureAdapter))]

namespace Arrow.SourceGenerator.Tests.Models;

/// <summary>A domain value type with no built-in mapping.</summary>
public readonly record struct Money(decimal Amount);

/// <summary>A domain reference type with validation in its constructor.</summary>
public sealed record EmailAddress
{
    public EmailAddress(string value) =>
        Value = value.Contains('@', StringComparison.Ordinal)
            ? value
            : throw new ArgumentException("not an email", nameof(value));

    public string Value { get; }
}

public readonly record struct Temperature(double Celsius);

public static class MoneyAdapter
{
    public static decimal ToStorage(this Money value) => value.Amount;

    public static Money FromStorage(decimal storage) => new(storage);
}

public static class EmailAddressAdapter
{
    public static string ToStorage(EmailAddress value) => value.Value;

    public static EmailAddress FromStorage(string storage) => new(storage);
}

public static class TemperatureAdapter
{
    public static double ToStorage(Temperature value) => value.Celsius;

    public static Temperature FromStorage(double storage) => new(storage);
}

/// <summary>An explicit override of a built-in mapping: an instant stored as Unix seconds.</summary>
public static class UnixSecondsAdapter
{
    public static long ToStorage(DateTimeOffset value) => value.ToUnixTimeSeconds();

    public static DateTimeOffset FromStorage(long storage) =>
        DateTimeOffset.FromUnixTimeSeconds(storage);
}

[ArrowSerializable]
public partial class AdapterRow
{
    [ArrowDecimal(18, 2)]
    public Money Price { get; set; }
    public Money? Discount { get; set; }
    public EmailAddress Contact { get; set; } = new("a@example.com");
    public EmailAddress? Backup { get; set; }
    public Temperature Reading { get; set; }

    [ArrowAdapter(typeof(UnixSecondsAdapter))]
    public DateTimeOffset Seen { get; set; }
}
