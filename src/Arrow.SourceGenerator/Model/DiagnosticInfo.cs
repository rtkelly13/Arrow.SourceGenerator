using Microsoft.CodeAnalysis;

namespace Arrow.SourceGenerator.Model;

/// <summary>
/// A diagnostic captured as plain data so it can live in cached pipeline state and be reported
/// later. Descriptors are static singletons, so holding one roots nothing.
/// </summary>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs
)
{
    public static DiagnosticInfo Create(
        DiagnosticDescriptor descriptor,
        LocationInfo? location,
        params string[] messageArgs
    ) => new(descriptor, location, messageArgs.ToEquatableArray());

    public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(
            Descriptor,
            Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
            [.. MessageArgs]
        );
}
