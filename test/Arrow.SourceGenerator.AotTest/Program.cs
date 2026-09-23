using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;

namespace Arrow.SourceGenerator.AotTest;

/// <summary>
/// Native AOT smoke matrix. Every check throws on failure; reaching the end prints a summary and
/// exits 0. Each generated capability adds its checks here as it lands.
/// </summary>
internal static class Program
{
    private static int _checks;

    private static int Main()
    {
        HandWrittenBaselineRoundTripsThroughIpc();
        GeneratedCompanionExists();
        GeneratedSchemaMatchesTheModel();

        Console.WriteLine($"Arrow.SourceGenerator AOT checks passed: {_checks}");
        return 0;
    }

    /// <summary>
    /// The control: Apache.Arrow itself, driven by hand, survives trimming and AOT compilation.
    /// If this fails the problem is the dependency, not generated code.
    /// </summary>
    private static void HandWrittenBaselineRoundTripsThroughIpc()
    {
        var schema = new Schema.Builder()
            .Field(f => f.Name("id").DataType(Int64Type.Default).Nullable(false))
            .Field(f => f.Name("name").DataType(StringType.Default).Nullable(true))
            .Build();

        using var batch = new RecordBatch(
            schema,
            [
                new Int64Array.Builder().Append(1).Append(2).Build(),
                new StringArray.Builder().Append("a").AppendNull().Build(),
            ],
            length: 2
        );

        using RecordBatch roundTripped = IpcRoundTrip(batch);
        Check(roundTripped.Length == 2, "baseline length");
        Check(((Int64Array)roundTripped.Column(0)).GetValue(1) == 2, "baseline int64");
        Check(((StringArray)roundTripped.Column(1)).GetString(0) == "a", "baseline utf8");
        Check(roundTripped.Column(1).IsNull(1), "baseline null");
    }

    /// <summary>The generator ran under the AOT build and emitted the companion type.</summary>
    private static void GeneratedCompanionExists() =>
        Check(
            typeof(AotOrderArrow).IsAbstract && typeof(AotOrderArrow).IsSealed,
            "companion is static"
        );

    private static void GeneratedSchemaMatchesTheModel()
    {
        Schema schema = AotOrderArrow.Schema;
        Check(schema.FieldsList.Count == 3, "schema field count");
        Check(schema.GetFieldByName("Id").DataType.TypeId == ArrowTypeId.Int64, "schema int64");
        Check(!schema.GetFieldByName("Customer").IsNullable, "schema required utf8");
        Check(schema.GetFieldByName("Quantity").IsNullable, "schema nullable int32");
    }

    internal static RecordBatch IpcRoundTrip(RecordBatch batch)
    {
        using var stream = new MemoryStream();
        using (var writer = new ArrowStreamWriter(stream, batch.Schema, leaveOpen: true))
        {
            writer.WriteRecordBatch(batch);
            writer.WriteEnd();
        }

        stream.Position = 0;
        using var reader = new ArrowStreamReader(stream);
        return reader.ReadNextRecordBatch()
            ?? throw new InvalidOperationException("IPC stream contained no batch.");
    }

    internal static void Check(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"AOT check failed: {what}");
        }

        _checks++;
    }
}
