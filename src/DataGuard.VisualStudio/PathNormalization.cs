// <copyright file="PathNormalization.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;

/// <summary>Path normalisation shared by the consent key and the solution re-check.</summary>
internal static class PathNormalization
{
    /// <summary>
    /// Full path without a trailing separator; the input itself (trimmed) when it cannot be resolved.
    /// Casing is left to the caller: the consent key upper-cases, the solution re-check compares
    /// case-insensitively.
    /// </summary>
    internal static string NormalizeFullPath(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            full = path;
        }

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
