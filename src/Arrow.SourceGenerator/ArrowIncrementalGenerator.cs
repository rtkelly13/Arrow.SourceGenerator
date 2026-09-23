using Microsoft.CodeAnalysis;

namespace Arrow.SourceGenerator;

/// <summary>
/// Roslyn incremental generator that maps <c>[ArrowSerializable]</c> models to Apache Arrow.
/// </summary>
/// <remarks>
/// Foundation 0 registers no pipeline yet: this type exists so the packaging, analyzer-loading and
/// test harness gates have a real generator to load. Discovery arrives with Foundation 1.
/// </remarks>
[Generator(LanguageNames.CSharp)]
internal sealed class ArrowIncrementalGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context) { }
}
