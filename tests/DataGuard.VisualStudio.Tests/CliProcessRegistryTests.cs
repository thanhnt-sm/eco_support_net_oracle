using System.Diagnostics;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// The run slot: one reservation at a time, visible before the process starts, and a Cancel issued
/// while the run is still waiting for consent must abort it without recording consent.
/// </summary>
public class CliProcessRegistryTests
{
    [Fact]
    public void TryReserve_Twice_SecondReturnsFalse()
    {
        var registry = new CliProcessRegistry();

        registry.TryReserve().Should().BeTrue();
        registry.TryReserve().Should().BeFalse();
        registry.ReleaseReservation();
        registry.TryReserve().Should().BeTrue();
    }

    [Fact]
    public void IsReserved_TrueAfterReserve_BeforeStartAndRegister()
    {
        var registry = new CliProcessRegistry();

        registry.IsReserved.Should().BeFalse();
        registry.TryReserve().Should().BeTrue();
        registry.IsReserved.Should().BeTrue();
        registry.Active.Should().BeNull();
        registry.Complete(null);
        registry.IsReserved.Should().BeFalse();
    }

    [Fact]
    public void RequestCancelPending_WhileReservedWithoutProcess_PreventsStart()
    {
        var registry = new CliProcessRegistry();
        registry.TryReserve().Should().BeTrue();

        registry.RequestCancelPending().Should().BeTrue();
        registry.ShouldStartAfterConsent().Should().BeFalse();
    }

    [Fact]
    public void RequestCancelPending_WithoutReservation_IsIgnored()
    {
        var registry = new CliProcessRegistry();

        registry.RequestCancelPending().Should().BeFalse();
        registry.TryReserve().Should().BeTrue();
        registry.ShouldStartAfterConsent().Should().BeTrue();
    }

    [Fact]
    public void CancelPending_IsResetByReleaseAndComplete()
    {
        var registry = new CliProcessRegistry();
        registry.TryReserve().Should().BeTrue();
        registry.RequestCancelPending().Should().BeTrue();
        registry.ReleaseReservation();

        registry.TryReserve().Should().BeTrue();
        registry.ShouldStartAfterConsent().Should().BeTrue("a stale cancel must not abort the next run");
        registry.RequestCancelPending().Should().BeTrue();
        registry.Complete(null);

        registry.TryReserve().Should().BeTrue();
        registry.ShouldStartAfterConsent().Should().BeTrue();
    }

    [Fact]
    public void ClearInventoryIfReserved_WhenSlotBusy_KeepsInventoryAndReturnsFalse()
    {
        var registry = new CliProcessRegistry();
        var inventory = new RuleInventory();
        registry.TryReserve().Should().BeTrue("simulates a run in progress");
        inventory.Add(new RuleInventoryItem("DG001", "Title", 2));

        RuleInventory.ClearInventoryIfReserved(registry, inventory).Should().BeFalse();

        inventory.Snapshot().Should().ContainSingle(item => item.RuleId == "DG001");
    }

    [Fact]
    public void ClearInventoryIfReserved_WhenSlotFree_ReservesAndClears()
    {
        var registry = new CliProcessRegistry();
        var inventory = new RuleInventory();
        inventory.Add(new RuleInventoryItem("DG001", "Title", 2));

        RuleInventory.ClearInventoryIfReserved(registry, inventory).Should().BeTrue();

        inventory.Snapshot().Should().BeEmpty();
        registry.IsReserved.Should().BeTrue();
    }

    [Fact]
    public void StartAndRegister_UsesInjectedStarter_AndTracksActiveProcess()
    {
        var registry = new CliProcessRegistry();
        registry.TryReserve().Should().BeTrue();
        using var process = new Process();
        Process? started = null;

        registry.StartAndRegister(process, p => started = p);

        started.Should().BeSameAs(process);
        registry.Active.Should().BeSameAs(process);
        registry.Complete(process);
        registry.Active.Should().BeNull();
        registry.IsReserved.Should().BeFalse();
    }
}
