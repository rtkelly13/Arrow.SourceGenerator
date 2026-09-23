namespace Arrow.SourceGenerator;

/// <summary>
/// Marks a <c>partial</c> class, struct or record for compile-time Apache Arrow mapping. The
/// generator emits a companion <c>{TypeName}Arrow</c> type beside it.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    Inherited = false,
    AllowMultiple = false
)]
public sealed class ArrowSerializableAttribute : Attribute { }
