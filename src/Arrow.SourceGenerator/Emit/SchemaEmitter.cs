using Arrow.SourceGenerator.Planning;

namespace Arrow.SourceGenerator.Emit;

/// <summary>
/// Emits <c>{Type}Arrow.Schema</c>: the generated schema, built once from the same plan every
/// other emitter consumes, usable with zero rows and for schema negotiation.
/// </summary>
internal static class SchemaEmitter
{
    public static void Emit(SourceWriter writer, EmissionContext context)
    {
        writer.Line("/// <summary>");
        writer.Line(
            $"/// The Apache Arrow schema of {context.ModelCref}: one field per mapped member,"
        );
        writer.Line(
            "/// in field order. Built once and shared; Apache.Arrow schemas are immutable."
        );
        writer.Line("/// </summary>");
        writer.Line($"public static {ArrowTypes.Arrow}.Schema Schema {{ get; }} = CreateSchema();");
        writer.Line();

        using (writer.Block($"private static {ArrowTypes.Arrow}.Schema CreateSchema()"))
        {
            if (context.Plan.Fields.Count == 0)
            {
                writer.Line(
                    $"return new {ArrowTypes.Arrow}.Schema(global::System.Array.Empty<{ArrowTypes.Arrow}.Field>(), null);"
                );
                return;
            }

            using (writer.Block($"var fields = new {ArrowTypes.Arrow}.Field[]", "};"))
            {
                foreach (FieldPlan field in context.Plan.Fields)
                {
                    writer.Line(
                        $"new {ArrowTypes.Arrow}.Field({field.FieldNameLiteral}, {ArrowTypes.TypeExpression(field.Leaf)}, nullable: {(field.IsNullable ? "true" : "false")}),"
                    );
                }
            }

            writer.Line($"return new {ArrowTypes.Arrow}.Schema(fields, null);");
        }
    }
}
