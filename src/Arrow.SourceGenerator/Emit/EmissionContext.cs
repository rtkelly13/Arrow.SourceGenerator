using Arrow.SourceGenerator.Model;
using Arrow.SourceGenerator.Planning;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// What every capability emitter receives: the target, its one plan, and stable naming. Emitters
/// read decisions from here and never reconstruct a mapping themselves.
/// </summary>
internal sealed class EmissionContext(EmissionPlan plan)
{
    public EmissionPlan Plan { get; } = plan;

    public TargetModel Target => Plan.Target;

    /// <summary>The model type as written in generated code, <c>global::</c>-qualified.</summary>
    public string ModelType => Target.FullyQualifiedName;

    /// <summary>The model type as a <c>cref</c>.</summary>
    public string ModelCref => $"<see cref=\"{Target.FullyQualifiedName}\"/>";
}
