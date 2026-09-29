// <copyright file="CliLocator.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Locates dataguard.exe: custom absolute path → bundled cli\dataguard.exe → DATAGUARD_CLI_PATH →
/// standard install locations → PATH. A custom path must be absolute: relative paths are never
/// resolved against the solution directory, so a repository cannot ship an executable that satisfies
/// the user's setting.
/// </summary>
public static class CliLocator
{
    private const string CliFileName = "dataguard.exe";

    public static string FindCliExecutable(string? customCliPath, string? extensionDirectory = null, string? solutionDirectory = null)
    {
        var normalizedCustom = customCliPath?.Trim(' ', '"');
        if (!string.IsNullOrEmpty(normalizedCustom))
        {
            // Rooted is not enough: "\tools\x.exe" and "C:x.exe" are rooted yet resolve against devenv's
            // current drive/directory. Require a fully qualified drive or UNC path.
            return IsFullyQualified(normalizedCustom!) && IsValidExecutablePath(normalizedCustom, requireRooted: true)
                ? normalizedCustom!
                : string.Empty;
        }

        var bundled = FindBundledCli(extensionDirectory);
        if (bundled != null)
        {
            return bundled;
        }

        var envPath = Environment.GetEnvironmentVariable("DATAGUARD_CLI_PATH")?.Trim(' ', '"');
        if (IsValidExecutablePath(envPath, requireRooted: true))
        {
            return envPath!;
        }

        foreach (var candidate in StandardInstallCandidates())
        {
            if (IsValidExecutablePath(candidate, requireRooted: true))
            {
                return candidate;
            }
        }

        return FindOnPath() ?? string.Empty;
    }

    private static string? FindBundledCli(string? extensionDirectory)
    {
        var extDir = extensionDirectory;
        if (string.IsNullOrEmpty(extDir))
        {
            try
            {
                var asm = typeof(CliLocator).Assembly;
                var location = asm.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    extDir = Path.GetDirectoryName(location);
                }

                // Fallback to CodeBase if Location is empty or the bundled CLI is not present (handles shadow copying in VS).
                if (string.IsNullOrEmpty(extDir) || !File.Exists(Path.Combine(extDir, "cli", CliFileName)))
                {
                    var codeBase = asm.CodeBase;
                    if (!string.IsNullOrEmpty(codeBase) && Uri.TryCreate(codeBase, UriKind.Absolute, out var uri) && uri.IsFile)
                    {
                        var codeBaseDir = Path.GetDirectoryName(uri.LocalPath);
                        if (!string.IsNullOrEmpty(codeBaseDir) && File.Exists(Path.Combine(codeBaseDir, "cli", CliFileName)))
                        {
                            extDir = codeBaseDir;
                        }
                    }
                }
            }
            catch
            {
                // Ignore reflection/path format errors in dynamic AppDomains.
            }
        }

        if (string.IsNullOrEmpty(extDir))
        {
            return null;
        }

        var bundledCli = Path.Combine(extDir, "cli", CliFileName);
        return File.Exists(bundledCli) ? bundledCli : null;
    }

    private static IEnumerable<string> StandardInstallCandidates()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, ".dotnet", "tools", CliFileName);
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "DataGuard", CliFileName);
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "DataGuard", CliFileName);
        }
    }

    private static string? FindOnPath()
    {
        var systemPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(systemPath))
        {
            return null;
        }

        foreach (var dir in systemPath.Split(Path.PathSeparator))
        {
            try
            {
                var trimmed = dir.Trim(' ', '"');
                if (string.IsNullOrWhiteSpace(trimmed) || !Path.IsPathRooted(trimmed))
                {
                    continue;
                }

                var file = Path.Combine(trimmed, CliFileName);
                if (IsValidExecutablePath(file, requireRooted: true))
                {
                    return file;
                }
            }
            catch
            {
                // Ignore invalid PATH entries.
            }
        }

        return null;
    }

    internal static bool IsFullyQualified(string path)
    {
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal);
    }

    internal static bool IsValidExecutablePath(string? path, bool requireRooted = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (requireRooted && !Path.IsPathRooted(path))
            {
                return false;
            }

            return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
