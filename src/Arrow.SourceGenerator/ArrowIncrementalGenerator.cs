using Arrow.SourceGenerator.Emit;
using Arrow.SourceGenerator.Model;
using Arrow.SourceGenerator.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Arrow.SourceGenerator;

/// <summary>
/// Roslyn incremental generator that maps <c>[ArrowSerializable]</c> models to Apache Arrow.
/// </summary>
/// <remarks>
/// <para>Pipeline:</para>
/// <list type="number">
///   <item><description>Discovery — <c>ForAttributeWithMetadataName</c>, keyed on the type symbol.</description></item>
///   <item><description>Parsing — the symbol collapses into a value-equatable
///     <see cref="TargetModel"/> plus diagnostics. No Roslyn object survives this step.</description></item>
///   <item><description>Emission — keyed on the model alone, so an edit that only moves a
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

        context.RegisterSourceOutput(
            parsed
                .Select(static (result, _) => result.Diagnostics)
                .WithTrackingName(TrackingNames.Diagnostics),
            static (spc, diagnostics) =>
            {
                foreach (DiagnosticInfo diagnostic in diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                }
            }
        );

        IncrementalValuesProvider<TargetModel> models = parsed
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(TrackingNames.Model);

        context.RegisterSourceOutput(
            models,
            static (spc, model) =>
                spc.AddSource(ArrowEmitter.HintName(model), ArrowEmitter.Emit(model))
        );
    }
}
