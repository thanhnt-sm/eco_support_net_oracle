// <copyright file="CodeFixTestHarness.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.CodeFixes.Tests.Verification;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

/// <summary>Shared Microsoft.CodeAnalysis.Testing configuration for the code fix round-trip tests.</summary>
internal static class CodeFixTestHarness
{
    /// <summary>Gets the reference assemblies of the consumer compilation (net9.0; the analyzer itself is netstandard2.0).</summary>
    public static ReferenceAssemblies ReferenceAssemblies { get; } = ReferenceAssemblies.Net.Net90;

    /// <summary>Gets the DataGuard.Contracts reference the fixes emit attributes from.</summary>
    public static MetadataReference ContractsReference { get; } =
        MetadataReference.CreateFromFile(typeof(DataGuard.Contracts.SkipContractCheckAttribute).Assembly.Location);

    /// <summary>Gets the editorconfig contents matching the repository .editorconfig formatting rules.</summary>
    public const string EditorConfig = """
        root = true

        [*.cs]
        end_of_line = lf
        """;

    /// <summary>Configures the test to enforce LF line endings per repository .editorconfig.</summary>
    /// <typeparam name="TTest">The test type.</typeparam>
    /// <param name="test">The test instance.</param>
    /// <returns>The configured test instance.</returns>
    public static TTest WithEditorConfig<TTest>(TTest test)
        where TTest : CodeFixTest<DefaultVerifier>
    {
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", EditorConfig));
        test.FixedState.AnalyzerConfigFiles.Add(("/.editorconfig", EditorConfig));
        return test;
    }
}
