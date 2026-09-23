using Arrow.SourceGenerator.Emit;
using Arrow.SourceGenerator.Model;
using Arrow.SourceGenerator.Parsing;
using Arrow.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arrow.SourceGenerator;

/// <summary>
/// Roslyn incremental generator that maps <c>[ArrowSerializable]</c> models to Apache Arrow.
/// </summary>
/// <remarks>
/// <para>Pipeline (docs/00-DESIGN-GOALS.md section 4):</para>
/// <list type="number">
///   <item><description>Discovery — <c>ForAttributeWithMetadataName</c>, keyed on the type symbol.</description></item>
///   <item><description>Parsing — the symbol collapses into a value-equatable
///     <see cref="TargetModel"/> plus diagnostics. No Roslyn object survives this step.</description></item>
///   <item><description>Planning — the model and compilation facts resolve into one immutable
///     <see cref="EmissionPlan"/>.</description></item>
///   <item><description>Emission — keyed on the plan alone, so an edit that only moves a
///     diagnostic location leaves the emitted source cached.</description></item>
/// </list>
/// </remarks>
[Generator(LanguageNames.CSharp)]
internal sealed class ArrowIncrementalGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ParseResult> parsed = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeNames.Serializable,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => TargetParser.Parse(ctx, ct)
            )
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName(TrackingNames.Parse);

        // A single bool: every downstream node stays cached until the reference itself changes.
        IncrementalValueProvider<bool> arrowReferenced = context
            .CompilationProvider.Select(
                static (compilation, _) =>
                    compilation.GetTypeByMetadataName("Apache.Arrow.RecordBatch") is not null
            )
            .WithTrackingName(TrackingNames.ArrowReference);

        // Assembly-level adapter registrations, from this compilation and its references. Value
        // equatable, so targets re-plan only when a registration actually changes.
        IncrementalValueProvider<AdapterRegistry> registry = context
            .CompilationProvider.Select(
                static (compilation, _) => AdapterAnalyzer.ReadRegistry(compilation)
            )
            .WithTrackingName(TrackingNames.AdapterRegistry);

        context.RegisterSourceOutput(registry.Select(static (r, _) => r.Diagnostics), ReportAll);

        IncrementalValuesProvider<PlanResult> planned = parsed
            .Where(static result => result.Model is not null)
            .Combine(arrowReferenced)
            .Combine(registry)
            .Select(
                static (pair, _) =>
                    EmissionPlanner.Plan(pair.Left.Left, pair.Left.Right, pair.Right)
            )
            .WithTrackingName(TrackingNames.Plan);

        context.RegisterSourceOutput(
            parsed
                .Select(static (result, _) => result.Diagnostics)
                .WithTrackingName(TrackingNames.Diagnostics),
            ReportAll
        );
        context.RegisterSourceOutput(
            planned
                .Select(static (result, _) => result.Diagnostics)
                .WithTrackingName(TrackingNames.PlanDiagnostics),
            ReportAll
        );

        IncrementalValuesProvider<EmissionPlan> plans = planned
            .Where(static result => result.Plan is not null)
            .Select(static (result, _) => result.Plan!)
            .WithTrackingName(TrackingNames.EmissionPlan);

        context.RegisterSourceOutput(
            plans,
            static (spc, plan) =>
                spc.AddSource(ArrowEmitter.HintName(plan), ArrowEmitter.Emit(plan))
        );
    }

    private static void ReportAll(
        SourceProductionContext context,
        EquatableArray<DiagnosticInfo> diagnostics
    )
    {
        foreach (DiagnosticInfo diagnostic in diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
    }
}
