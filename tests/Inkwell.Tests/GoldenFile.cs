namespace Structed.Inkwell.Tests;

/// <summary>
/// Compares generated output against a committed fixture.
/// </summary>
/// <remarks>
/// <para>
/// Line endings are normalised on both sides before comparing, and the fixtures are pinned to LF in
/// <c>.gitattributes</c>. This is not tidiness. The generator this engine was lifted out of shipped
/// a renderer that emitted CRLF on Windows and LF on Linux, so the same seed produced different
/// bytes depending on which machine built it — a difference invisible in every diff and fatal to the
/// promise that a shared link rebuilds what the sender saw.
/// </para>
/// <para>
/// Set <c>UPDATE_GOLDEN=1</c> to rewrite the fixtures. Regenerating them should always be a
/// deliberate act with a diff to read, because a changed fixture means every previously shared link
/// now resolves to something else.
/// </para>
/// </remarks>
internal static class GoldenFile
{
    public static void Verify(string name, string actual)
    {
        string path = Path.Combine(RepositoryRoot, "tests", "Inkwell.Tests", "Fixtures", name);
        string normalised = Normalise(actual);

        if (ShouldUpdate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalised);
            return;
        }

        Assert.True(
            File.Exists(path),
            $"The fixture '{name}' is missing. Run the tests with UPDATE_GOLDEN=1 to write it.");

        string expected = Normalise(File.ReadAllText(path));

        Assert.True(
            string.Equals(expected, normalised, StringComparison.Ordinal),
            $"The output no longer matches '{name}'. Every seed already shared resolves to the old " +
            $"output, so check this is intended before running with UPDATE_GOLDEN=1.\n\n" +
            $"--- expected ---\n{expected}\n--- actual ---\n{normalised}");
    }

    public static bool ShouldUpdate =>
        Environment.GetEnvironmentVariable("UPDATE_GOLDEN") is "1" or "true";

    /// <summary>The repository root, found by walking up to the solution file.</summary>
    private static string RepositoryRoot { get; } = Locate();

    private static string Locate()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Inkwell.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find 'Inkwell.slnx' above '{AppContext.BaseDirectory}'.");
    }

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd('\n') + "\n";
}
