using Arrow.SourceGenerator;
using NodaTime;

namespace NodaTimeSample;

[ArrowSerializable]
public partial class Shipment
{
    public long Id { get; set; }
    public LocalDate ShipDate { get; set; }
    public Instant DispatchedAt { get; set; }
    public Duration Transit { get; set; }
    public Instant? DeliveredAt { get; set; }
}
