namespace Arrow.SourceGenerator;

/// <summary>
/// Stores one member through the given type adapter, overriding any registered adapter and any
/// built-in mapping. See <see cref="ArrowTypeAdapterAttribute"/> for the adapter shape.
/// </summary>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    Inherited = true,
    AllowMultiple = false
)]
public sealed class ArrowAdapterAttribute : Attribute
{
    /// <summary>Stores the member through <paramref name="adapterType"/>.</summary>
    /// <param name="adapterType">The adapter class declaring <c>ToStorage</c> and <c>FromStorage</c>.</param>
    public ArrowAdapterAttribute(Type adapterType)
    {
        AdapterType = adapterType;
    }

    /// <summary>The adapter class.</summary>
    public Type AdapterType { get; }
}
