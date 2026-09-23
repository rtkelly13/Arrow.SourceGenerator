using Arrow.SourceGenerator.Planning;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// Emits <c>{Type}Arrow.View(batch)</c> and the <c>{Type}ArrowView</c> it returns: a strongly
/// typed, validated window onto an existing <c>RecordBatch</c>.
/// </summary>
/// <remarks>
/// View-only zero-copy (docs/03-OWNERSHIP.md): creating a view runs the same validation as the
/// reader, once, then exposes each field's Apache.Arrow array under the member's name. Nothing is
/// copied and nothing is owned; the arrays are the batch's own. The constructor is internal, so a
/// view can only be obtained through validation.
/// </remarks>
internal static class TypedViewEmitter
{
    private const string Arrow = ArrowTypes.Arrow;

    /// <summary>Members of the view that are not fields; model members may not reuse them.</summary>
    public static readonly string[] ReservedMemberNames = ["Batch", "Length"];

    public static string ViewName(EmissionContext context) => context.Target.Name + "ArrowView";

    /// <summary>The factory on the companion.</summary>
    public static void EmitFactory(SourceWriter writer, EmissionContext context)
    {
        string view = SourceNames.Identifier(ViewName(context));
        writer.Line("/// <summary>");
        writer.Line(
            $"/// Validates <paramref name=\"batch\"/> against {context.ModelCref}'s <see cref=\"Schema\"/>,"
        );
        writer.Line(
            "/// exactly as <see cref=\"FromRecordBatch\"/> does, and returns a typed view over it without"
        );
        writer.Line("/// copying. The view is valid for as long as the batch is.");
        writer.Line("/// </summary>");
        writer.Line(
            "/// <param name=\"batch\">The batch. It is not retained beyond the view, nor disposed.</param>"
        );
        writer.Line(
            "/// <exception cref=\"global::System.IO.InvalidDataException\">The batch does not match.</exception>"
        );
        using (writer.Block($"public static {view} View({Arrow}.RecordBatch batch)"))
        {
            writer.Line(
                "if (batch is null) throw new global::System.ArgumentNullException(nameof(batch));"
            );
            writer.Line($"return new {view}(batch);");
        }
    }

    /// <summary>The view type, a sibling of the companion.</summary>
    public static void EmitViewType(SourceWriter writer, EmissionContext context)
    {
        string view = SourceNames.Identifier(ViewName(context));
        writer.Line("/// <summary>");
        writer.Line(
            $"/// A validated, strongly typed view over a <c>RecordBatch</c> of {context.ModelCref}:"
        );
        writer.Line(
            "/// one Apache.Arrow array per field, resolved once, with no copy. Obtain one from"
        );
        writer.Line(
            $"/// <see cref=\"{context.Target.CompanionName}.View\"/>. It does not own the batch."
        );
        writer.Line("/// </summary>");
        using (writer.Block($"{context.Target.Accessibility} sealed partial class {view}"))
        {
            // The only constructor validates: generated code is compiled into the consumer's
            // assembly, so an internal constructor taking arrays would let any code there build a
            // "validated" view over arrays from another batch, or null.
            using (writer.Block($"internal {view}({Arrow}.RecordBatch batch)"))
            {
                writer.Line(
                    "if (batch is null) throw new global::System.ArgumentNullException(nameof(batch));"
                );
                writer.Line(
                    context.Plan.Fields.Count == 0
                        ? $"{SourceNames.Identifier(context.Target.CompanionName)}.ResolveColumns(batch);"
                        : $"int[] ordinals = {SourceNames.Identifier(context.Target.CompanionName)}.ResolveColumns(batch);"
                );
                writer.Line("Batch = batch;");
                foreach (FieldPlan field in context.Plan.Fields)
                {
                    writer.Line(
                        $"{field.MemberIdentifier} = ({ArrowTypes.ArrayClass(field.Leaf)})batch.Column(ordinals[{field.Ordinal}]);"
                    );
                }
            }

            writer.Line();
            writer.Line("/// <summary>The batch this view reads.</summary>");
            writer.Line($"public {Arrow}.RecordBatch Batch {{ get; }}");
            writer.Line();
            writer.Line("/// <summary>The number of rows.</summary>");
            writer.Line("public int Length => Batch.Length;");

            foreach (FieldPlan field in context.Plan.Fields)
            {
                writer.Line();
                writer.Line(
                    $"/// <summary>Arrow field <c>{SourceNames.XmlText(field.FieldName)}</c> ({SourceNames.XmlText(ArrowTypes.Describe(field.Leaf))}{(field.IsNullable ? ", nullable" : "")}).</summary>"
                );
                writer.Line(
                    $"public {ArrowTypes.ArrayClass(field.Leaf)} {field.MemberIdentifier} {{ get; }}"
                );
            }
        }
    }
}
