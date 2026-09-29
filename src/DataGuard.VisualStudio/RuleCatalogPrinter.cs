// <copyright file="RuleCatalogPrinter.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>Prints the rule catalog (Tools → DataGuard → Validation Rules &amp; Descriptions) to the Output pane.</summary>
internal static class RuleCatalogPrinter
{
    private const string Separator = "========================================================================\r\n";
    private const string Divider = "------------------------------------------------------------------------\r\n";

    public static async Task PrintAsync(IReadOnlyList<DataGuardRulesOptionsPage.RuleDescriptor> rules, OutputPaneWriter output)
    {
        await output.ActivateAsync();
        await output.WriteAsync(Separator + "DataGuard Validation Rules — Current Configuration\r\n" + Separator);

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
            await output.WriteAsync($"\r\nCategory: {category}\r\n" + Divider);
            foreach (var rule in grouped[category])
            {
                var status = rule.IsEnabled ? "[ENABLED] " : "[DISABLED]";
                await output.WriteAsync($"{status} {rule.Id}: {rule.Name}\r\n           {rule.Description}\r\n");
            }
        }

        await output.WriteAsync(
            "\r\n" + Separator +
            "[ENABLED] = enabled    [DISABLED] = disabled\r\n" +
            "To change rules: Tools -> Options -> DataGuard -> Validation Rules.\r\n" +
            Separator);
    }
}
