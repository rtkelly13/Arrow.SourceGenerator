namespace Arrow.SourceGenerator;

/// <summary>
/// Describes the Arrow field a member maps to. Without it a member maps to a field named after
/// the member, in declaration order.
/// </summary>
/// <remarks>
/// On a positional record the attribute may be written on the primary-constructor parameter;
/// the generator reads it from there as well as from the property.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    Inherited = true,
    AllowMultiple = false
)]
public sealed class ArrowColumnAttribute : Attribute
{
    /// <summary>Maps the member to a field named after the member.</summary>
    public ArrowColumnAttribute() { }

    /// <summary>Maps the member to the field <paramref name="name"/>.</summary>
    /// <param name="name">The Arrow field name. Field names are case-sensitive.</param>
    public ArrowColumnAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The Arrow field name, or <see langword="null"/> for the member name.</summary>
    public string? Name { get; }

    /// <summary>
    /// Explicit field position. Members with an order come first, ascending; the rest follow in
    /// declaration order.
    /// </summary>
    public int Order { get; set; }
}
