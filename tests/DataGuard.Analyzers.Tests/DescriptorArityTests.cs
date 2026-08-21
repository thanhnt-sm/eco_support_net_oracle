// <copyright file="DescriptorArityTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

using System.Text.RegularExpressions;
using DataGuard.Analyzers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Analyzers.Tests;

/// <summary>
/// Guard the analyzer surface: every emitted diagnostic must carry exactly one
/// message argument (the fully formatted violation message), so descriptor
/// messageFormat placeholders and Diagnostic.Create arity cannot drift apart.
/// </summary>
public class DescriptorArityTests
{
    private static readonly ContractValidationAnalyzer Analyzer = new();

    [Fact]
    public void AllDescriptors_HaveExactlyOnePlaceholder()
    {
        var bad = new List<string>();
        foreach (var descriptor in Analyzer.SupportedDiagnostics)
        {
            var format = descriptor.MessageFormat.ToString();
            var placeholders = Regex.Matches(format, @"\{(\d+)\}").Select(m => int.Parse(m.Groups[1].Value)).ToList();
            if (placeholders.Count != 1 || placeholders[0] != 0)
            {
                bad.Add($"{descriptor.Id}: '{format}' has {placeholders.Count} placeholder(s)");
            }
        }

        bad.Should().BeEmpty($"descriptors must use a single {{0}} placeholder: {string.Join("; ", bad)}");
    }

    [Fact]
    public void DescriptorIds_AreUnique()
    {
        var ids = Analyzer.SupportedDiagnostics.Select(d => d.Id).ToList();
        ids.Count.Should().Be(ids.Distinct().Count());
    }

    [Fact]
    public void DiagnosticIds_ConstantsMatchDescriptors()
    {
        // DG001 (UnvalidatedSqlCall) is emitted by the incremental generator with its
        // own descriptor, not by ContractValidationAnalyzer - checked separately.
        Analyzer.SupportedDiagnostics.Select(d => d.Id)
            .Should().Contain(DiagnosticIds.MissingFromClause)
            .And.Contain(DiagnosticIds.SqlInjectionPattern)
            .And.Contain(DiagnosticIds.PhantomTable)
            .And.Contain(DiagnosticIds.PhantomColumn);
    }
}
