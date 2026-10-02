using System.Collections.Generic;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

public class RuleInventoryTests
{
    [Fact]
    public void TryFormatProgress_RuleExecutedWithZeroViolations_ReturnsInventoryEntry()
    {
        var json = "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Scan\",\"Data\":{\"RuleId\":\"DG001\",\"RuleTitle\":\"Title\",\"ViolationCount\":0,\"ContractCount\":5}}";

        var success = ProgressLineParser.TryFormatProgress(
            json,
            out var formatted,
            out var errorCount,
            out var warningCount,
            out var inventoryEntry);

        success.Should().BeTrue();
        formatted.Should().BeNull();
        inventoryEntry.Should().NotBeNull();
        inventoryEntry!.Value.RuleId.Should().Be("DG001");
        inventoryEntry.Value.RuleTitle.Should().Be("Title");
        inventoryEntry.Value.ViolationCount.Should().Be(0);
    }

    [Fact]
    public void TryFormatProgress_RuleExecutedWithViolations_ReturnsInventoryEntry()
    {
        var json = "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Scan\",\"Data\":{\"RuleId\":\"DG001\",\"RuleTitle\":\"Title\",\"ViolationCount\":3,\"ContractCount\":5}}";

        var success = ProgressLineParser.TryFormatProgress(
            json,
            out var formatted,
            out var errorCount,
            out var warningCount,
            out var inventoryEntry);

        success.Should().BeTrue();
        formatted.Should().NotBeNull();
        formatted.Should().Contain("3 violations");
        inventoryEntry.Should().NotBeNull();
        inventoryEntry!.Value.RuleId.Should().Be("DG001");
        inventoryEntry.Value.RuleTitle.Should().Be("Title");
        inventoryEntry.Value.ViolationCount.Should().Be(3);
    }

    [Fact]
    public void RuleInventoryBanner_ThreeRulesOneSummary_ContainsAllRuleIds()
    {
        var rules = new List<string>
        {
            "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Scan\",\"Data\":{\"RuleId\":\"DG001\",\"RuleTitle\":\"Title 1\",\"ViolationCount\":0,\"ContractCount\":5}}",
            "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Scan\",\"Data\":{\"RuleId\":\"DG002\",\"RuleTitle\":\"Title 2\",\"ViolationCount\":2,\"ContractCount\":5}}",
            "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Scan\",\"Data\":{\"RuleId\":\"DG010\",\"RuleTitle\":\"Title 10\",\"ViolationCount\":0,\"ContractCount\":5}}"
        };

        var inventory = new List<RuleInventoryItem>();
        foreach (var ruleJson in rules)
        {
            var success = ProgressLineParser.TryFormatProgress(
                ruleJson,
                out _,
                out _,
                out _,
                out var entry);

            success.Should().BeTrue();
            if (entry.HasValue)
            {
                inventory.Add(entry.Value);
            }
        }

        var banner = RuleInventory.BuildRuleInventoryBanner(inventory);

        banner.Should().Contain("Rules Evaluated: 3");
        banner.Should().Contain("DG001");
        banner.Should().Contain("DG002");
        banner.Should().Contain("DG010");
        banner.Should().Contain("Rules with Findings: DG002 (2)");
    }

    [Fact]
    public void TryFormatProgress_SummaryEvent_DoesNotReturnInventoryEntry()
    {
        var json = "{\"Kind\":\"Summary\",\"Phase\":\"Completed\",\"Data\":{\"ErrorCount\":0,\"WarningCount\":0}}";

        var success = ProgressLineParser.TryFormatProgress(
            json,
            out var formatted,
            out var errorCount,
            out var warningCount,
            out var inventoryEntry);

        success.Should().BeTrue();
        inventoryEntry.Should().BeNull();
    }

    [Fact]
    public void RuleInventory_ConcurrentAddSnapshotAndClear_IsThreadSafe()
    {
        var inventory = new RuleInventory();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        const int iterations = 1000;

        System.Threading.Tasks.Parallel.Invoke(
            () =>
            {
                // Thread 1: simulating stderr reader appending items rapidly
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        inventory.Add(new RuleInventoryItem($"DG{i:D3}", $"Rule {i}", i % 5));
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            },
            () =>
            {
                // Thread 2: simulating UI thread creating snapshots / reading banner
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        var snapshot = inventory.Snapshot();
                        snapshot.Should().NotBeNull();
                        _ = inventory.BuildBanner();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            },
            () =>
            {
                // Thread 3: simulating user or session clearing inventory
                try
                {
                    for (var i = 0; i < iterations; i++)
                    {
                        if (i % 50 == 0)
                        {
                            inventory.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        exceptions.Should().BeEmpty("Concurrent access to RuleInventory must never throw collection modified or concurrency exceptions");
    }
}
