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
    /// <summary>Reports a missing file, otherwise invokes and awaits the caller's navigation operation.</summary>
    /// <param name="document">The Error List task's document path.</param>
    /// <param name="writeOutput">Optional output sink, absent for a presenter without output services.</param>
    /// <param name="navigate">The shell operation; invoked on the calling thread only when the file exists.</param>
    /// <returns>A task covering output or navigation completion and failures.</returns>
    internal static async Task NavigateAsync(string document, Func<string, Task>? writeOutput, Func<Task> navigate)
    {
        if (!File.Exists(document))
        {
            if (writeOutput != null)
            {
                await writeOutput($"[StackAccord] Cannot navigate: file not found '{document}'.\r\n").ConfigureAwait(false);
            }

            return;
        }

        await navigate().ConfigureAwait(false);
    }
}
