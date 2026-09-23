using Apache.Arrow;
using Apache.Arrow.Types;

namespace Arrow.SourceGenerator.Benchmarks;

/// <summary>
/// The meaningful baseline every generated path is measured against: what a careful developer
/// writes by hand with Apache.Arrow's public builders (docs/00-DESIGN-GOALS.md section 31).
/// </summary>
public static class HandWrittenArrow
{
    public static readonly Schema Schema = new Schema.Builder()
        .Field(f => f.Name("Id").DataType(Int64Type.Default).Nullable(false))
        .Field(f => f.Name("Quantity").DataType(Int32Type.Default).Nullable(true))
        .Field(f => f.Name("Price").DataType(DoubleType.Default).Nullable(false))
        .Field(f => f.Name("Customer").DataType(StringType.Default).Nullable(false))
        .Field(f => f.Name("Note").DataType(StringType.Default).Nullable(true))
        .Field(f => f.Name("Total").DataType(new Decimal128Type(38, 18)).Nullable(false))
        .Build();

    public static RecordBatch ToRecordBatch(IReadOnlyCollection<BenchmarkRow> rows)
    {
        int count = rows.Count;
        var id = new Int64Array.Builder().Reserve(count);
        var quantity = new Int32Array.Builder().Reserve(count);
        var price = new DoubleArray.Builder().Reserve(count);
        var customer = new StringArray.Builder();
        var note = new StringArray.Builder();
        var total = new Decimal128Array.Builder(new Decimal128Type(38, 18)).Reserve(count);

        foreach (BenchmarkRow row in rows)
        {
            id.Append(row.Id);
            if (row.Quantity is int q)
            {
                quantity.Append(q);
            }
            else
            {
                quantity.AppendNull();
            }

            price.Append(row.Price);
            customer.Append(row.Customer);
            if (row.Note is null)
            {
                note.AppendNull();
            }
            else
            {
                note.Append(row.Note);
            }

            total.Append(row.Total);
        }

        return new RecordBatch(
            Schema,
            [
                id.Build(),
                quantity.Build(),
                price.Build(),
                customer.Build(),
                note.Build(),
                total.Build(),
            ],
            count
        );
    }

    public static BenchmarkRow[] FromRecordBatch(RecordBatch batch)
    {
        var id = (Int64Array)batch.Column(0);
        var quantity = (Int32Array)batch.Column(1);
        var price = (DoubleArray)batch.Column(2);
        var customer = (StringArray)batch.Column(3);
        var note = (StringArray)batch.Column(4);
        var total = (Decimal128Array)batch.Column(5);

        var rows = new BenchmarkRow[batch.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new BenchmarkRow
            {
                Id = id.GetValue(i)!.Value,
                Quantity = quantity.GetValue(i),
                Price = price.GetValue(i)!.Value,
                Customer = customer.GetString(i),
                Note = note.GetString(i),
                Total = total.GetValue(i)!.Value,
            };
        }

        return rows;
    }
}
