using System.Diagnostics;
using System.Diagnostics.Tracing;
using DataGuard.Core.Validation;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Telemetry;

/// <summary>Local opt-in graph execution events. No contract data or disk/network sink.</summary>
[EventSource(Name = "DataGuard-Validation")]
internal sealed class ValidationTrace : EventSource
{
    internal static readonly ValidationTrace Log = new();
    private readonly ValidationTraceBudget _budget = new(1000, 60 * Stopwatch.Frequency);
    private long _sequence;

    [NonEvent]
    internal long TryBegin(IReadOnlyList<ContractDescriptor>? contracts, bool concurrent)
    {
        if (!IsEnabled(EventLevel.Informational, EventKeywords.None))
        {
            return 0;
        }
        try
        {
            if (!_budget.TryAdmit(Stopwatch.GetTimestamp()))
            {
                return 0;
            }
            var id = Interlocked.Increment(ref _sequence);
            RequestBegin(id, contracts?.Count ?? -1, concurrent ? 1 : 0);
            return id;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [Event(1, Version = 1, Level = EventLevel.Informational)]
    public void RequestBegin(long requestId, int contracts, int concurrent)
        => WriteEvent(1, new object[] { requestId, contracts, concurrent });

    [Event(2, Version = 1, Level = EventLevel.Informational)]
    public void RequestEnd(long requestId, double elapsedMs, int outcome, int violations, int unevaluated, int dropped, int reasonFlags)
        => WriteEvent(2, new object[] { requestId, elapsedMs, outcome, violations, unevaluated, dropped, reasonFlags });

    [NonEvent]
    internal void End(long id, long started, int outcome, ValidationExecutionResult? result = null)
    {
        try
        {
            // Capture elapsed before counting/reporting: graph execution, not telemetry or compiler/evaluator time.
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var flags = 0;
            if (result is not null)
            {
                if (result.DroppedViolationCount > 0)
                {
                    flags |= 1;
                }
                if (result.UnevaluatedContracts.Count > 0)
                {
                    flags |= 2;
                }
                foreach (var rule in result.RuleOutcomes)
                {
                    if (rule.State == RuleExecutionState.Failed)
                    {
                        flags |= 4;
                    }
                    if (rule.State == RuleExecutionState.Unavailable)
                    {
                        flags |= 8;
                    }
                    if (rule.FailureReason is "TimeoutException" or "RegexMatchTimeoutException")
                    {
                        flags |= 16;
                    }
                }
            }
            RequestEnd(id, elapsed, outcome, result?.Violations.Count ?? -1,
                result?.UnevaluatedContracts.Count ?? -1, result?.DroppedViolationCount ?? -1, flags);
        }
        catch (Exception)
        {
            // Listener failures cannot replace validation results.
        }
    }
}

/// <summary>One process-lifetime budget; listener reconnects cannot replenish it.</summary>
internal sealed class ValidationTraceBudget(int limit, long durationTicks)
{
    private long _started;
    private int _admitted;

    internal bool TryAdmit(long now)
    {
        var started = Interlocked.CompareExchange(ref _started, now, 0);
        if (started == 0)
        {
            started = now;
        }
        if (now - started >= durationTicks)
        {
            return false;
        }
        while (true)
        {
            var admitted = Volatile.Read(ref _admitted);
            if (admitted >= limit)
            {
                return false;
            }
            if (Interlocked.CompareExchange(ref _admitted, admitted + 1, admitted) == admitted)
            {
                return true;
            }
        }
    }
}
