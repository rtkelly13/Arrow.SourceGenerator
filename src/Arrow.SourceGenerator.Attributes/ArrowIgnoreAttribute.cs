namespace Arrow.SourceGenerator;

/// <summary>Excludes a member from the generated Arrow mapping.</summary>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    Inherited = true,
    AllowMultiple = false
)]
public sealed class ArrowIgnoreAttribute : Attribute { }
