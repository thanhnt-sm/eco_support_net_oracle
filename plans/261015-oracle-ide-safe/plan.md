# Oracle IDE-safe REF CURSOR maintenance — 2026-10-15

Status: local verification complete; published as draft PR #60. Hosted outcomes are maintained on the PR for its exact head SHA. Branch: `fix/oracle-ide-safe-ref-cursor`.
Baseline: `63243ec9a09fe2ec1d657ca7eb24a080bed3aefb` (PR 59 merge, verified by fetch and GitHub connector).

## Requirement → code → test → evidence

| Requirement | Implementation | Verification / evidence |
|---|---|---|
| Suppress repository-controlled procedure execution even with retained environment credential | `IdeSafePolicy.Apply`: nested Oracle record clone clears operative `DescribeRefCursors` and legacy `UseRefCursorDescribe` | `IdeSafeOraclePolicyTests`: 128 matrix cases; baseline 96 failed, 32 passed; plus 3 null/safe cases passed |
| Keep Full/Snapshot behavior, credentials, owner and metadata options | Preserve all record properties except the two execution flags | Matrix crosses both flags, allow/present credentials, both modes and both metadata booleans; compares entire Oracle record |
| No mutation, repeat-safe application, fixed credential-free report | Clone only when either Oracle flag is set | Original identity/value, nested identity, second-application and report assertions |
| Verify actual execution boundary and trusted opt-in | No adapter changes; `ValidateCommand` passes sanitized config through `ContractAcquisition` to `OracleCatalogBuilder` | Disposable Oracle marker procedure; CLI Full/Snapshot retained-credential runs must leave marker empty; trusted catalog positive control must increment it |
| Explain behavior in EN/VI | `docs/05-operations/configuration-guide{,.vi}.md` | docs sync and independent review |

## Source findings

`ValidateCommand` applies policy only inside `if (ideSafe)` and replaces `config` before acquisition. `ContractAcquisition` forwards `config.Oracle` to `OracleCatalogBuilder.BuildAsync`. The builder constructs `RefCursorDescriber` only when `DescribeRefCursors` is true. `OracleReaders` invokes the procedure/function inside PL/SQL before describing the returned cursor. Legacy `UseRefCursorDescribe` does not control execution. Full and Snapshot modes can both reach catalog acquisition when an environment credential is retained.

Pure policy tests prove sanitized configuration properties, not absence of database execution. The live regression uses a disposable Oracle Free container and an autonomous-transaction marker to observe actual invocation; it does not establish safety for all possible database behaviors.

## Environment and commands

Environment started successfully on Debian 13 x64. Initial checkout was clean branch `work` at `341472510925e22459c1c71d0f6a470647d07a62`; fetched main without resetting/discarding work and created the focused branch.

Initial `dotnet --info` failed: command not found; no standalone csc/mcs/msbuild on PATH. Installed SDKs with:

```sh
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 9.0 --install-dir /workspace/.dotnet
bash /tmp/dotnet-install.sh --version 9.0.100 --install-dir /workspace/.dotnet --azure-feed https://builds.dotnet.microsoft.com/dotnet
```

Channel discovery retried after ci.dot.net HTTP 403; azureedge probe also returned 403. Both installations ultimately completed: SDKs 9.0.100 and 9.0.319; selected SDK 9.0.319, MSBuild 17.14.60.43110, Roslyn 4.14.0-3.26064.1 (450493a9), runtime 9.0.21. Initial SDK first-run/restore failed on read-only `/home/agent/.dotnet`; corrected with writable CLI home/package cache. No source was written before compiler/MSBuild ran successfully.

All subsequent .NET/hook commands use:

```sh
export PATH=/workspace/.dotnet:/workspace/.local/bin:$PATH
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
```

```sh
git fetch origin main
git switch -c fix/oracle-ide-safe-ref-cursor origin/main
dotnet --info
dotnet msbuild -version
dotnet /workspace/.dotnet/sdk/9.0.319/Roslyn/bincore/csc.dll -version
dotnet restore tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --locked-mode
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~IdeSafeOraclePolicyTests
docker pull gvenzl/oracle-free:23-slim-faststart
DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~IdeSafeValidate_RetainedCredentialReadsCatalogWithoutExecutingRefCursorProcedure
```

Initial policy test compile failed SA1519 (nested unbraced loops); replaced with LINQ Cartesian product. Rerun produced genuine assertion failures: 96 failed, 35 passed, zero skipped. Initial live test compile failed SA1116; call layout corrected before execution. These compile failures are not counted as red assertion evidence.

`go install` could not install actionlint because `/usr/bin/go` is a different program; downloaded official actionlint 1.7.12 release to `/workspace/.local/bin`. Docker server 28.4.0 available. Shell `gh pr view 59` and `gh api .../pulls/59` returned Forbidden; GitHub connector read succeeded and confirmed merged state. Existing `.githooks` enabled and unchanged.

## Limits and scope

No production database, real secrets, merge, release, protection changes, Vault, SQLServer or quick-fix implementation. Executed review, hooks and regression outcomes appear below; hosted CI outcomes are recorded on PR #60. Do not treat planned checks or skipped checks as passes; no completeness or 100% correctness claim.

**Tiếng Việt:** Phạm vi chỉ chặn hai cờ REF CURSOR trong policy IDE-safe; giữ metadata và credential môi trường được cho phép, không sửa cấu hình gốc. Ma trận policy đã chạy đỏ trước sửa (96 lỗi assertion, 35 đạt); không xem unit test policy là bằng chứng không thực thi DB. Test biên thực thi dùng Oracle container tạm và bảng ghi số lần gọi; không dùng DB sản xuất hoặc secret thật. Kết quả hook và review đã chạy được ghi bên dưới; kết quả CI theo SHA chính xác nằm trong mô tả PR #60.

## Executed TDD boundary evidence

The live test initially exited 2 because `--format contracts` requires `--output`; corrected the fixture to read its explicit output file. The corrected baseline run then failed on the actual security assertion: Full IDE-safe validation with the retained container credential executed the procedure, producing marker count **1 instead of 0**. This is execution evidence, not a source-only assertion. Log: `/tmp/oracle-live-red.log`.

Only after both policy and live assertion failures were observed, added the seven-line nested-clone policy fix. No Oracle adapter or acquisition behavior was changed. Green command (includes existing Oracle integration regressions):

```sh
DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~IdeSafeOraclePolicyTests|FullyQualifiedName~OracleIntegrationTests'
```

Green targeted run: **134 passed, 0 failed, 0 skipped** (131 policy cases and all 3 disposable Oracle integration tests), 41 seconds. Full and Snapshot retained-credential CLI runs left the marker count at zero, catalog output contained the fixture metadata, and the ordinary trusted opt-in positive control invoked it once. Log: `/tmp/oracle-green.log`.

Independent read-only review found no blocking issues in code, source path, matrix, live fixture or bilingual docs. Applied its optional correction to the policy class XML summary so it acknowledges the existing environment-credential exception. No concurrent review build was run.

## Broader local verification

- `dotnet build DataGuard.sln --configuration Release`: PASS, 0 warnings/errors, 40.45 seconds; includes net472 compilation, not Windows execution.
- `./scripts/verify_docs_sync.sh`: PASS.
- `python3 scripts/check-workflow-policy.py`: PASS (5 workflows).
- `python3 -m unittest discover -s scripts/tests`: 118 total, 117 passed, 1 skipped (live GitHub API).
- `for test in tests/git-tools/*.sh; do bash "$test" || exit; done`: all three suites passed using temporary local repositories.
- `git diff --check`: PASS.
- `SKIP_ACT=1 ./scripts/verify_local_gates.sh`: initial run and subsequent outcomes recorded below, with unchanged script. This is the pre-push hook's default; local act and full-history secret scan are not claimed as executed.

Logs are local `/tmp/oracle-{restore,red,live-red,green,full-build,docs,python,gates}.log`; hosted links and exact pushed SHA will be recorded on the draft PR. No generated logs or credentials are committed.

First broader gate run reached tests but failed: Core 57 failed / 1,869 passed / 5 skipped; other suites 235 passed. Failures reported read-only `/home/agent/.config/DataGuard` from existing credential/audit tests. Formatting emitted a workspace-load warning but passed. Re-ran unchanged gates with writable test-specific configuration directories:

```sh
mkdir -p /workspace/.test-config /workspace/.test-data
export XDG_CONFIG_HOME=/workspace/.test-config XDG_DATA_HOME=/workspace/.test-data
SKIP_ACT=1 ./scripts/verify_local_gates.sh
```

Rerun log: `/tmp/oracle-gates-rerun.log`. This environment correction does not modify hooks, tests or credential logic. Initial gate failure remains recorded rather than counted as a pass.

The first writable-XDG rerun stopped at restore: NuGet `NuGet-Migrations` named mutex creation encountered read-only `session12803`. Isolated `TMPDIR=/workspace/.test-tmp` (created with `mkdir -p`) and reran the same command; log `/tmp/oracle-gates-final.log`. This is an additional environment failure, not a product regression or a skipped gate.

TMPDIR isolation and `dotnet build-server shutdown` did not resolve that migration mutex error. Narrowed the environment override to **XDG_CONFIG_HOME only**, preserving the existing NuGet data location; `dotnet restore DataGuard.CrossPlatform.slnf --locked-mode --force-evaluate` then passed (`/tmp/oracle-restore-config-only.log`). Final gate retry uses PATH, DOTNET_CLI_HOME, NUGET_PACKAGES and XDG_CONFIG_HOME above, without XDG_DATA_HOME/TMPDIR overrides; log `/tmp/oracle-gates-config-only.log`.

Config-only gate retry: 2,157 passed, 4 failed, 5 skipped across the five suites. The remaining four were existing extractor path assertions: sandbox synthetic `/tmp/.git` and `/workspace/.git` markers made paths repository-relative to the wrong ancestor. No product/test workaround was added. A narrow additional-permissions attempt to create `/var/tmp/dataguard-oracle-tests` still returned read-only; automatic escalation then allowed the isolated test directory. With `TMPDIR=/var/tmp/dataguard-oracle-tests`, all **38 extractor tests passed**:

```sh
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ProjectCSharpSqlSourceExtractorTests
```

Final gates run under the same environment and approved execution context (no hook changes); log `/tmp/oracle-gates-verified.log`. Environment now uses PATH, DOTNET_CLI_HOME, NUGET_PACKAGES, XDG_CONFIG_HOME and isolated TMPDIR, with XDG_DATA_HOME unset. This separates observed harness interference from the Oracle regression.


Final unchanged local gates: **PASS**, 2,161 C# tests passed, 5 platform-specific junction tests skipped, 0 failed. Coverage union **70.80% (14,718/20,789)** for this run; not an Oracle-specific coverage figure. Locked restore, Release build, analyzers, formatting, workflow lint, topology/preflight, docs and dependency audit passed. Formatting emitted workspace-load warnings without failure. Local act/full-history TruffleHog were skipped by the hook default; the script wording “verified separately” is not evidence that they ran here.

**Tiếng Việt:** Kiểm tra cục bộ cuối đã đạt: 2.161 test C# đạt, 5 test junction phụ thuộc nền tảng bị skip, không lỗi; coverage hợp nhất 70,80% chỉ thuộc lượt chạy này. Cả ba test Oracle container tạm đã đạt riêng (kèm 131 ca policy). Review độc lập không thấy lỗi chặn. Các lỗi môi trường và lần chạy lại được ghi đầy đủ ở trên; không đổi hook hoặc test ngoài phạm vi để né kiểm tra. Đã mở draft PR #60; kết quả CI theo SHA chính xác được cập nhật trong mô tả PR; chưa merge.


## Publication evidence

[Draft PR #60](https://github.com/thanhnt-sm/eco_support_net_oracle/pull/60) publishes code commit `b2da9829c9a47f265f9aa8034acd0d4df31a7bff` on base `63243ec9a09fe2ec1d657ca7eb24a080bed3aefb`.

```sh
git add src/DataGuard.Cli/IdeSafePolicy.cs tests/DataGuard.Core.Tests/IdeSafeOraclePolicyTests.cs tests/DataGuard.Core.Tests/OracleIntegrationTests.cs docs/05-operations/configuration-guide.md docs/05-operations/configuration-guide.vi.md plans/261015-oracle-ide-safe/plan.md
git commit -m 'fix(cli): suppress Oracle REF CURSOR execution in IDE-safe mode'
git push -u origin fix/oracle-ide-safe-ref-cursor
```

Unchanged pre-commit, commit-msg and pre-push hooks all passed. Exact code-head pre-push: **2,161 passed, 5 skipped, 0 failed; 70.79% (14,716/20,789)** coverage union. This differs from the earlier local gate measurement (70.80%) and is reported separately. Logs: `/tmp/oracle-commit.log`, `/tmp/oracle-push.log`. Working tree was clean after publication. Draft creation used the authorized GitHub connector because shell API requests were blocked.

This documentation-only follow-up records the known publication state. The PR description is the canonical post-push evidence ledger for exact head SHA, workflow/run/job links, terminal conclusions, failures/skips and remaining limitations; this avoids a self-referential sequence of evidence commits. CI is not claimed successful by this plan. User retains merge decision.

**Tiếng Việt:** Commit mã nêu trên đã qua đủ hook nguyên trạng và được push lên PR #60 dạng draft. Lượt pre-push riêng đạt 2.161 test, 5 skip, coverage 70,79%. Mô tả PR lưu kết quả CI cuối cùng theo đúng SHA và các giới hạn; kế hoạch này không tự khẳng định CI thành công. Quyết định merge vẫn thuộc người dùng.
