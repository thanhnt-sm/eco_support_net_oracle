using System;
using System.IO;

namespace DataGuard.VisualStudio.Tests;

/// <summary>Repository locations shared by tests that read source files or build artifacts.</summary>
internal static class TestPaths
{
    /// <summary>Repository root: the nearest ancestor of the test output directory that contains DataGuard.sln.</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DataGuard.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("DataGuard.sln was not found above " + AppContext.BaseDirectory + "; run the tests from the repository's build output.");
    }
}
