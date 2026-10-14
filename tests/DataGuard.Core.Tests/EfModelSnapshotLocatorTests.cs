using DataGuard.Cli.Services;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// <see cref="EfModelSnapshotLocator"/> is a pure filter over the file list the run already walked: one usable
/// <c>*ModelSnapshot.cs</c> is read once with a bounded read; anything else is a diagnostic line, never an exception.
/// </summary>
public class EfModelSnapshotLocatorTests : IDisposable
{
    private const string SnapshotSource = "class Snapshot { void Build(ModelBuilder modelBuilder) { modelBuilder.Entity<Order>(b => { b.Property<int>(\"OrderId\"); }); } }";

    private readonly string _root = Directory.CreateTempSubdirectory("dg-ef-locator").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TryLocate_OneMatch_ReturnsSourceAndRelativePath()
    {
        var snapshot = Write("Migrations/ShopContextModelSnapshot.cs", SnapshotSource);
        var repo = Write("OrderRepository.cs", "class Repo { }");

        var located = EfModelSnapshotLocator.TryLocate(_root, new[] { repo, snapshot }, maximumSourceBytes: 1_048_576);

        located.Found.Should().BeTrue();
        located.Source.Should().Be(SnapshotSource);
        located.RelativePath.Should().Be("Migrations/ShopContextModelSnapshot.cs");
        located.FullPath.Should().Be(snapshot);
        located.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void TryLocate_NoCandidate_IsSilent()
    {
        var repo = Write("OrderRepository.cs", "class Repo { }");

        var located = EfModelSnapshotLocator.TryLocate(_root, new[] { repo }, 1_048_576);

        located.Found.Should().BeFalse();
        located.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void TryLocate_TwoMatches_NamesBothAndUsesNone()
    {
        var first = Write("A/ShopContextModelSnapshot.cs", SnapshotSource);
        var second = Write("B/BillingContextModelSnapshot.cs", SnapshotSource);

        var located = EfModelSnapshotLocator.TryLocate(_root, new[] { second, first }, 1_048_576);

        located.Found.Should().BeFalse();
        var diagnostic = located.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Should().StartWith("2 ModelSnapshot.cs files found under --project (A/ShopContextModelSnapshot.cs, B/BillingContextModelSnapshot.cs)");
        diagnostic.Should().Contain("commit .dataguard-snapshot.json or keep one design-time snapshot per solution");
        diagnostic.Should().NotContain(_root, "no absolute path is echoed");
    }

    [Fact]
    public void TryLocate_NodeModulesSegment_IsNotACandidate()
    {
        var vendored = Write("node_modules/some-pkg/FakeModelSnapshot.cs", SnapshotSource);
        var real = Write("Migrations/ShopContextModelSnapshot.cs", SnapshotSource);

        EfModelSnapshotLocator.TryLocate(_root, new[] { vendored }, 1_048_576).Should().BeSameAs(EfModelSnapshotLocation.None);
        EfModelSnapshotLocator.TryLocate(_root, new[] { vendored, real }, 1_048_576).RelativePath.Should().Be("Migrations/ShopContextModelSnapshot.cs");
        EfModelSnapshotLocator.IsCandidate(Path.Combine(_root, "Repo.cs")).Should().BeFalse();
    }

    [Fact]
    public void TryLocate_FileSymlink_IsSkipped()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating symbolic links needs a privilege on Windows; the attribute check is the same code path.
        }

        var target = Write("outside/RealModelSnapshot.cs", SnapshotSource);
        var link = Path.Combine(_root, "ShopContextModelSnapshot.cs");
        File.CreateSymbolicLink(link, target);

        var located = EfModelSnapshotLocator.TryLocate(_root, new[] { link }, 1_048_576);

        located.Found.Should().BeFalse();
        located.Diagnostics.Should().ContainSingle().Which.Should().Be("ShopContextModelSnapshot.cs: EF model not used: symbolic link, junction or device");
    }

    [Fact]
    public void TryLocate_PathOutsideRoot_IsSkipped()
    {
        // "<root>/Sln-evil/..." shares the prefix "<root>/Sln" with the root "<root>/Sln": a plain StartsWith would accept it.
        var root = Path.Combine(_root, "Sln");
        Directory.CreateDirectory(root);
        var evil = Write("Sln-evil/ShopContextModelSnapshot.cs", SnapshotSource);

        var located = EfModelSnapshotLocator.TryLocate(root, new[] { evil }, 1_048_576);

        located.Found.Should().BeFalse();
        located.Diagnostics.Should().ContainSingle().Which.Should().Be("ShopContextModelSnapshot.cs: EF model not used: outside --project");
    }

    [Fact]
    public void TryLocate_FileLongerThanTheBound_IsSkippedByTheStat()
    {
        var snapshot = Write("ShopContextModelSnapshot.cs", new string('x', 200));

        var located = EfModelSnapshotLocator.TryLocate(_root, new[] { snapshot }, maximumSourceBytes: 64);

        located.Found.Should().BeFalse();
        located.Diagnostics.Should().ContainSingle().Which.Should().Be("ShopContextModelSnapshot.cs: EF model not used: 200 bytes exceeds the 64-byte limit");
    }

    [Fact]
    public void TryLocate_FileGrownAfterTheStat_IsSkippedByTheBoundedRead()
    {
        // The stat is advisory: the seam returns a FileInfo whose cached length is small, then the file grows before the
        // read. The read stops at bound + 1 bytes and the file is still not used.
        var snapshot = Write("ShopContextModelSnapshot.cs", "short");

        var located = EfModelSnapshotLocator.TryLocate(_root, new[] { snapshot }, maximumSourceBytes: 64, fileInfo: path =>
        {
            var info = new FileInfo(path);
            _ = info.Length;
            File.AppendAllText(path, new string('y', 4096));
            return info;
        });

        located.Found.Should().BeFalse();
        located.Diagnostics.Should().ContainSingle().Which.Should().Be("ShopContextModelSnapshot.cs: EF model not used: longer than the 64-byte limit");
    }

    [Fact]
    public void TryLocate_EmptyFile_IsSkipped()
    {
        var snapshot = Write("ShopContextModelSnapshot.cs", string.Empty);

        var located = EfModelSnapshotLocator.TryLocate(_root, new[] { snapshot }, 1_048_576);

        located.Found.Should().BeFalse();
        located.Diagnostics.Should().ContainSingle().Which.Should().Be("ShopContextModelSnapshot.cs: EF model not used: empty file");
    }

    [Fact]
    public void TryLocate_FileDeletedBetweenDiscoveryAndCheck_IsADiagnosticNotAnException()
    {
        // On Unix the attributes of a missing file are -1 (every bit set): without the Exists check this read as a symlink.
        var snapshot = Write("ShopContextModelSnapshot.cs", SnapshotSource);

        var act = () => EfModelSnapshotLocator.TryLocate(_root, new[] { snapshot }, 1_048_576, fileInfo: path =>
        {
            File.Delete(path);
            return new FileInfo(path);
        });

        var located = act.Should().NotThrow().Subject;
        located.Found.Should().BeFalse();
        located.Diagnostics.Should().ContainSingle().Which.Should().Be("ShopContextModelSnapshot.cs: EF model not used: file not found");
    }

    [Fact]
    public void TryLocate_FileCheckThrows_IsADiagnosticNotAnException()
    {
        var snapshot = Write("ShopContextModelSnapshot.cs", SnapshotSource);

        var act = () => EfModelSnapshotLocator.TryLocate(_root, new[] { snapshot }, 1_048_576, fileInfo: _ => throw new FileNotFoundException("race"));

        var located = act.Should().NotThrow().Subject;
        located.Found.Should().BeFalse();
        located.Diagnostics.Should().ContainSingle().Which.Should().Be("ef model auto-detect skipped: race");
    }

    [Fact]
    public void TryLocate_Utf8Bom_IsStripped()
    {
        var snapshot = Path.Combine(_root, "ShopContextModelSnapshot.cs");
        File.WriteAllText(snapshot, SnapshotSource, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        EfModelSnapshotLocator.TryLocate(_root, new[] { snapshot }, 1_048_576).Source.Should().Be(SnapshotSource);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
