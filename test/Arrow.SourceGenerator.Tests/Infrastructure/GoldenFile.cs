using Shouldly;

namespace Arrow.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Reads and (only when asked) rewrites checked-in golden artefacts under <c>GoldenFiles/</c>.
/// </summary>
/// <remarks>
/// A missing golden file is a failure, never a silent first-run write: a baseline that creates
/// itself cannot catch the change that deleted it. Refresh existing goldens with
/// <c>UPDATE_GOLDEN_FILES=true dotnet test</c>; add a new one with <c>UPDATE_GOLDEN_FILES=create</c>.
/// Review the diff either way.
/// </remarks>
internal static class GoldenFile
{
    public static string Directory { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "GoldenFiles"));

    public static bool UpdateRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable("UPDATE_GOLDEN_FILES"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>Like <see cref="UpdateRequested"/>, and also writes goldens that do not exist yet.</summary>
    public static bool CreateRequested =>
        string.Equals(
            Environment.GetEnvironmentVariable("UPDATE_GOLDEN_FILES"),
            "create",
            StringComparison.OrdinalIgnoreCase
        );

    public static string ReadInput(string relativePath) =>
        File.ReadAllText(Path.Combine(Directory, relativePath));

    /// <summary>Asserts <paramref name="actual"/> matches the golden file, or rewrites it.</summary>
    public static void AssertMatches(string fileName, string actual)
    {
        string path = Path.Combine(Directory, fileName);
        string normalized = Normalize(actual);

        // Checked before the update branch: a refresh rewrites existing baselines only, so a
        // deleted golden cannot quietly reappear during the very command that refreshes the rest.
        // A new golden is created deliberately with UPDATE_GOLDEN_FILES=create.
        if (!File.Exists(path) && !CreateRequested)
        {
            throw new ShouldAssertException(
                $"Golden file GoldenFiles/{fileName} is missing. Neither a normal run nor a refresh "
                    + "creates one; add it deliberately with UPDATE_GOLDEN_FILES=create, review the "
                    + "result and commit it."
            );
        }

        if (UpdateRequested || CreateRequested)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, normalized);
            return;
        }

        string expected = Normalize(File.ReadAllText(path));
        if (!string.Equals(expected, normalized, StringComparison.Ordinal))
        {
            throw new ShouldAssertException(Describe(fileName, expected, normalized));
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private static string Describe(string fileName, string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');
        int first = 0;
        while (
            first < expectedLines.Length
            && first < actualLines.Length
            && string.Equals(expectedLines[first], actualLines[first], StringComparison.Ordinal)
        )
        {
            first++;
        }

        string expectedLine = first < expectedLines.Length ? expectedLines[first] : "<end of file>";
        string actualLine = first < actualLines.Length ? actualLines[first] : "<end of file>";
        return $"GoldenFiles/{fileName} drifted at line {first + 1}.\n"
            + $"  expected: {expectedLine}\n"
            + $"  actual:   {actualLine}\n"
            + "If the change is intended, refresh with UPDATE_GOLDEN_FILES=true and review the diff.";
    }
}
