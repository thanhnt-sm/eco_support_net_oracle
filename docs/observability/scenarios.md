# Phase 2 scenario report

Dimensions analyzed: input extremes, timing, scale, state transitions, error cascades, integration, compliance, business logic. User/environment dimensions are skipped because this is a server library without a UI.

| # | Dimension | Scenario | Severity | Expected behavior |
|---:|---|---|---|---|
| 1 | Input extremes | Empty/overlong/dynamic operation name | High | Reject before span/metric emission. |
| 2 | Input extremes | Malformed `traceparent` or oversized baggage | High | Start a safe root, drop malformed/oversized context, never throw into business path. |
| 3 | Timing | Client cancellation while operation is awaiting dependency | High | Classify cancellation separately; rethrow original cancellation. |
| 4 | Timing | Collector unavailable or returns 429/5xx | Critical | Business request succeeds/fails independently; bounded exporter queue and drop metric. |
| 5 | Scale | Thousands of unique transaction/account/message IDs | Critical | No dynamic metric labels or span names; bounded series. |
| 6 | State transition | Message retry, duplicate or dead-letter attempt | High | Idempotency remains domain responsibility; attempts use links/attributes without payload. |
| 7 | Error cascade | DB/Redis dependency timeout | High | Normalized `timeout`/`dependency_unavailable`/`technical_failure` result, dependency span, no secret parameter capture. |
| 8 | Integration | Fan-in batch with multiple parent contexts | High | Use span links; do not pick an arbitrary parent silently. |
| 9 | Compliance | PAN/token/email seeded in exception/message/header | Critical | Sensitive-data tests fail if any signal contains it. |
| 10 | Business logic | Business rejection vs technical failure | High | Result classifier separates rejection from availability SLI bad event. |

Summary: Critical 3, High 7, Medium 0, Low 0; 10 scenarios across 8 dimensions.
