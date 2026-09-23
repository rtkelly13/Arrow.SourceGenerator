namespace Arrow.SourceGenerator;

/// <summary>
/// Names of tracked pipeline steps. Incrementality is a tested product property, and tests find
/// steps by these names (docs/00-DESIGN-GOALS.md section 10).
/// </summary>
internal static class TrackingNames
{
    public const string Parse = "Arrow.Parse";
    public const string Diagnostics = "Arrow.Diagnostics";
    public const string ArrowReference = "Arrow.ArrowReference";
    public const string Plan = "Arrow.Plan";
    public const string PlanDiagnostics = "Arrow.PlanDiagnostics";
    public const string EmissionPlan = "Arrow.EmissionPlan";
}
