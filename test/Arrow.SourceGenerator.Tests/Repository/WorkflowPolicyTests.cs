using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Arrow.SourceGenerator.Tests.Repository;

/// <summary>
/// Repository policy that is cheaper to enforce as a test than to re-review on every pull request.
/// </summary>
public sealed partial class WorkflowPolicyTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
    );

    public static TheoryData<string> Workflows()
    {
        var data = new TheoryData<string>();
        foreach (
            string path in Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, ".github", "workflows"),
                "*.yml"
            )
        )
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void EveryThirdPartyActionIsPinnedToAFullCommitSha(string workflow)
    {
        string text = File.ReadAllText(
            Path.Combine(RepositoryRoot, ".github", "workflows", workflow)
        );

        var unpinned = UsesLine()
            .Matches(text)
            .Select(match => match.Groups["ref"].Value)
            .Where(reference => !reference.StartsWith("./", StringComparison.Ordinal))
            .Where(reference => !PinnedReference().IsMatch(reference))
            .ToList();

        unpinned.ShouldBeEmpty(
            $"{workflow} uses mutable action references; pin each to a 40-character commit SHA."
        );
    }

    [GeneratedRegex(@"^\s*-?\s*uses:\s*(?<ref>\S+)", RegexOptions.Multiline)]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"^[^@\s]+@[0-9a-f]{40}$")]
    private static partial Regex PinnedReference();
}
