using Arrow.SourceGenerator.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests;

/// <summary>
/// The harness gate: the generator loads, runs, and leaves an ordinary compilation untouched.
/// </summary>
public sealed class GeneratorLoadTests
{
    [Fact]
    public void GeneratorRunsWithoutExceptionOrOutputOnAnUnannotatedCompilation()
    {
        GeneratorOutcome outcome = GeneratorHarness.Run(
            """
            namespace Demo;

            public sealed class Plain
            {
                public int Id { get; set; }
            }
            """
        );

        outcome.RunResult.Exception.ShouldBeNull();
        outcome.GeneratedSources.ShouldBeEmpty();
        outcome.GeneratorDiagnostics.ShouldBeEmpty();
        outcome.CompilationProblems.ShouldBeEmpty();
    }
}
