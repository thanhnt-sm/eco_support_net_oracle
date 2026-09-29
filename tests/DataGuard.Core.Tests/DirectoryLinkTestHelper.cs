using System.Diagnostics;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>Directory links for write-path tests: NTFS junctions on Windows (no privilege needed), symbolic links elsewhere.</summary>
internal static class DirectoryLinkTestHelper
{
    /// <summary>Creates <paramref name="link"/> pointing at <paramref name="target"/>; fails loudly rather than skipping when mklink is unavailable.</summary>
    internal static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var mklink = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var error = mklink.StandardError.ReadToEnd();
        mklink.WaitForExit();
        if (mklink.ExitCode != 0)
        {
            Assert.Fail("mklink /J failed on this machine; link tests cannot run: " + error);
        }
    }
}

/// <summary>Fact that is skipped (not silently passed) on non-Windows hosts, where NTFS junctions do not exist.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "NTFS junctions (mklink /J) require Windows.";
        }
    }
}

/// <summary>Theory counterpart of <see cref="WindowsFactAttribute"/>.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "NTFS junctions (mklink /J) require Windows.";
        }
    }
}
