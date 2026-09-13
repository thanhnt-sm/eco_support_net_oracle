# Expanded scope red-team — full documented capability delivery

Date: 2026-09-12. User explicitly selected implementation of missing current-doc capabilities. This review is separate from the historical seven-phase GO. Reviewers are read-only GPT-5.6 Sol; Terra implementation has not started in this planning turn.

## Accepted obligations

All rows are accepted plan amendments; execution state remains **open**. A mapped FC group/parent cannot close before these obligations have implementation/test evidence. These rows supplement, not replace, the original15 RT children.

| ID | Severity | Evidence at review | Required correction / phase | FC mapping | Execution state |
|---|---|---|---|---|---|
| XR01 | High | Phase10 privacy requirement versus `src/DataGuard.Core/Assessment/Internal/DependencyHealthPack.cs:29` and lock files lacking trusted source identity | Unknown-origin non-egress; operator-owned public coordinate allowlist/provenance, exact-body tests; Phase10 | FC02, FC03 | open |
| XR02 | High | Phase9 remote-auth placeholder; `Dockerfile:53` has only CLI runtime | JWT issuer/audience/lifetime/scope, TLS/trusted proxy/rate bounds and negative auth tests; Phase9 | FC01 | open |
| XR03 | High | Phase13 path-based admission; `src/DataGuard.Core/Plugins/RulePluginManager.cs:72` loads mutable paths | Independent offline trust roots, verified exact bytes and dependency closure, substitution/race tests; Phase13 | FC12, FC13 | open |
| XR04 | High | Phase11 originally created classifier after Phase12 consumer; private classifier `src/DataGuard.Analyzers/Analyzers.cs:239` | Phase12 owns shared classifier/golden contract/generator migration; Phase11 depends on and consumes it | FC05, FC08 | open |
| XR05 | High | Phase12 success allowed non-delivery; `src/DataGuard.CodeFixes/CodeFixProviders.cs:31` advertises unimplemented IDs; comment placeholder at296 | Every required fix needs real transformation; owner-blocked cannot satisfy phase/group success; Phase12 | FC09 | open |
| XR06 | High | FC18 required cache but Phase14 had benchmark-only steps; unused cache fields `src/DataGuard.Core/Baseline/BaselineManager.cs:25` | Implement bounded versioned memory/file cache,1h TTL, atomicity/corruption/live bypass tests before benchmark; Phase14 | FC18 | open |
| XR07 | High | Original plan review/validation paragraphs described seven-phase GO next to15-phase DAG | Label historical GO scope; separate expanded review/validation and mandatory CP8 census | FC20 | open |
| XR08 | Critical | Phase12 project-controlled MSBuild property enabled live validation | Network/credential-free MSBuild consumer; independently operator-launched live CLI preflight; hostile-project tests, no sandbox claim; Phase12 | FC08 | open |
| XR09 | Critical | New Host/LSP/Build projects absent from current distribution matrix; `.github/workflows/build_release.yml:19,99,188` | CP8 artifact/TFM/RID/package matrix; feature-phase packaging; Phase15 clean install-from-produced-artifact tests | FC01, FC05, FC08 | open |
| XR10 | High | Phase9 used Phase13 policy but omitted dependency/integration owner | Phase9 depends13 and owns verifier→health adapter; joint Unknown/Verified/Failed/Stale tests | FC01, FC12 | open |

Four reviewers produced12 raw findings, consolidated into10 obligations (2 Critical,8 High); XR05 and XR08 each had duplicate reviewer reports. All10 accepted; none rejected. Final rechecks recorded below.

## Closure protocol

Final rechecks: **GO at design level** from security, assumptions, scope and Sol advisor after all amendments. Assumptions recheck caught remaining classifier-ownership wording; scope recheck caught stale live-MSBuild wording; both were corrected and independently rechecked. No reviewer GO closes an execution row. CP8 census/runtime/artifact/API/owner decisions and Phase15 delivery evidence remain mandatory.

Track `closed + blocked + open = 10` for XR obligations independently of66 parents,15 RT children,20 FC groups and N occurrences. Extend explicitly if new accepted findings are added. Design-level acceptance is not execution closure. CP8 must enumerate all actual current claims, settle feasible bounded contracts and retain unresolved owner/environment gates. Phase15 final GO requires every required XR obligation and claim verified.
