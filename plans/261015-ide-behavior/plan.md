# Local IDE behavior maintenance / Bảo trì hành vi IDE cục bộ — 2026-10-15

Local-only authorization: no commit, push, PR, upload, publish, identity or protection changes.
Chỉ thay đổi cục bộ; không commit/push/PR/upload/phát hành/đổi danh tính hoặc bảo vệ.

Dependency: isolated branch `local/ide-behavior-tests`, worktree `/workspace/ide-behavior-tests`,
based on PR63 `0439c1ca66727cbe087e05f5fabc3d64f248dfa6`. Read-only fetch and ls-remote
confirmed PR63 head and branch; main `cb45266f8641462f0e751a8ce16f369ee0fdf85c`.
PR63 coverage infrastructure is required and remains a dependency, not merged here.
Phụ thuộc hạ tầng coverage PR63; không merge hay sửa nhánh PR63.

## Acceptance / Tiêu chí

| Requirement / Yêu cầu | Source / Mã | Tests / Kiểm thử | Evidence / Bằng chứng |
| --- | --- | --- | --- |
| Preserve multi-line ending column; convert SARIF once / Giữ cột cuối khác dòng | `src/DataGuard.VSCode/src/ui/decoration-manager.ts` | Production manager via mocked editor boundary; first line, multi-line, reversed same-line | `red-ts.log`: 1 fail; `after-ts.log`: 101 pass / Đã có đỏ-trước-xanh |
| Clear every visible editor and dispose once / Xóa mọi editor và dispose một lần | Same manager / Cùng manager | Empty replacement, explicit clear, repeated dispose, no calls after dispose | 4/4 production tests pass; mocked API / 4 test đạt, API mock |
| Keep valid diagnostics beside malformed entries; fresh empty load / Giữ chẩn đoán hợp lệ và không giữ kết quả cũ | `src/DataGuard.VisualStudio/SarifErrorListPublisher.cs` | Mixed malformed locations, multiple runs, valid-empty-valid sequence | Release build: 0 warnings/errors; Windows pending / Build đạt, chờ Windows |
| Honest coverage / Coverage trung thực | PR63 `scripts/coverage.cjs` unchanged | Same command before/after, all 16 production files | 50.08% → 52.37%, all 16 files / Giữ đủ 16 file |

Environment verified before edits: Debian 13, SDK 9.0.319/runtime 9.0.21, Node 22.23.3,
npm 10.9.9 through `/workspace/.setup/activate.sh`. Default PATH had no dotnet and Node 24.
Initial npm ci failed on unwritable default cache; retry uses `/workspace/.cache/npm`.
No local `code`, `xvfb-run`, or `mono` executable found. No production DB or user code.

Sequence: baseline TS coverage and solution build; add failing regression; minimal fix;
after coverage; VS test compile; feasible local regression checks; independent read-only review.
Trình tự: baseline; test đỏ; sửa nhỏ; coverage sau sửa; compile VS; hồi quy; review độc lập.

## Executed evidence / Bằng chứng đã chạy

All commands below use `source /workspace/.setup/activate.sh`; npm uses
`npm_config_cache=/workspace/.cache/npm`. Raw logs are retained locally at
`/workspace/.tmp/ide-behavior-evidence/` (copied from `/tmp/dg-ide-evidence/`).
Mọi kết quả là working-tree cục bộ, không phải kết quả hosted hay commit mới.

- Readiness: `dotnet --info`, `node --version`, `npm --version`; SDK/runtime above verified.
- Baseline and after: from `src/DataGuard.VSCode`, `npm run test:coverage`:
  both 101 pass, 0 fail, 0 skip, including TypeScript contract compile.
  All 16 production files retained. Lines 2,127/4,247 (50.08%) → 2,225/4,248
  (52.37%); branches 274/349 → 294/368. The added source comment increases the
  line denominator by one. V8/c8 discovers more branch/function detail for the
  newly executed module; these branch denominators are not fixed source inventories.
  Decoration manager 0/97 → 98/98 lines. No coverage infrastructure/exclusion edits.
- Red: `npm run compile && node --test out/ui/decoration-manager.test.js`:
  3 pass, 1 fail before production edit; actual ending column 19 vs expected 2
  (zero-based). Green: same four production tests pass in full coverage run and
  independent review rerun. / Đã tái hiện lỗi trước sửa; bốn test đạt sau sửa.
- `dotnet build DataGuard.sln --configuration Release`: baseline and after both
  0 warnings / 0 errors. Includes three new net472 parser tests; compile only,
  not Windows execution. They test independent SARIF loads, not Error List UI clearing.
- `dotnet format whitespace tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj --verify-no-changes --no-restore --include tests/DataGuard.VisualStudio.Tests/SarifErrorListPublisherTests.cs`:
  exit 0, workspace-loading warning emitted; not a full analyzer-format gate.
- `./scripts/verify_docs_sync.sh`, `./scripts/anti_garbage_guard.sh`,
  `./scripts/preflight_agent_check.sh`, `python3 scripts/check-workflow-policy.py`,
  `git diff --check`: pass. EN/VI VSCode guide updated with range/lifecycle semantics.
- `python3 -m unittest discover -s scripts/tests -p test_coverage_manifest.py`:
  15 pass, local-only report validation; no upload.
- `npm run test:extension-host`: blocked, no `VSCODE_EXECUTABLE_PATH` executable.
  Mock tests do not establish real editor activation/rendering/disposal.
- `SKIP_ACT=1 ./scripts/verify_local_gates.sh`: stops at missing `actionlint`.
  Full unchanged hook gate not claimed; hooks were not edited or bypassed.
- Independent read-only reviewer found no blocking issues, confirmed red assertion,
  reran 4/4 tests, checked all-file coverage and diff whitespace. Requested docs and
  evidence updates are included. / Review độc lập không thấy lỗi chặn; đã bổ sung tài liệu.

### Scope limits / Giới hạn phạm vi

No Windows runtime, VSIX/editor-host acceptance, hosted CI, LiveDB, release or upload.
Prior Windows 49.76% and C# 81.12% are historical PR63 evidence, not new measurements.
No broader activation/config/provider/process/quick-fix rewrite; those remain follow-up
coverage work. This focused batch addresses decoration locations/lifecycle and SARIF
parser isolation. No C# production fix is claimed or fabricated red/green Windows evidence.
Không chạy Windows/hosted/LiveDB/phát hành/upload; không đổi tỷ lệ lịch sử thành kết quả mới.

### Full C# local regression / Hồi quy C# cục bộ

Command: `dotnet test DataGuard.CrossPlatform.slnf --configuration Release --no-build --no-restore --filter 'Category!=LiveDb' --collect:'XPlat Code Coverage' --results-directory /tmp/dg-ide-evidence/dotnet-native-results`.
Initial sandbox attempt: 2,102 pass, 111 fail, 5 skip; CLI collector subprocesses exited
134 on read-only runtime mutex directory, with synthetic-temp fixture failures. Retained
as `dotnet-tests.log`; this is not successful regression evidence.
Supported execution permission retry outside that filesystem sandbox: **2,213 pass,
0 fail, 5 Windows-junction skips**, five coverage reports; no assertion/source changes,
no upload or live database. Log `dotnet-native-tests.log`. / Lượt chạy lại ngoài sandbox
đạt 2.213 test, không lỗi; 5 skip không được tính là pass.

C# coverage is an additional regression measurement, not a before/after claim for this
batch: no C# production changed, and Windows tests are outside this Linux suite.
C# non-live union: 17017/20978 = 81.12%

## Local verification continuation / Tiếp tục kiểm chứng cục bộ — 2026-10-15

The earlier missing-actionlint blocker is resolved. Existing tool locations were
inspected first; actionlint was absent. Installed official release **1.7.12** under
`/workspace/.tools/actionlint-1.7.12`, without editing hooks, workflows, credentials,
or the original activation script. Vendor archive:
`https://github.com/rhysd/actionlint/releases/download/v1.7.12/actionlint_1.7.12_linux_amd64.tar.gz`.
Its SHA-256 matches the vendor `actionlint_1.7.12_checksums.txt`:
`8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8`.
`actionlint --version` confirms 1.7.12. / Đã cài bản chính thức, đối chiếu checksum;
không sửa hook/workflow hoặc danh tính.

Exact unchanged gate command (supported local execution permission outside filesystem
sandbox for the previously established runtime mutex/temp-path requirement):

```sh
source /workspace/.setup/activate.sh
export PATH=/workspace/.tools/actionlint-1.7.12:$PATH
export npm_config_cache=/workspace/.cache/npm
SKIP_ACT=1 ./scripts/verify_local_gates.sh
```

The gate's selected mode performs topology/docs checks, actionlint, locked restore,
Release build/analyzers, format verification, non-live tests/coverage and read-only
NuGet audit. It contains no upload/push/publication command on this path. `SKIP_ACT=1`
is the pre-push hook's existing default, not a modified/bypassed hook. It explicitly
omits TruffleHog `--only-verified` (external credential verification) and act/Docker
workflow simulation. No release/signing/upload workflow is dispatched. The script's
literal “verified separately; hosted CI will run” message is not evidence: those
omitted checks were NOT run, and hosted CI remains unauthorized.
Chế độ cục bộ không upload/push/phát hành; bỏ qua TruffleHog xác thực qua mạng và act;
không được coi dòng thông báo mặc định là bằng chứng đã chạy các bước này.

### Real editor-host availability / Khả năng chạy editor thật

No installed `code`, `xvfb-run`, `Xvfb`, DISPLAY, WAYLAND_DISPLAY or X11 socket was
found. `/usr/bin/Xorg` is installed, but no active display is available. Official vendor metadata/download probes returned HTTP 403 (proxy CONNECT
failure on download URLs), recorded in `host-readiness.log`:

- `https://update.code.visualstudio.com/api/update/linux-x64/stable/latest`
- `https://update.code.visualstudio.com/latest/linux-x64/stable`
- `https://code.visualstudio.com/sha/download?build=stable&os=linux-x64`

No unverified third-party editor binary was substituted. Real VS Code host execution
remains pending on an environment with the official runtime and display support;
Windows Visual Studio/net472 execution also remains pending. Mock tests do not prove
actual editor behavior. / Endpoint chính thức trả 403; chưa chạy VS Code thật hoặc
Windows. Không đổi nhãn test mock thành test editor.

### Final continuation results / Kết quả cuối lượt tiếp tục

- **PASS, exit 0**: unchanged `SKIP_ACT=1 ./scripts/verify_local_gates.sh` with
  actionlint 1.7.12 on PATH. Includes all workflows linted, locked restore, build,
  analyzer build, both format commands, five non-live test projects, coverage threshold,
  and locked NuGet all-dependency audit. Both builds: 0 warnings/errors. Format commands
  returned success but emitted workspace-loading warnings; these are retained in log.
- C#: **2,213 passed, 0 failed, 5 skipped**; measured union **17,018/20,978 = 81.12%**.
  This is a distinct run from the earlier 17,017/20,978 observation, not substituted
  evidence. Five Windows junction skips remain unexecuted.
- Fresh `npm run test:coverage` after the gate cleanup: **101 passed, 0 failed,
  0 skipped**; **2,225/4,248 = 52.37%** lines, **294/368 = 79.89%** branches,
  all 16 production files retained. Source unchanged from reviewed/TDD patch.
- `git diff --check` passes. Hooks, workflow files, coverage exclusions, package locks
  and original checkout remain unchanged. No account/review setup, commits, remote
  changes, uploads, publishing, or live database tests performed.
- Kết quả gate cục bộ đạt; C# 2.213 pass/5 skip, TS 101 pass. Không chạy TruffleHog,
  act/Docker, hosted CI, Windows runtime hoặc editor host; không thay đổi từ xa.

Evidence: `full-local-gate.log`, `continued-ts-coverage.log`, `actionlint-install.log`,
`host-readiness.log`, copied gate TRX/Cobertura reports, and `tested-files.sha256` under
`/workspace/.tmp/ide-behavior-evidence/`. Updated patch `ide-behavior.patch` includes
this dated plan; source/tests remain those independently reviewed above.
