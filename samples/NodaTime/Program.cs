using System.Globalization;
using Apache.Arrow;
using NodaTime;
using NodaTimeSample;

Shipment[] shipments =
[
    new()
    {
        Id = 1,
        ShipDate = new LocalDate(2026, 9, 23),
        DispatchedAt = Instant.FromUtc(2026, 9, 23, 8, 30, 15).PlusTicks(1230),
        Transit = Duration.FromHours(36),
        DeliveredAt = null,
    },
];

// The adapters store NodaTime values as native Arrow temporal types.
foreach (Field field in ShipmentArrow.Schema.FieldsList)
{
    Console.WriteLine(
        $"{field.Name}: {field.DataType.Name}{(field.IsNullable ? " (nullable)" : "")}"
    );
}

using RecordBatch batch = ShipmentArrow.ToRecordBatch(shipments);
Shipment read = ShipmentArrow.FromRecordBatch(batch)[0];
Console.WriteLine(
    $"Round trip: {read.ShipDate} {read.DispatchedAt} {read.Transit} delivered={read.DeliveredAt?.ToString(null, CultureInfo.InvariantCulture) ?? "no"}"
);

if (read.DispatchedAt != shipments[0].DispatchedAt || read.ShipDate != shipments[0].ShipDate)
{
    Console.Error.WriteLine("NodaTime round trip lost information.");
    return 1;
}

// Interop adapters refuse to narrow silently.
try
{
    ShipmentArrow.ToRecordBatch([new Shipment { DispatchedAt = Instant.FromUnixTimeTicks(1) }]);
    Console.Error.WriteLine("Expected a sub-microsecond instant to be rejected.");
    return 1;
}
catch (ArgumentOutOfRangeException error)
{
    Console.WriteLine($"Rejected as expected: {error.Message.Split('\n')[0]}");
}

return 0;
