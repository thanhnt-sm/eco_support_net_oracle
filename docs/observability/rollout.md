# Rollout and lifecycle

1. Local: disabled exporter, synthetic redaction/cardinality tests and the checked-in microbenchmark.
2. Phase 6A preflight: run `./scripts/verify_observability_phase6_preflight.sh`; this is a
   non-mutating exact-artifact gate and does not satisfy the runtime canary.
3. Owner packet: validate the redacted, owner-approved contract with
   `python3 scripts/verify_observability_phase6_owner_inputs.py --file <packet.json>`; the
   checked-in template is not an approval.
4. Integration: ephemeral pinned Collector with TLS test certificates and backend outage tests.
5. Synthetic workload: compare baseline vs SDK/head sampling overhead with exporter enabled.
6. Non-critical service: one signal at a time with feature flags and a 24-hour observation window.
7. Canary: read-only operation, then critical transaction with an explicit rollback window and
   business-SLO guardrail.
8. Wider adoption: package semantic versioning, dashboard/rule compatibility period and a
   deprecation window for semantic-convention changes.

Kill switches: `Observability.Enabled`, per-signal flags and sampling ratio. The
`UseAlwaysOnHeadSamplingForTailSampling` override is separately change-controlled, requires
`TraceSamplingRatio=1.0`, and is enabled only for a capacity-reviewed gateway tail-sampling
window. Rollback removes the override first, then restores the approved sampling ratio; package
registration and deployment injection can be rolled back without changing business code.
Collector/backend upgrades require immutable image digest, config validation, schema review and
canary. Profiling has an independent deployment-level kill switch and is never enabled by
`AddCoreObservability`.
