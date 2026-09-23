namespace Arrow.SourceGenerator;

/// <summary>
/// Registers a type adapter as the default Arrow mapping for its domain type: every mapped member
/// of that type, in this assembly and in any assembly that references this one, is stored as the
/// adapter's surrogate type.
/// </summary>
/// <remarks>
/// <para>An adapter is a class with two static methods, statically resolved and called directly
/// by generated code (no reflection, Native AOT safe):</para>
/// <code>
/// public static TSurrogate ToStorage(TDomain value);   // may be an extension method
/// public static TDomain FromStorage(TSurrogate storage);
/// </code>
/// <para><c>TSurrogate</c> must be a built-in Arrow-mappable type. Nulls never reach an adapter:
/// the generator handles <c>TDomain?</c> itself.</para>
/// <para>Precedence, highest first: <see cref="ArrowAdapterAttribute"/> on the member, then a
/// registration in the compiling assembly, then a registration in a referenced assembly. Types
/// with a built-in mapping are only adapted through <see cref="ArrowAdapterAttribute"/>. Two
/// registrations for one domain type at the same level are an error (ARROW005).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ArrowTypeAdapterAttribute : Attribute
{
    /// <summary>Registers <paramref name="adapterType"/>.</summary>
    /// <param name="adapterType">The adapter class declaring <c>ToStorage</c> and <c>FromStorage</c>.</param>
    public ArrowTypeAdapterAttribute(Type adapterType)
    {
        AdapterType = adapterType;
    }

    /// <summary>The adapter class.</summary>
    public Type AdapterType { get; }
}
