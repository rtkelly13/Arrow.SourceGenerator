using Apache.Arrow;
using Apache.Arrow.Ipc;
using Basic;

// The founding sketch (docs/00-DESIGN-GOALS.md section 35), end to end.
Order[] orders =
[
    new(1, "Ada", 12.50m, new DateOnly(2026, 9, 1)),
    new(2, null, 7.25m, new DateOnly(2026, 9, 2)),
];

// 1. The generated schema, usable before there is any data (IPC, Flight, negotiation).
Console.WriteLine("Schema:");
foreach (Field field in OrderArrow.Schema.FieldsList)
{
    Console.WriteLine(
        $"  {field.Name}: {field.DataType.Name}{(field.IsNullable ? " (nullable)" : "")}"
    );
}

// 2. Rows → RecordBatch, written as an Arrow IPC stream.
using var stream = new MemoryStream();
using (RecordBatch batch = OrderArrow.ToRecordBatch(orders))
using (var writer = new ArrowStreamWriter(stream, OrderArrow.Schema, leaveOpen: true))
{
    writer.WriteRecordBatch(batch);
    writer.WriteEnd();
}

Console.WriteLine($"Wrote {orders.Length} rows as {stream.Length} bytes of Arrow IPC.");

// 3. IPC → RecordBatch → a typed view (zero-copy) and back to rows (validated).
stream.Position = 0;
using var reader = new ArrowStreamReader(stream);
using RecordBatch read = reader.ReadNextRecordBatch()!;

OrderArrowView view = OrderArrow.View(read);
Console.WriteLine($"View: {view.Length} rows, first total {view.Total.GetValue(0)}");

foreach (Order order in OrderArrow.FromRecordBatch(read))
{
    Console.WriteLine($"  {order}");
}
