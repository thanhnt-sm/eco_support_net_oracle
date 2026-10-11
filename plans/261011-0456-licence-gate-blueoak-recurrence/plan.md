---
title: "Licence gate: unblock PR #49 (minimatch BlueOak-1.0.0) and stop the recurrence class"
description: "Allow BlueOak-1.0.0, pre-review the licences already waiting in the npm dev tree, fix SPDX OR semantics in the gate, keep notices truthful, ship via PR and rebase Dependabot PR #49"
status: in-progress
priority: P1
effort: 3 phases
branch: fix/licence-gate-blueoak
tags: [ci, licence-gate, npm, dependabot, tdd]
blockedBy: []
blocks: []
created: 2026-10-11
supersedes: LICENCE_GATE_PLAN.md (repo root, untracked draft)
---

# Licence gate plan

## Overview

CI run 37560442889 (PR #49, job `build-and-test`, step **NuGet/npm licence allow-list**, `.github/workflows/ci.yml:73-74`) failed:

```
licence gate: FAIL: 1 package(s) outside the allow-list:
  - npm minimatch 10.2.6: [expression] BlueOak-1.0.0
licence gate: checked 326 NuGet packages (DataGuard.sln) and 9 production npm packages (DataGuard.VSCode) against allowed-licences.txt
```

Root cause (verified): PR #49 (`origin/dependabot/npm_and_yarn/src/DataGuard.VSCode/npm-eac8022a8b`, head `11be89b`) bumps `vscode-languageclient` 9 → `10.1.2`, whose dependency spec is `minimatch ^10.2.6`. The hoisted `node_modules/minimatch` 10.2.6 (`BlueOak-1.0.0`) moves from dev-only to production; the nested ISC `minimatch 5.1.9` disappears. `scripts/allowed-licences.txt:13-23` has no `BlueOak-1.0.0`, and `spdx_allowed` (`scripts/check-nuget-licences.py:59-62`) requires an exact, case-sensitive token match.

The gate behaved as designed (fail closed on an unreviewed licence). "Recurrence" therefore means: a Dependabot bump promotes a dev-only package whose licence was never reviewed. The fix has two parts: unblock now, and review ahead of time the licences that are already sitting in the dev tree waiting to be promoted.
## Recurrence inventory (main, `src/DataGuard.VSCode/package-lock.json`, all entries, licences outside `[spdx]`)

| Licence (lockfile text) | Packages (all dev today) | Promotion risk | Disposition in this plan |
|---|---|---|---|
| `BlueOak-1.0.0` | minimatch 10.2.6, sax | **Realised** (PR #49) | Allow (Phase 1) |
| `(MIT AND Zlib)` | pako | Low (vsce only) | Allow `Zlib` — validation Q2 |
| `(MIT OR GPL-3.0-or-later)` | jszip | Low (vsce only) | Passes if Q1=Yes (Phase 2 OR parser); otherwise fails closed |
| `Artistic-2.0` | binaryextensions, editions, istextorbinary, textextensions, version-range | Low (vsce only) | Excluded by policy — validation Q3 |
| `SEE LICENSE IN LICENSE.txt` | @vscode/vsce-sign* | Low (build tooling, proprietary) | Keep fail-closed, never allow |

NuGet side: the gate already evaluates every package in `DataGuard.sln`, test-only ones included, so it has no dev/prod promotion gap.

| # | Phase | File | Status |
|---|---|---|---|
| 1 | Allow-list policy + truthful notices (unblocks PR #49) | `phase-01-allow-list-policy.md` | completed |
| 2 | SPDX expression semantics (OR/AND/WITH, fail closed) — TDD | `phase-02-spdx-expression-semantics.md` | completed |
| 3 | Verification, CHANGELOG, ship, rebase PR #49 | `phase-03-verify-and-ship.md` | in-progress |

Order: Phase 1 is independent and is the critical path for PR #49. Phase 2 touches only `spdx_allowed` and its tests. Phase 3 needs both.

## Already done (do not repeat)

- Baseline on `main` @ `cbe47e9` (PR #49 head is `11be89b`): `python3 -m unittest discover -s scripts/tests` → `Ran 92 tests … OK`; `python3 scripts/check-license-consistency.py` → `licence consistency: OK (221 documentation files scanned, 6 copies identical)`, exit 0.
- Two-step locked restore verified on macOS: `dotnet restore DataGuard.CrossPlatform.slnf --locked-mode` followed by `dotnet msbuild tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -restore -p:RestoreLockedMode=true` restores assets without lockfile drift, allowing `python3 scripts/check-nuget-licences.py` to evaluate 326 NuGet packages and 8 npm packages cleanly (`licence gate: OK`, exit 0).
- PR #49 true RED reproduction verified on real CLI: running `python3 scripts/check-nuget-licences.py --npm-lock /tmp/pr49-package-lock.json` outputs `- npm minimatch 10.2.6: [expression] BlueOak-1.0.0`, exit 1.
- Blue Oak Council Model License FAQ (`https://blueoakcouncil.org/license-faq`) verified: Council states no obstacle to combination/distribution under GPLv2/GPLv3/AGPLv3; FSF license list does not explicitly catalogue BlueOak-1.0.0 (`[chưa xác minh qua FSF, căn cứ vào phân tích của Blue Oak Council]`).
- VSIX ships production `node_modules`: `src/DataGuard.VSCode/.vscodeignore` does not exclude `node_modules/**`, and `compile` is plain `tsc` (no bundler). So the minimatch `LICENSE.md` is redistributed. This is `[INFERENCE]` until Phase 3 step V5 proves it with `vsce ls`.
## Control gates: judge and advisor

Judge = TypeSafe devices (tighten-only). Khi TypeSafe System One không kích hoạt hoặc thiếu API key (như trong phiên này), các cổng J tự động chuyển sang cơ chế **Gate 1 deterministic verification** (chạy trực tiếp lệnh CLI / test suite, kiểm tra exit code và chuỗi offender bắt buộc) kết hợp với điểm dừng phê duyệt của người dùng.

Advisor = omp advisor. Tắt mặc định (`AGENTS.md` §4.5), chỉ người dùng bật (`/advisor on`) tại các mốc A1, A2, A3 sau khi kiểm tra `/advisor status` (read-only grant), và tắt ngay sau mốc (`/advisor off`).

| Gate | When | Tool (hoặc Deterministic Fallback) | Pass condition | On fail |
|---|---|---|---|---|
| J0 | Trước khi cook bắt đầu | `xd://typesafe_elevate_plan` hoặc self-audit kiểm tra Scope/Algorithm | Không còn unverified claim, tiêu chí rõ ràng | Hoàn thiện plan |
| A1 | Cuối Phase 1, trước Phase 2 | Advisor checkpoint (người dùng gõ `/advisor on`) | Advisor xác nhận giấy phép hợp lệ và NOTICES §3 trung thực | Dừng, hỏi người dùng |
| J1 | Cuối Phase 1 | `xd://typesafe_verify_completion` hoặc Gate 1 deterministic check | CLI PR #49 lockfile exit 0, negative control exit 1, consistency script exit 0 | Sửa, chạy lại |
| J2 | Phase 2, sau GREEN | `xd://typesafe_expert_review` hoặc diff audit parser SPDX | Không có kẽ hở chấp nhận biểu thức không có vế hợp lệ | Thêm test case fail, sửa |
| A2 | Cuối Phase 2 | Advisor checkpoint (người dùng gõ `/advisor on`) | Advisor duyệt độ ưu tiên AND > OR, WITH và fail-closed | Dừng, hỏi người dùng |
| J3 | Cuối Phase 2 | `xd://typesafe_verify_completion` hoặc Gate 1 deterministic check | 92+N unit tests pass, fail-closed CLI proof exit 1 với đúng dòng offender | Sửa |
| J4 | Phase 3, trước commit | `xd://typesafe_expert_review` hoặc diff audit phạm vi V8 | Diff khớp chính xác danh sách file cho phép | Revert thay đổi ngoài scope |
| A3 | Phase 3, trước push | Advisor checkpoint (người dùng gõ `/advisor on`) | Advisor ký duyệt bảng bằng chứng V1–V8 | Dừng |
| J5 | Kết thúc Phase 3 | `xd://typesafe_verify_completion` hoặc Gate 1 deterministic check | Toàn bộ V1–V8 pass, CI job xanh | Sửa |
## Red Team Review

### Session — 2026-10-11
**Findings:** 8 total across 3 hostile reviewers (Security Adversary, Failure Mode Analyst, Assumption Destroyer).
**Dispositions:** 8 Accepted, 0 Rejected.

| # | Finding | Severity | Disposition | Applied To |
|---|---------|----------|-------------|------------|
| 1 | Admission rule admits Artistic-2.0 & contradicts MS-PL/MPL-2.0 | High | Accept | `phase-01`, `plan.md` |
| 2 | Reject `(` and `)` in id position; avoid accepting `MIT OR )` | Medium | Accept | `phase-02` |
| 3 | Bound recursion depth (max 10) in parser to fail closed on deep nesting | Medium | Accept | `phase-02` |
| 4 | Assert offender line and `licence gate: checked` in fail-closed proof | High | Accept | `phase-02` |
| 5 | Replace no-op `--solution` fallback with exact CI restore recipe | High | Accept | `phase-01` |
| 6 | Precondition: check `dotnet --version` resolves 9.0.x under global.json | Medium | Accept | `phase-01`, `phase-03` |
| 7 | Define validation Q1–Q4 and add Validation Log section | High | Accept | `plan.md`, all phases |
| 8 | Make Phase 3 expectations conditional on Q1 branch (Yes/No) | High | Accept | `phase-03` |

## Validation Log

### Session — 2026-10-11

### Verification Results
- Claims checked: 9
- Verified: 9 | Failed: 0 | Unverified: 0
- Tier: Standard (3 phases)
- Resolved notes:
  - Căn cứ tương thích GPLv3 của `BlueOak-1.0.0`: FSF chưa lập danh mục riêng; căn cứ phân tích chính thức từ Blue Oak Council FAQ (`https://blueoakcouncil.org/license-faq`) đã được người dùng phê duyệt chính thức tại Q5.

| # | Topic | Decision | Details |
|---|---|---|---|
| Q1 | SPDX OR/AND/WITH Parser (Phase 2) | **Accept (Yes)** | Triển khai Phase 2 với phương pháp TDD, xử lý đúng độ ưu tiên WITH > AND > OR, hỗ trợ dual-license (ví dụ MIT OR GPL-3.0-or-later của jszip). |
| Q2 | Bổ sung Zlib vào `[spdx]` | **Accept (Yes)** | Thêm Zlib vào allow-list cùng BlueOak-1.0.0, bao phủ pako (MIT AND Zlib) trước khi có nguy cơ promote lên prod. Không thêm vào NOTICES vì không ship trong VSIX. |
| Q3 | Chính sách với Artistic-2.0 | **Reject (Chặn)** | Tiếp tục loại trừ Artistic-2.0 (fail-closed) để kiểm soát chặt chẽ chuỗi cung ứng. |
| Q4 | Dọn dẹp LICENCE_GATE_PLAN.md | **Accept (Xóa)** | Xóa file nháp chưa track ở root; plan canonical nằm tại `plans/261011-0456-licence-gate-blueoak-recurrence/`. |
| Q5 | Phê duyệt căn cứ Blue Oak Council | **Accept (Duyệt)** | Phê duyệt tuyên bố chính thức của Blue Oak Council FAQ làm căn cứ tương thích GPLv3 cho BlueOak-1.0.0. Mở khóa an toàn cho `--auto`. |

## Cook command

Toàn bộ 5 quyết định đã được xác thực và phê duyệt (`Failed: 0`), sẵn sàng chạy cook:

```
/ck:cook --auto --tdd /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/261011-0456-licence-gate-blueoak-recurrence/plan.md
```

## Execution log

### 2026-10-11
- **Status:** Phase 1 & Phase 2 completed; Phase 3 in-progress. Working tree contains implementation on branch `fix/licence-gate-blueoak` (uncommitted).
- **Evidence summary:**
  - Unit test suite: `Ran 101 tests OK` (92 baseline + 9 new tests).
  - Real gate on `main` lockfile: OK (`licence gate: OK`, exit 0).
  - Real gate on PR #49 lockfile: OK (9 production npm packages, `licence gate: OK`, exit 0).
  - Negative control without BlueOak: exit 1 with offender `npm minimatch 10.2.6: [expression] BlueOak-1.0.0`.
  - Fail-closed proof: `MIT OR` produces exit 1.
  - Licence consistency: `python3 scripts/check-license-consistency.py` OK (6 copies identical).
  - Scripts and policy: `verify_docs_sync.sh` and `check-workflow-policy.py` exit 0.
  - V5 VSIX check: `vsce ls` lists `node_modules/minimatch/LICENSE.md`.
  - V8 diff check: tracked changes limited to the 7 allowed files.
  - Independent review & test: reviewer reports 0 critical findings; tester reports 8/8 PASS.
- **Plan deviation note:**
  - The todo item "Phase 1 RED: add test" was superseded by phase-01 "Tests first (RED)" (CLI acceptance harness, no permanent test, by design to avoid pinning config data).
- **Pending user commands (Ship steps):**
  - Commit, push, `gh pr create`, CI green on the fix PR, `@dependabot rebase` on PR #49, and recording its CI run id all require explicit user commands.
  - Advisor checkpoints A1–A3 (user-enabled only) and judge gate J2 via TypeSafe device (TypeSafe unavailable; replaced by fuzz review) remain pending/bypassed.
