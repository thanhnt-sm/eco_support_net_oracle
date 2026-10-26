// <copyright file="RuleCatalogPrinter.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>Prints the rule catalog (Tools → StackAccord (Beta) → Validation Rules &amp; Descriptions) to the Output pane.</summary>
internal static class RuleCatalogPrinter
{
    private const string Separator = "========================================================================";
    private const string Divider = "------------------------------------------------------------------------";

    public static async Task PrintAsync(IReadOnlyList<DataGuardRulesOptionsPage.RuleDescriptor> rules, OutputPaneWriter output)
    {
        await output.ActivateAsync();
        await output.WriteLinesAsync(new[] { Separator, "StackAccord Validation Rules — Current Configuration", Separator });

        var grouped = new Dictionary<string, List<DataGuardRulesOptionsPage.RuleDescriptor>>();
        var order = new List<string>();
        foreach (var rule in rules)
        {
            if (!grouped.TryGetValue(rule.Category, out var categoryRules))
            {
                categoryRules = new List<DataGuardRulesOptionsPage.RuleDescriptor>();
                grouped.Add(rule.Category, categoryRules);
                order.Add(rule.Category);
            }

            categoryRules.Add(rule);
        }

        foreach (var category in order)
        {
            await output.WriteLinesAsync(new[] { string.Empty, $"Category: {category}", Divider });
            foreach (var rule in grouped[category])
            {
                var status = rule.IsEnabled ? "[ENABLED] " : "[DISABLED]";
                await output.WriteLinesAsync(new[] { $"{status} {rule.Id}: {rule.Name}", $"           {rule.Description}" });
            }
        }

        await output.WriteLinesAsync(new[]
        {
            string.Empty,
            Separator,
            "[ENABLED] = enabled    [DISABLED] = disabled",
            "To change rules: Tools -> Options -> StackAccord -> Validation Rules.",
            Separator,
        });
    }
}
