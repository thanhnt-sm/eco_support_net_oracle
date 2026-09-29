// <copyright file="RuleInventory.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>One RuleExecuted progress event as recorded for the end-of-run banner.</summary>
internal readonly struct RuleInventoryItem
{
    public RuleInventoryItem(string? ruleId, string? ruleTitle, int violationCount)
    {
        this.RuleId = ruleId;
        this.RuleTitle = ruleTitle;
        this.ViolationCount = violationCount;
    }

    public string? RuleId { get; }

    public string? RuleTitle { get; }

    public int ViolationCount { get; }
}

/// <summary>Thread-safe collection of rules executed during the current run.</summary>
internal sealed class RuleInventory
{
    private readonly object gate = new();
    private readonly List<RuleInventoryItem> items = new();

    public void Clear()
    {
        lock (this.gate)
        {
            this.items.Clear();
        }
    }

    /// <summary>
    /// Reserves the run slot and only then clears the inventory, so a command issued while another run
    /// is active neither wipes that run's banner data nor starts.
    /// </summary>
    internal static bool ClearInventoryIfReserved(CliProcessRegistry registry, RuleInventory inventory)
    {
        if (!registry.TryReserve())
        {
            return false;
        }

        inventory.Clear();
        return true;
    }

    public void Add(RuleInventoryItem item)
    {
        lock (this.gate)
        {
            this.items.Add(item);
        }
    }

    public IReadOnlyList<RuleInventoryItem> Snapshot()
    {
        lock (this.gate)
        {
            return this.items.ToList();
        }
    }

    public string BuildBanner()
    {
        return BuildRuleInventoryBanner(this.Snapshot());
    }

    internal static string BuildRuleInventoryBanner(IReadOnlyList<RuleInventoryItem> inventory)
    {
        if (inventory == null || inventory.Count == 0)
        {
            return string.Empty;
        }

        var evaluated = inventory.Where(r => !string.IsNullOrEmpty(r.RuleId) && r.ViolationCount >= 0).ToList();
        var withViolations = inventory.Where(r => !string.IsNullOrEmpty(r.RuleId) && r.ViolationCount > 0).ToList();

        var banner = new StringBuilder();
        banner.AppendLine("[DataGuard] ==================== Validation Summary ====================");
        banner.AppendLine($"[DataGuard] Rules Evaluated: {evaluated.Count} ({string.Join(", ", evaluated.Select(r => r.RuleId))})");
        if (withViolations.Count > 0)
        {
            banner.AppendLine($"[DataGuard] Rules with Findings: {string.Join(", ", withViolations.Select(r => $"{r.RuleId} ({r.ViolationCount})"))}");
        }

        banner.AppendLine("[DataGuard] Double-click any Error List item to jump directly to code.");
        banner.AppendLine("[DataGuard] ==========================================================");
        return banner.ToString();
    }
}
