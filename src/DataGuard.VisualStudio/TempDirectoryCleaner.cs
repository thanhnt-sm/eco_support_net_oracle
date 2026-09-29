// <copyright file="TempDirectoryCleaner.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;

/// <summary>
/// Best-effort hygiene for the per-run SARIF temp directories. Reparse points are never followed:
/// a junction is unlinked, never recursed into, so a planted link cannot redirect the delete.
/// </summary>
internal static class TempDirectoryCleaner
{
    private static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(15);

    internal static string CreateRunDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DataGuard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static void CleanStaleTempDirectories()
    {
        try
        {
            var now = DateTime.UtcNow;
            var tempBase = Path.GetTempPath();
            var tempRoot = Path.Combine(tempBase, "DataGuard");
            if (Directory.Exists(tempRoot))
            {
                if (IsReparsePoint(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: false);
                }
                else
                {
                    SweepStaleChildren(tempRoot, "*", now);
                }
            }

            // Also sweep dataguard-* directories created by older versions / the VS Code extension.
            SweepStaleChildren(tempBase, "dataguard-*", now);
        }
        catch
        {
            // Best-effort sweep; ignore failures so operations are never blocked.
        }
    }

    private static void SweepStaleChildren(string parent, string pattern, DateTime now)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parent, pattern))
            {
                try
                {
                    if (IsReparsePoint(dir))
                    {
                        Directory.Delete(dir, recursive: false);
                        continue;
                    }

                    var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                    var creation = Directory.GetCreationTimeUtc(dir);
                    var latest = lastWrite > creation ? lastWrite : creation;
                    if (now - latest > StaleAge)
                    {
                        SafeDeleteDirectory(dir);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch
        {
        }
    }

    internal static void SafeDeleteDirectory(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);
            if (IsReparsePoint(dirInfo))
            {
                ClearProtectiveAttributes(dirInfo);
                Directory.Delete(path, recursive: false);
                return;
            }

            try
            {
                foreach (var subDir in Directory.GetDirectories(path))
                {
                    try
                    {
                        SafeDeleteDirectory(subDir);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    TryDeleteFile(file);
                }
            }
            catch
            {
            }

            ClearProtectiveAttributes(dirInfo);
            Directory.Delete(path, recursive: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
        }
        catch
        {
        }
    }

    private static bool IsReparsePoint(string path) => IsReparsePoint(new DirectoryInfo(path));

    private static bool IsReparsePoint(DirectoryInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;

    private static void ClearProtectiveAttributes(DirectoryInfo info)
    {
        if ((info.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden)) != 0)
        {
            info.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.Hidden);
        }
    }
}
