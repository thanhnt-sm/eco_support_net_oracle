// <copyright file="DescriptorCatalogParityTests.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers.Tests;

using System.IO;
using System.Text.RegularExpressions;
using DataGuard.Analyzers;
using FluentAssertions;
using Xunit;

/// <summary>
/// <see cref="DiagnosticDescriptors"/> is the single source of analyzer IDs/titles; an ID the analyzer shares with the
/// CLI rules engine must carry the engine's <c>ProviderRuleCatalog.RuleTitles</c> text, and analyzer-only heuristics
/// (DG097-DG099) must not reuse an engine ID. The CLI file is read as text so this net9.0 test project does not take
/// a dependency on the CLI.
/// </summary>
public class DescriptorCatalogParityTests
{
    private static readonly Regex TitleEntry = new(@"\[""(?<id>[A-Z]+\d+)""\]\s*=\s*""(?<title>[^""]*)""", RegexOptions.CultureInvariant);

    [Fact]
    public void SharedIds_HaveTheEngineTitle()
    {
        var engineTitles = ReadEngineTitles();
        engineTitles.Should().ContainKey("DG001").And.ContainKey("DG017");

        var shared = 0;
        foreach (var descriptor in DiagnosticDescriptors.All)
        {
            if (engineTitles.TryGetValue(descriptor.Id, out var engineTitle))
            {
                descriptor.Title.ToString().Should().Be(engineTitle, $"{descriptor.Id} is shared with ProviderRuleCatalog.RuleTitles");
                shared++;
            }
        }

        shared.Should().Be(17, "DG001-DG017 are shared between the analyzer package and the CLI engine");
    }

    [Fact]
    public void AnalyzerOnlyIds_AreNotEngineIds()
    {
        var engineTitles = ReadEngineTitles();
        engineTitles.Should().NotContainKey(DiagnosticIds.StoredProcedureCommandText)
            .And.NotContainKey(DiagnosticIds.MissingFromClause)
            .And.NotContainKey(DiagnosticIds.SqlInjectionPattern);
    }

    [Fact]
    public void All_ContainsTheGeneratorAndEveryAnalyzerDescriptorOnce()
    {
        DiagnosticDescriptors.All.Should().HaveCount(DiagnosticDescriptors.AnalyzerDescriptors.Length + 1);
        DiagnosticDescriptors.All.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        new ContractValidationAnalyzer().SupportedDiagnostics.Should().Equal(DiagnosticDescriptors.AnalyzerDescriptors);
    }

    private static Dictionary<string, string> ReadEngineTitles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "DataGuard.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test runs inside the repository");
        var path = Path.Combine(directory!.FullName, "src", "DataGuard.Cli", "ProviderRuleCatalog.cs");
        var text = File.ReadAllText(path);
        var start = text.IndexOf("RuleTitles", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = text.IndexOf("};", start, StringComparison.Ordinal);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in TitleEntry.Matches(text.Substring(start, end - start)))
        {
            titles[match.Groups["id"].Value] = match.Groups["title"].Value;
        }

        return titles;
    }
}
