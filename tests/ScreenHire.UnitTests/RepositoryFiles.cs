namespace ScreenHire.UnitTests;

/// <summary>Paths to files in the repository, for tests that check a committed file (a contract, a golden file).</summary>
internal static class RepositoryFiles
{
    /// <summary>The path of <paramref name="parts"/> under the repository root, the folder that holds ScreenHire.slnx.</summary>
    public static string PathTo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ScreenHire.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException($"No ScreenHire.slnx above {AppContext.BaseDirectory}.")
            : Path.Combine([directory.FullName, .. parts]);
    }
}
