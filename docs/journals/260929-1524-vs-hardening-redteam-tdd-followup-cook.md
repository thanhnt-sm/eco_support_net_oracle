# Cook of the red-team TDD follow-up: four phases green, one "closed" finding that was not

**Date**: 2026-09-29 15:24
**Severity**: Medium
**Component**: DataGuard CLI (`src/DataGuard.Cli`), VS extension, VS Code extension, CI/release workflows
**Status**: Resolved on branch; owner items open

**Plan:** `plans/260929-0952-vs-hardening-redteam-tdd-followup/plan.md` · **Mode:** `/ck:cook --tdd`, four Fable executors in parallel, then tester → code-reviewer → review-fix executors → post-review gate.
**Pushed to `origin/feat/vs-extension-hardening`:** `cce75a0` (CLI), `3cf1a27` (VS Code), `f228d16` (VS), `c37a2ce` (CI/docs), `56cfec8` (review fixes), `6d3eb67` (sink fix). PR #24 body updated. Nine `packages.lock.json` files differ by CRLF only and were not committed.

## What Happened

The routine part worked as the plan promised. TDD evidence per phase, quoted from the executor reports:

| Phase | RED | GREEN |
|---|---|---|
| 1 CLI handshake / `--allow-env-connection` / regex bounds | 17 failed + 1 regex-DoS test killed at 121 s = 18 | Core 871 / 0 |
| 2 VS run lifecycle / handshake gate / consent-by-`.sln` | 27 | VS 152 / 0 / 1 skip (was 95) |
| 3 VS Code parity / credential carve-out / old-CLI detection | 9 | VS Code 93 (was 76) |
| 4 workflows + docs | policy script 4 assertions FAIL | `check-workflow-policy: OK` |
| Review fixes (`56cfec8`) | 11 (Core), 9 compile errors + 1 flipped row (VS), 1 + TS2305 (VS Code) | Core 892, VS 155, VS Code 95 |
| Sink fix (`6d3eb67`) | 4 + 1 (`verify-shape`) | Core 898 |

Reviewer: 7/10, 0 Critical, 1 High, 5 Medium, 9 Low. First gate (`tester-260929-1412-followup-e2e.md`): every suite green, VSIX 50 481 287 bytes, `assert-vsix: OK`, scenarios (a)–(f) PASS. Docs gap found and closed on the way: USAGE/README/SECURITY now told users to download the CLI from GitHub Releases, but `release.yml` published no CLI binary at all — the `cli-package` job (win-x64/linux-x64/osx-arm64 zip + `.sha256`, attached and attested) exists because of that contradiction, not because anyone planned it.

## The Brutal Truth

1. **`56cfec8` says "close review findings". It did not close M3.** The review-fix executor hardened `SafeWritablePath` (ancestor walk bounded to the workspace, five unit tests) and wrote a docstring calling it the "write-path policy shared by every CLI file sink". `validate --format sarif` never called it. The post-review gate's new scenario (g) — the first time anyone pointed `validate --output` at an in-workspace junction — got exit 0 and `outside-target/x.sarif` on disk, four runs out of four. 892 green tests said nothing about that path because no test had ever exercised it. The tester found it; the executor, the reviewer and I all signed off on a claim nobody had measured.
2. **H1 was a decision I made in the plan.** F1 accepted "keep the user's SecretStorage credential under `--ide-safe`" and looked only at where the credential came from. It did not ask what `validate` does with a credential: `ProviderRuleCatalog.cs:98-109` registered `LiveSqlShapeValidationRule(connectionString, …)`, so every repo-extracted SQL literal would be handed to `sp_describe_first_result_set` on the user's database, with no confirmation, from a "Run Validation" click. The reviewer had to spell it out.
3. **Two executors died mid-run to an API rate limit** (reset 13:30 Asia/Bangkok). Phase 2 and 3 resumed with context intact about 2.5 h later — phase report stamps `1103` → `1341` are the hole. Nothing was lost; a lot of clock was.
4. **Our own commit gates blocked the Phase 3 commit twice.** First the commit-gate secret scan on synthetic `postgres://user:<digit-bearing password>@…` test fixtures. Then the repo pre-commit `dotnet format whitespace --verify-no-changes`, which runs over the whole tree, tripped on CRLF that another executor's Python patch script had introduced in files it was still editing. Frustrating, and both were self-inflicted.

## Technical Details

- Scenario (g) control: `assess --ide-safe --output <ws>/junction/a.sarif` → exit 4, `Assessment failed: Refusing to write SARIF through a symbolic link or invalid path.` Same junction, `validate` → exit 0. Root cause read, not guessed: `Program.cs:387` `emitter.AddSarifSink(new FileSarifSink(output!))` → `ContractExportWriter.WriteAtomicallyAsync` (plain `Directory.CreateDirectory` + `File.Move`); only the sidecar `summary.json` went through the guarded writer, and its rejection was swallowed by `catch (Exception) { /* Non-fatal */ }` at `:409`. Same hole for `--format evidence` (`ContractEvidenceWriter`) and `oracle-check --output`.
- H1 fix, observed stderr with a kept but unreachable credential: `ide-safe: suppressed ManualAssemblyPath (assembly loading disabled); live SQL shape rule disabled (use verify-shape); …` then `ide-safe: kept environment connection (--allow-env-connection)`; `from-config`/`CONFIGSECRET` never echoed.

## What We Tried

- **H1**: reviewer offered (a) connection-less shape rule under ide-safe, (b) a VS Code modal for validate-with-credential, (c) accept and document. Chose (a): the credential serves catalog reads only; live shape checking stays behind `verify-shape` and its modal. Rejected (b) because VS has no modal on the build-triggered path and a prompt on every "Run Validation" is exactly the fatigue that makes people click through. Docs (M5) rewritten across SECURITY en/vi, USAGE, both CHANGELOGs, VS Code README.
- **M3, second attempt**: `RefuseUnsafeOutput` at the process level in `validate`, `oracle-check` and `verify-shape`, before any acquisition or connection, exit 4 (assess's code), `summary.json` refusal now logged instead of swallowed. Rejected lifting the guard into Core: it would mean threading the workspace root through `FileSarifSink`/`ContractEvidenceWriter` constructors for the same three call sites. Proven with `[WindowsTheory]` e2e tests that spawn the built CLI, not with more helper unit tests.
- **Secret scan**: digit-free fixture passwords instead of an allow-marker — same conclusion as the jwt.io fixture in the 08:54 journal.
- **CRLF**: deferred the commit until the other executor normalised its files rather than running the formatter over files I did not own.

## Root Cause Analysis

M3's "fix" was verified at the unit of the helper while the finding was about a write path. Nobody enumerated the sinks. The commit message and the docstring asserted a scope that no test checked, and a green suite was mistaken for evidence. H1 came from the same shape of error one level up: a plan decision about a credential that never listed the operations the command performs with it. The rate limit is shared subagent quota with no mitigation beyond resume. The commit-gate collisions are the cost of a whole-tree pre-commit check plus parallel agents editing the same tree.

## Lessons Learned

- A fix for a write-path or credential-path finding is closed only when every sink or consumer is tested at the process level. Grep the call sites, list them in the report, spawn the binary.
- A commit message that says "closes X" is an assertion. The gate after it must include X's own scenario, not just the suites.
- Decisions about accepting a credential must enumerate what the command does with it — not just who supplied it.
- Synthetic secrets in fixtures: digit-free or runtime-built, never allow-marked.
- With a whole-tree pre-commit formatter and concurrent executors, the committer waits for the last editor; do not format files you do not own.

## Next Steps

Owner: thanhnt-sm unless noted.

- `Process.Start` inside `CliProcessRegistry.StartAndRegister`'s lock (M4 second half; needs a Cancel-during-start test).
- DG1291 literal-cap note goes to stdout only, invisible in VS — emit as a progress event.
- Core `snapshot`/`baseline` writers not audited for reparse points.
- `IdeSafeEndToEndTests.cs` (442 lines) and `check-workflow-policy.py` (292) exceed 200 lines — shared process helper / fixture module.
- VSIX Authenticode signing decision; reserve `DataGuard.Cli` on nuget.org.
- First `v0.3.0` tag is the first real run of `cli-package` and the reordered `visual-studio-package`; watch it, and check the osx-arm64 apphost against Gatekeeper.
- Re-run the two rate-limited red-team lenses (supply chain, Core/adapters) after 2026-10-01 07:00 Asia/Bangkok.

## Addendum (evening) — CI red, `/simplify`, CI green

The first push (`8a2df13`) failed two CI jobs and CodeQL. The debugger's finding overturned an assumption baked into the PR text: the "Visual Studio VSIX Packaging Gate" had never been green on this branch, and `main` has carried the same bug since `9f82f3f`. `BuildAnalyzers` in the VSIX csproj ran `Restore;Build` in a single MSBuild task, so on a clean checkout the Analyzers project compiled from a pre-restore evaluation with no netstandard reference assemblies (CS0518). Locally it passed only because `obj/` was warm. Fix: separate Restore and Build calls with a unique `MSBuildRestoreSessionId` (`ce20e3a`), reproduced red-to-green from a `git archive` copy with the exact CI command. Lesson: "watch the gate on this PR" is not evidence; read the gate's history before claiming it passed.

CodeQL's 12 alerts came from the repo's custom `dataguard/*` queries firing on synthetic test fixtures. Rewriting fixtures to the existing two-segment convention cleared them; the fixture agent also noted the queries are anchored full-string matches, so real credentialed strings with `;` never fire while credential-free `"Server=env"` does — a query-quality item for the owner.

`/simplify` ran four lenses (reuse, simplification, efficiency, altitude) and 20 findings were applied in `27283d9` with output proven byte-identical by e2e diff. Skipped on purpose: waiting for the target after taskkill (behaviour), gating Core snapshot/baseline writers (owner call), a parallel release test job (plan pins the order), and dropping `fetch-depth: 0` (MinVer still needs history). CI on `27283d9`: all jobs green, first time for the packaging gate.
