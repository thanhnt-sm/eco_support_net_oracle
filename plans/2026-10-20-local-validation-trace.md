# Local validation tracing slice

Baseline: e5007e38 (test child of integrated 7dc8a6e); branch feat/local-validation-trace. Source commits/worktrees preserved; local patch only.

## Audit and decision

Core TelemetryCollector already provides opt-in Meter summaries and bounded queued local file records, with legacy explicit export compatibility. DataGuard.Observability provides ActivitySource/Meter/business wrappers and optional SDK integration for server applications. Neither supplies a numeric request-correlated EventPipe stream from the common CLI/API graph executor. VS output logging is a separate net472 surface; analyzer netstandard callbacks are a different timing boundary. Do not import the server SDK into these hosts.

Use the BCL EventSource in net9 Core, sharing the existing Telemetry namespace. No dependency/exporter/file sink added. CLI ValidationRunner and ValidationPipeline share GraphValidationExecutor; instrument direct sequential/concurrent overloads once, leaving the bool dispatcher untouched. Graph elapsed does not mean CLI extraction, baseline filtering, compiler or evaluator elapsed.

## Priorities and acceptance

- P0 privacy/result semantics: numeric-only versioned schema, process sequence IDs, no payload/SQL/path/exception strings, original errors/cancellation/results preserved, listener exceptions contained. Reproduce with sentinel contracts/violations/exception and nested requests; test numeric payload and unchanged results.
- P1 useful lifecycle: correlated begin/end, monotonic duration, finite outcome/reason codes and counts with unknown=-1. Test nested/concurrent isolation, cancellation, contained timeout and failure. No unbounded per-rule events.
- P1 resource limits: no-listener return of original task; bounded admission of 1,000 requests within 60 seconds after first admission, process lifetime, no reset on reconnect. Two events per admitted request. No internal queue/IO. Measure warmed allocation fast path and on/off same-feature overhead. Admitted ends may occur later; external collector duration caps capture.
- P2 future work: netstandard analyzer/net472 instrumentation; extraction/baseline phase correlation; typed unknown-budget reason taxonomy, cache metrics where actual caches exist; repeatable in-process capture sessions if needed. Do not invent these metrics now.

EN/VI runbooks must document opt-in/offline operation, finite schema, bounded capture, capture truncation, listener overhead and deferred surfaces. Full allowed Release build/tests/docs checks on final worktree; never retry coverage named-mutex permissions. Independent security/performance review. No push/merge/DB/Marketplace/upload.

## Implementation evidence

Implemented two version-1 numeric EventSource events in Core with per-request correlation, graph boundary, incomplete/cancel/error/timeout categories, counts and finite reason flags. No-listener/exhausted/error-level-only admission preserves direct executor task path. Metadata getters are isolated; throwingCount regression added after independent review. Eight tests cover schema, nested/concurrent pairs, cancellation/errors/contained timeout, numeric/sentinel privacy, listener disposal/failure, atomic quota/expiry and warmed zero-allocation no-listener admission.

Actual EventPipe smoke uncovered keyword-zero filtering of object-array writes in this runtime. Corrected collection recipe to provider-scoped all-keywords; made both WriteEvent payload arrays explicit so native fields match declared widths. Final native smoke verifies1,000paired events each, correct concurrent=true field, schema1 and all numeric values. This is separate from in-process EventListener tests.

Synthetic overhead with10contracts/request,2processes/mode,5batches of100requests: off/on allocated5,544/6,344bytes per request; wall median15.4845/22.177microseconds. Results include graph work and minimal listener, not IDE latency; noRSS/retained-memory claim. Raw evidence and reproducible scripts live under /workspace/.tmp/risk-maintenance/validation-trace-bench (local only).

Final production serialization passes full Release build0warnings/errors and cross-platform tests2,397passed/5Windowsjunction skips (Category!=LiveDb). The subsequent strengthened sentinel serialization assertions are rerun with the targeted suite; initial compiler/style failures are not behavioral red evidence. Wiring-red with baseline executor really fails expected[1,2] vs actual[]. Full source patch and final evidence are delivered locally; no product commit/push/coverage retry.
