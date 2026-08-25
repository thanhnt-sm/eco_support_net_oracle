// <copyright file="CodeFixProviderTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.CodeFixes.Tests;

using DataGuard.Analyzers;
using DataGuard.Analyzers.CodeFixes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

/// <summary>
/// Tests for DataGuard code fix providers.
/// Verifies that each provider can fix the expected diagnostic IDs.
/// </summary>
public class CodeFixProviderTests
{
    [Fact]
    public void AddMaxLengthAttributeFixProvider_FixesLengthExceedsColumn()
    {
        // Arrange
        var provider = new AddMaxLengthAttributeFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.LengthExceedsColumn);
    }

    [Fact]
    public void AddMaxLengthAttributeFixProvider_FixesInferredSizeFallback()
    {
        // Arrange
        var provider = new AddMaxLengthAttributeFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.InferredSizeFallback);
    }

    [Fact]
    public void AddMaxLengthAttributeFixProvider_HasBatchFixer()
    {
        // Arrange
        var provider = new AddMaxLengthAttributeFixProvider();

        // Act
        var fixAllProvider = provider.GetFixAllProvider();

        // Assert
        fixAllProvider.Should().NotBeNull();
        fixAllProvider.Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void SkipContractCheckFixProvider_FixesUnvalidatedSqlCall()
    {
        // Arrange
        var provider = new SkipContractCheckFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.UnvalidatedSqlCall);
    }

    [Fact]
    public void SkipContractCheckFixProvider_HasBatchFixer()
    {
        // Arrange
        var provider = new SkipContractCheckFixProvider();

        // Act
        var fixAllProvider = provider.GetFixAllProvider();

        // Assert
        fixAllProvider.Should().NotBeNull();
    }

    [Fact]
    public void DataGuardCodeFixProvider_FixesUnvalidatedSqlCall()
    {
        // Arrange
        var provider = new DataGuardCodeFixProvider();

        // Act
        var fixableIds = provider.FixableDiagnosticIds;

        // Assert
        fixableIds.Should().Contain(DiagnosticIds.UnvalidatedSqlCall);
    }

    [Fact]
    public void DiagnosticIds_UnvalidatedSqlCall_IsDG001()
    {
        // Assert
        DiagnosticIds.UnvalidatedSqlCall.Should().Be("DG001");
    }

    [Fact]
    public void DiagnosticIds_ParameterMismatch_IsDG002()
    {
        // Assert
        DiagnosticIds.ParameterMismatch.Should().Be("DG002");
    }

    [Fact]
    public void DiagnosticIds_LengthExceedsColumn_IsDG007()
    {
        // Assert
        DiagnosticIds.LengthExceedsColumn.Should().Be("DG007");
    }

    [Fact]
    public void DiagnosticIds_InferredSizeFallback_IsDG009()
    {
        // Assert
        DiagnosticIds.InferredSizeFallback.Should().Be("DG009");
    }

    [Fact]
    public void AllCodeFixProviders_ImplementGetFixAllProvider()
    {
        // Arrange
        var providers = new CodeFixProvider[]
        {
            new DataGuardCodeFixProvider(),
            new AddMaxLengthAttributeFixProvider(),
            new SkipContractCheckFixProvider(),
        };

        // Act & Assert
        foreach (var provider in providers)
        {
            provider.GetFixAllProvider().Should().NotBeNull(
                because: $"provider {provider.GetType().Name} should support FixAll");
        }
    }

    [Fact]
    public void AllCodeFixProviders_HaveNonEmptyFixableDiagnosticIds()
    {
        // Arrange
        var providers = new CodeFixProvider[]
        {
            new DataGuardCodeFixProvider(),
            new AddMaxLengthAttributeFixProvider(),
            new SkipContractCheckFixProvider(),
        };

        // Act & Assert
        foreach (var provider in providers)
        {
            provider.FixableDiagnosticIds.Should().NotBeEmpty(
                because: $"provider {provider.GetType().Name} should fix at least one diagnostic");
        }
    }
}
