// <copyright file="RoslynReferenceVersionTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.Tests;

using System;
using System.Linq;
using DataGuard.Analyzers;
using FluentAssertions;
using Xunit;

/// <summary>
/// Deterministic guard for the compiler line the analyzer is built against. Roslyn refuses an analyzer compiled
/// against a newer <c>Microsoft.CodeAnalysis</c> than the running compiler (CS9057, no diagnostics at all), and
/// VS 2022 17.12 / the .NET 9.0.1xx SDK run Roslyn 4.12. The test reads the assembly references of the built
/// analyzer, so it does not depend on the SDK installed on the machine that runs it.
/// </summary>
public class RoslynReferenceVersionTests
{
    /// <summary>Highest Microsoft.CodeAnalysis assembly version the bundled analyzer may reference.</summary>
    internal static readonly Version MaximumRoslynVersion = new(4, 12, 0, 0);

    [Fact]
    public void AnalyzerAssembly_ReferencesMicrosoftCodeAnalysis_AtMost4_12()
    {
        var referenced = typeof(ContractValidationAnalyzer).Assembly.GetReferencedAssemblies();

        var roslyn = referenced.SingleOrDefault(a => a.Name == "Microsoft.CodeAnalysis");
        roslyn.Should().NotBeNull("the analyzer must reference Microsoft.CodeAnalysis");
        roslyn!.Version.Should().BeLessThanOrEqualTo(
            MaximumRoslynVersion,
            "VS 2022 17.12 and the .NET 9.0.1xx SDK run Roslyn 4.12 and refuse analyzers built against a newer compiler (CS9057)");

        foreach (var name in referenced.Where(a => a.Name!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)))
        {
            name.Version.Should().BeLessThanOrEqualTo(MaximumRoslynVersion, $"{name.Name} must stay on the Roslyn 4.12 line");
        }
    }
}
