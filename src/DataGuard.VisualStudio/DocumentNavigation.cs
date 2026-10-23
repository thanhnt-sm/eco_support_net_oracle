// <copyright file="DocumentNavigation.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Threading.Tasks;

/// <summary>Guards shell navigation with the same missing-document output used by Error List tasks.</summary>
internal static class DocumentNavigation
{
    /// <summary>Reports a missing file and returns whether shell navigation may continue.</summary>
    /// <param name="document">The Error List task's document path.</param>
    /// <param name="writeOutput">Optional output sink, absent for a presenter without output services.</param>
    /// <returns>False after missing-file output completes; true when the file exists.</returns>
    internal static async Task<bool> CanNavigateAsync(string document, Func<string, Task>? writeOutput)
    {
        if (!File.Exists(document))
        {
            if (writeOutput != null)
            {
                await writeOutput($"[StackAccord] Cannot navigate: file not found '{document}'.\r\n").ConfigureAwait(false);
            }

            return false;
        }

        return true;
    }
}
