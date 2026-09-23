using Apache.Arrow;
using Apache.Arrow.Ipc;
using Arrow.SourceGenerator.Interop;

// write FILE: the generated writer produces an Arrow IPC file for PyArrow to verify.
// read FILE:  a PyArrow-produced IPC file must materialise, strictly, into the expected rows.
if (args is not [var command, var path])
{
    Console.Error.WriteLine("usage: (write|read) <arrow-ipc-file>");
    return 2;
}

switch (command)
{
    case "write":
    {
        using RecordBatch batch = InteropRowArrow.ToRecordBatch(InteropRow.Expected());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream file = File.Create(path);
        using var writer = new ArrowFileWriter(file, InteropRowArrow.Schema);
        writer.WriteRecordBatch(batch);
        writer.WriteEnd();
        Console.WriteLine($"wrote {batch.Length} rows to {path}");
        return 0;
    }

    case "read":
    {
        using FileStream file = File.OpenRead(path);
        using var reader = new ArrowFileReader(file);
        var batches = new List<RecordBatch>();
        while (reader.ReadNextRecordBatch() is { } batch)
        {
            batches.Add(batch);
        }

        InteropRow[] actual = [.. InteropRowArrow.FromRecordBatches(batches)];
        InteropRow[] expected = InteropRow.Expected();
        var differences = new List<string>();
        if (actual.Length != expected.Length)
        {
            differences.Add($"expected {expected.Length} rows, got {actual.Length}");
        }

        for (int i = 0; i < Math.Min(actual.Length, expected.Length); i++)
        {
            differences.AddRange(InteropRow.Differences(expected[i], actual[i], i));
        }

        batches.ForEach(b => b.Dispose());
        if (differences.Count > 0)
        {
            differences.ForEach(Console.Error.WriteLine);
            return 1;
        }

        Console.WriteLine($"read {actual.Length} PyArrow-produced rows; all fields match");
        return 0;
    }

    default:
        Console.Error.WriteLine($"unknown command '{command}'");
        return 2;
}
