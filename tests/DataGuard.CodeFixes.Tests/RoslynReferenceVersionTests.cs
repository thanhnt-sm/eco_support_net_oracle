// <copyright file="RoslynReferenceVersionTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.CodeFixes.Tests;

using System;
using System.Linq;
using DataGuard.Analyzers.CodeFixes;
using FluentAssertions;
using Xunit;

/// <summary>
/// Deterministic guard for the compiler line the code-fix assembly is built against (see the analyzer test of the
/// same name). The code fixes load next to the analyzer inside VS 2022 17.12 / the .NET 9.0.1xx SDK, which run
/// Roslyn 4.12 and refuse assemblies that reference a newer Microsoft.CodeAnalysis.
/// </summary>
public class RoslynReferenceVersionTests
{
    /// <summary>Highest Microsoft.CodeAnalysis(.Workspaces) assembly version the bundled code fixes may reference.</summary>
    internal static readonly Version MaximumRoslynVersion = new(4, 12, 0, 0);

    [Fact]
    public void CodeFixAssembly_ReferencesMicrosoftCodeAnalysis_AtMost4_12()
    {
        var referenced = typeof(DataGuardCodeFixProvider).Assembly.GetReferencedAssemblies();

        var roslyn = referenced.Where(a => a.Name!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)).ToList();
        roslyn.Select(a => a.Name).Should().Contain("Microsoft.CodeAnalysis", "the code fixes must reference Microsoft.CodeAnalysis");
        roslyn.Select(a => a.Name).Should().Contain("Microsoft.CodeAnalysis.Workspaces", "the code fixes must reference Microsoft.CodeAnalysis.Workspaces");

        foreach (var name in roslyn)
        {
            name.Version.Should().BeLessThanOrEqualTo(
                MaximumRoslynVersion,
                $"{name.Name} must stay on the Roslyn 4.12 line that VS 2022 17.12 and the .NET 9.0.1xx SDK run (CS9057 otherwise)");
        }
    }
}
