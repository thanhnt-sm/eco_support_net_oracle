---
type: full-claims-evidence
date: 2026-09-13
scope: current-worktree
---

# Full claims evidence matrix

This matrix records evidence at the FC-group level. The generated
[`claim-occurrences.md`](claim-occurrences.md) inventory now supplies
line-addressable candidate seeds, but it is deliberately conservative and does
not prove exhaustive prose classification. No FC group can be closed from this
document. A local test is evidence only for the exercised behavior and platform.

| Group | Current evidence | Remaining acceptance | State |
|---|---|---|---|
| FC01 | `DataGuard.Host`, loopback readiness tests, published-host smoke | authenticated remote/TLS/proxy policy and RID artifacts | partial |
| FC02 | OSV policy/client and focused tests | provenance-origin and live network acceptance | partial |
| FC03 | Versioned scoring model, additive report envelope, and complete/partial/unknown `AssessmentEngine.RunAsync` tests | provenance-origin and live advisory acceptance | partial |
| FC04 | VS Code manifest, command registration, extension-host smoke, npm audits and VSIX packaging | supported-platform acceptance | partial |
| FC05 | Language server, bounded output and local diagnostics smoke | supported-platform and stream lifecycle acceptance | partial |
| FC06 | Visual Studio command source and docs | Windows VS SDK build/install/host proof | blocked_environment |
| FC07 | Visual Studio process/output source | Windows lifecycle and Error List host proof | blocked_environment |
| FC08 | `DataGuard.Build`, bounded offline manifest tests, clean consumer test, and explicit `dataguard preflight` producer with redacted hash-bound output | hostile-project matrix and live provider acceptance | partial |
| FC09 | DG001 now offers compile-valid `DataContract`/`SqlParameter` declarations alongside safe skip; five real providers and Fix All tests | occurrence/action matrix, automatic validation-call contract, and twelve-provider decision | partial |
| FC10 | bounded Roslyn snapshot parser plus `--ef-project`/`--ef-context` source-only CLI selection tests | generated-snapshot compatibility matrix and strict CLI matrix | partial |
| FC11 | `DataContract`/`SqlParameter`/`ResultSet` facades now extract offline entity, parameter and result descriptors; focused regression passes | `DataGuard.Validate` contract and compiler collision matrix | partial |
| FC12 | anchorless verifier fails closed and admission tests | signed identity, SPDX/in-toto binding and release proof | open |
| FC13 | pre-load manifest/digest/API checks, PE-reference closure/identity validation for declared managed dependencies, swap/symlink rejection, native fail-closed loading tests | signed provenance, one-handle proof, and release evidence | partial |
| FC14 | DPAPI/Keychain tests; Secret Service fail-closed path | live Linux Secret Service integration | partial |
| FC15 | CLI wizard, linked-worktree hook reachability, and hook symlink-preservation tests | remaining hostile-workspace and full command-contract matrix | partial |
| FC16 | deterministic YAML writer, cancellation tests, CLI selection, and YamlDotNet round-trip consumer test using the shared camelCase schema | broader downstream consumer matrix | partial |
| FC17 | static parser behavior plus live four-provider Testcontainers procedure/function extraction | supported routine-body/result matrix | partial |
| FC18 | bounded benchmark harnesses and output-equivalence gate | clean hardware baseline and owner decision on missed target | partial |
| FC19 | explicit constraints documented in red-team/audit | owner-approved bounded contracts for absolute claims | blocked_owner |
| FC20 | group ledger, 146-file classification, 539 generated mapped rows and 0 unmatched command candidates with EN/VI peers; automated absolute-claim scan reviewed against current security/architecture evidence | manual review of prose claims beyond the conservative scanner and source/test/evidence links | open |

## FC09 provider/action accounting

The shipped implementation has five concrete providers, each with non-empty
diagnostic registration and a FixAll provider:

| Provider | Diagnostic scope | Transformation boundary |
|---|---|---|
| `DataGuardCodeFixProvider` | DG001, DG002 | verified contract attributes only |
| `AddMaxLengthAttributeFixProvider` | DG007, DG009 | verified manifest length evidence |
| `SkipContractCheckFixProvider` | DG001 | explicit review-only skip annotation |
| `NamingConventionFixProvider` | naming convention diagnostic | deterministic symbol rename |
| `UseOracleCodeFixProvider` | provider option mismatch | verified provider option rewrite |

The separate historical “twelve providers” claim remains an owner decision gate;
no dummy providers or unverified transformations are counted toward it.

## FC20 prose audit boundary

On 2026-09-13, a bounded scan of current README, overview, architecture,
component, and product documents searched for absolute performance, secrecy,
dependency, provider-count, and network/DB statements. The scan found only
contextual claims already qualified by current docs (for example “no database
connection” for snapshot/manual modes and “zero vendor dependencies” for Core);
the memory-dump and universal zero-allocation wording is explicitly qualified as
best-effort/host-bound. This narrows the remaining manual review to line-level
claim-to-evidence links rather than leaving the scan unperformed.

## Verified local gate

On 2026-09-13, `dotnet build DataGuard.sln --configuration Release --no-restore`
completed with zero warnings/errors. `dotnet test DataGuard.sln --configuration
Release --no-build --logger 'console;verbosity=minimal'` passed 689 tests: Core
635, Golden 25, Analyzers 8, and CodeFixes 21. Documentation synchronization and
`git diff --check` also passed in the same worktree.

The latest required live provider profile passed 10/10 DB-backed assertions
across SQL Server, PostgreSQL, MySQL, and Oracle. VS Code `npm test`, extension-host smoke,
both npm audits, and VSIX packaging also passed; platform-specific Windows and
Linux Secret Service evidence remains open.

The latest `SKIP_ACT=1 ./scripts/verify_local_gates.sh` rerun also passed with
coverage **63.80% (9020/14138)** after integrating the workspace preflight guard
and truncation contract alignment.

This gate does not prove Windows-only behavior, signed release provenance, live
remote services, native CI workflow execution, or exhaustive document-occurrence coverage.
