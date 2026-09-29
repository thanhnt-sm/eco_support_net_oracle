using DataGuard.Cli;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Pins the write-path policy (code-review M3): links directly at the target or its parent are always rejected,
/// repository-controlled ancestors (inside the workspace root) are walked, host-chosen ancestors (a junctioned
/// <c>%TEMP%</c> outside the workspace) are not.
/// </summary>
public class SafeWritablePathTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dg-safe-path").FullName;
    private readonly string _workspace;
    private readonly string _outside;

    public SafeWritablePathTests()
    {
        _workspace = Path.Combine(_root, "ws");
        _outside = Path.Combine(_root, "outside-target");
        Directory.CreateDirectory(_workspace);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        // Remove links first (non-recursive) so the recursive delete never follows a junction into its target.
        foreach (var link in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories)
                     .Where(d => new DirectoryInfo(d).LinkTarget != null).OrderByDescending(d => d.Length))
        {
            Directory.Delete(link, recursive: false);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void PlainNestedPathInsideWorkspace_IsSafe()
    {
        var path = Path.Combine(_workspace, "reports", "ci", "x.sarif");

        SafeWritablePath.IsSafe(path, _workspace).Should().BeTrue();
    }

    [Fact]
    public void LinkedDirectoryInsideWorkspace_PointingOutside_IsRejected()
    {
        // Committed `reports -> <outside>`; the immediate parent `ci` does not exist yet, so only an ancestor walk catches it.
        var link = Path.Combine(_workspace, "reports");
        DirectoryLinkTestHelper.CreateDirectoryLink(link, _outside);
        var path = Path.Combine(link, "ci", "x.sarif");

        SafeWritablePath.IsSafe(path, _workspace).Should().BeFalse();
    }

    [Fact]
    public void LinkedDirectoryOutsideWorkspace_WithPlainSubdirectory_IsSafe()
    {
        // Host layout: %TEMP% is a junction; output goes to <junction>/sub/x.sarif. Ancestors above the workspace are not walked.
        var link = Path.Combine(_root, "temp-junction");
        DirectoryLinkTestHelper.CreateDirectoryLink(link, _outside);
        var path = Path.Combine(link, "sub", "x.sarif");

        SafeWritablePath.IsSafe(path, _workspace).Should().BeTrue();
    }

    [Fact]
    public void TargetDirectlyUnderLink_IsRejectedEvenOutsideWorkspace()
    {
        var link = Path.Combine(_root, "direct-junction");
        DirectoryLinkTestHelper.CreateDirectoryLink(link, _outside);
        var path = Path.Combine(link, "x.sarif");

        SafeWritablePath.IsSafe(path, _workspace).Should().BeFalse();
    }

    [Fact]
    public void SiblingWithWorkspacePrefix_IsNotTreatedAsInsideWorkspace()
    {
        // `ws2` shares the string prefix of `ws`; a link there is host territory, not repository territory.
        var sibling = Path.Combine(_root, "ws2");
        Directory.CreateDirectory(sibling);
        var link = Path.Combine(sibling, "link");
        DirectoryLinkTestHelper.CreateDirectoryLink(link, _outside);
        var path = Path.Combine(link, "sub", "x.sarif");

        SafeWritablePath.IsSafe(path, _workspace).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPath_IsRejected(string path)
    {
        SafeWritablePath.IsSafe(path, _workspace).Should().BeFalse();
    }
}
