# Cost and cardinality budgets

Use measured values rather than guesses:

`trace_GB_day = spans_per_second × average_span_bytes × 86400 / 1e9`

`log_GB_day = events_per_second × average_log_bytes × 86400 / 1e9`

`queue_bytes = peak_items_per_second × average_item_bytes × tolerated_outage_seconds × 1.5`

Per-service starting budgets (must be owner-approved): ≤2,000 active metric series, ≤500 stable span names, ≤100 distinct values per dimension, ≤10 diagnostic exception logs/minute/operation, bounded queue memory and profiler CPU overhead measured against baseline. Alert on budget breach and drop rather than allowing backend exhaustion. Customer/account/transaction/message/trace IDs are never dimensions.
