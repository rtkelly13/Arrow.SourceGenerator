using Apache.Arrow;

namespace Arrow.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Compares two arrays' logical values slot by slot through Apache.Arrow's own visitor-free
/// accessors, ignoring the (unspecified) bytes behind null slots.
/// </summary>
internal static class ArrowArrayEquality
{
    public static bool ValuesEqual(IArrowArray expected, IArrowArray actual)
    {
        if (expected.Length != actual.Length || expected.GetType() != actual.GetType())
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected.IsNull(i) != actual.IsNull(i))
            {
                return false;
            }

            if (!expected.IsNull(i) && !Equals(Value(expected, i), Value(actual, i)))
            {
                return false;
            }
        }

        return true;
    }

    private static object? Value(IArrowArray array, int index) =>
        array switch
        {
            StringArray s => s.GetString(index),
            // Decimal128Array derives from FixedSizeBinaryArray, so it is matched first;
            // StringArray likewise before BinaryArray.
            Decimal128Array d => d.GetValue(index),
            Apache.Arrow.Arrays.FixedSizeBinaryArray f => Convert.ToHexString(f.GetBytes(index)),
            BinaryArray b => Convert.ToHexString(b.GetBytes(index)),
            BooleanArray b => b.GetValue(index),
            Int8Array a => a.GetValue(index),
            UInt8Array a => a.GetValue(index),
            Int16Array a => a.GetValue(index),
            UInt16Array a => a.GetValue(index),
            Int32Array a => a.GetValue(index),
            UInt32Array a => a.GetValue(index),
            Int64Array a => a.GetValue(index),
            UInt64Array a => a.GetValue(index),
            FloatArray a => a.GetValue(index),
            DoubleArray a => a.GetValue(index),
            Date32Array a => a.GetValue(index),
            Time64Array a => a.GetValue(index),
            TimestampArray a => a.GetValue(index),
            DurationArray a => a.GetValue(index),
            _ => throw new NotSupportedException(array.GetType().Name),
        };
}
