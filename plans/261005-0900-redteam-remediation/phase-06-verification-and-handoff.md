# Phase 6: Verification, ADRs, journal, PR

## 6.1 ADRs for out-of-scope components (rec 22)
`docs/adr/ADR-00xx-<component>.md` for: Visual Studio extension, VS Code extension + LanguageServer, Observability*, Host/Health, Assessment (OSV), Telemetry HTTP export, Plugins, AutoDetection, MySql/PostgreSql adapters. Each ADR: what the original goal said (file + quote), what exists (LOC, tests, CI), risk it adds, recommendation `keep | freeze | extract`, and the owner decision placeholder. No deletion in this plan.

## 6.2 Owner decisions recorded as Deferred
- GPL §7 additional-permission text and commercial terms legal review (report §2.3 R35).
- VSIX Authenticode signing key and NuGet author-signing certificate (rec 41 note).
- Reserve `DataGuard.Cli` on nuget.org (pre-existing).
- `environment: release` reviewers on GitHub (Phase 2 adds the YAML; protection rules are a repo setting).

## 6.3 Final verification
1. Full build with analyzers, format check, full test suite (unit + LiveDb locally), coverage ≥ 60 % (report the number).
2. e2e: `dataguard validate` in snapshot mode on `samples/DataGuard.Sample` for `sqlserver`, `oracle`, `mysql`, `postgresql` ⇒ exit 0 with no DG015 on valid tables; a seeded phantom ⇒ exit 1; a seeded SP arity mismatch ⇒ DG101.
3. `python3 scripts/check-workflow-policy.py`, `scripts/tests`, `verify_docs_sync.sh`, `check-license-consistency.py`.
4. Mutation spot checks from the report re-run by hand (document the three).
5. `BinaryCompatibilityFixture` builds against the new Core.

## 6.4 Handoff
- `docs/journals/261005-redteam-remediation.md` with per-phase evidence (commands + counts).
- `plans/ACTIVE_SESSION_REGISTER.md` new entry: what shipped, what is Deferred, next steps.
- Update this plan's phase table with commit SHAs.
- Push branch; PR only on explicit request.
