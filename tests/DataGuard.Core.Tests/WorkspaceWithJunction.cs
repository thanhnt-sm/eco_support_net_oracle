namespace DataGuard.Core.Tests;

/// <summary>
/// Temp layout <c>root/ws</c> (the workspace) and <c>root/outside-target</c>, joined by a directory link that is either
/// committed inside the workspace (<c>ws/junction</c>: repository-controlled) or a sibling of it
/// (<c>root/sibling-junction</c>: host-chosen). Disposal removes the link non-recursively first so the recursive
/// delete never follows it into the target.
/// </summary>
internal sealed class WorkspaceWithJunction : IDisposable
{
    private readonly string _root;

    /// <summary>Initializes a new instance of the <see cref="WorkspaceWithJunction"/> class: the workspace, the target and the link between them.</summary>
    /// <param name="prefix">Temp directory prefix, one per test so a leaked directory is attributable.</param>
    /// <param name="junctionInsideWorkspace">True for <c>ws/junction</c>; false for a sibling junction outside the workspace.</param>
    /// <param name="targetSubdirectory">Optional pre-created subdirectory of the target, for output pointed below the link.</param>
    public WorkspaceWithJunction(string prefix, bool junctionInsideWorkspace, string? targetSubdirectory = null)
    {
        _root = Directory.CreateTempSubdirectory(prefix).FullName;
        Workspace = Path.Combine(_root, "ws");
        Target = Path.Combine(_root, "outside-target");
        Junction = junctionInsideWorkspace ? Path.Combine(Workspace, "junction") : Path.Combine(_root, "sibling-junction");
        Directory.CreateDirectory(Workspace);
        Directory.CreateDirectory(targetSubdirectory is null ? Target : Path.Combine(Target, targetSubdirectory));
        DirectoryLinkTestHelper.CreateDirectoryLink(Junction, Target);
    }

    /// <summary>Workspace root handed to the CLI (<c>--project</c> / <c>--workspace</c> and working directory).</summary>
    public string Workspace { get; }

    /// <summary>Real directory the junction resolves to; assertions inspect it for files that must (not) land.</summary>
    public string Target { get; }

    /// <summary>The directory link; output paths are built under it.</summary>
    public string Junction { get; }

    /// <summary>Writes a source or project file into the workspace and returns its path.</summary>
    public string WriteWorkspaceFile(string fileName, string content)
    {
        var path = Path.Combine(Workspace, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        // Defensive: a constructor failure never reaches Dispose, so this only guards an externally removed link.
        if (Directory.Exists(Junction))
        {
            Directory.Delete(Junction, recursive: false);
        }

        Directory.Delete(_root, recursive: true);
    }
}
