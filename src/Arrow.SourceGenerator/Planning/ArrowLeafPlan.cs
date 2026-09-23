namespace Arrow.SourceGenerator.Planning;

/// <summary>The Arrow logical type a field has. Only the P0 scalar set exists today.</summary>
internal enum ArrowLogicalKind
{
    Boolean,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
    Utf8,
    Binary,
    Decimal128,
    Date32,
    Time64Microsecond,
    TimestampMicrosecond,
    TimestampMicrosecondUtc,
    DurationMicrosecond,
    FixedSizeBinary16,
}

/// <summary>How a CLR value becomes the Arrow storage value and back.</summary>
internal enum ConversionStrategy
{
    /// <summary>The CLR value is the stored value, bit for bit (integers, floats).</summary>
    Direct,

    /// <summary>An enum stored as its underlying integral value.</summary>
    EnumUnderlying,

    /// <summary><see cref="bool"/> into a bit-packed value buffer.</summary>
    Boolean,

    /// <summary><see cref="string"/> as UTF-8 bytes with int32 offsets.</summary>
    Utf8,

    /// <summary><c>byte[]</c> as bytes with int32 offsets.</summary>
    Binary,

    /// <summary><see cref="decimal"/> as a 128-bit two's-complement integer at a fixed scale.</summary>
    Decimal128,

    /// <summary><c>DateOnly</c> as days since 1970-01-01.</summary>
    DateOnlyDays,

    /// <summary><c>TimeOnly</c> as microseconds since midnight (sub-microsecond ticks truncate).</summary>
    TimeOnlyMicroseconds,

    /// <summary>
    /// <see cref="System.DateTime"/> as a wall-clock (timezone-less) microsecond timestamp. The
    /// <see cref="System.DateTimeKind"/> is not stored; reads produce <c>Unspecified</c>.
    /// </summary>
    DateTimeWallClockMicroseconds,

    /// <summary>
    /// <see cref="System.DateTimeOffset"/> as a UTC instant in microseconds. The offset is not
    /// stored; reads produce offset zero.
    /// </summary>
    DateTimeOffsetUtcMicroseconds,

    /// <summary><see cref="System.TimeSpan"/> as microseconds (sub-microsecond ticks truncate).</summary>
    TimeSpanMicroseconds,

    /// <summary><see cref="System.Guid"/> as 16 bytes in RFC 4122 (big-endian) order.</summary>
    GuidBigEndian,
}

/// <summary>How nulls reach the validity bitmap.</summary>
internal enum NullStrategy
{
    /// <summary>The field is non-nullable; a null reference at write time is an error.</summary>
    Required,

    /// <summary>A <see cref="System.Nullable{T}"/> member.</summary>
    NullableValue,

    /// <summary>A nullable (or nullable-oblivious) reference member.</summary>
    NullableReference,
}

/// <summary>
/// Every resolved decision for one leaf field, as data. Emitters branch on these enums; they do not
/// re-derive a mapping from CLR types or thread loose strings and booleans.
/// </summary>
/// <param name="Kind">The Arrow logical type.</param>
/// <param name="Conversion">How values convert between CLR and Arrow storage.</param>
/// <param name="StorageType">
/// The C# type of one stored value where it is a primitive (<c>long</c> for timestamps,
/// <c>int</c> for dates, the underlying type for enums); empty where storage is not a primitive.
/// </param>
/// <param name="Precision">Decimal precision; zero for other kinds.</param>
/// <param name="Scale">Decimal scale; zero for other kinds.</param>
internal sealed record ArrowLeafPlan(
    ArrowLogicalKind Kind,
    ConversionStrategy Conversion,
    string StorageType,
    int Precision = 0,
    int Scale = 0
);
