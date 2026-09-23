namespace Arrow.SourceGenerator;

/// <summary>
/// Sets the precision and scale of the <c>Decimal128</c> field a <see cref="decimal"/> member maps
/// to. Without it the field is <c>Decimal128(38, 18)</c>.
/// </summary>
/// <remarks>
/// Writing a value that does not fit throws; nothing is rounded or truncated. Reading requires the
/// Arrow field's precision and scale to match exactly.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    Inherited = true,
    AllowMultiple = false
)]
public sealed class ArrowDecimalAttribute : Attribute
{
    /// <summary>Maps the member to <c>Decimal128(<paramref name="precision"/>, <paramref name="scale"/>)</c>.</summary>
    /// <param name="precision">Total significant digits, 1 to 38.</param>
    /// <param name="scale">Digits after the decimal point, 0 to 28 and at most <paramref name="precision"/>.</param>
    public ArrowDecimalAttribute(int precision, int scale)
    {
        Precision = precision;
        Scale = scale;
    }

    /// <summary>Total significant digits.</summary>
    public int Precision { get; }

    /// <summary>Digits after the decimal point.</summary>
    public int Scale { get; }
}
