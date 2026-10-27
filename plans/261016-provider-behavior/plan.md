# Provider behavior / Hành vi provider — 2026-10-16

Local-only batch; no commit, push, PR, CI dispatch, upload, identity/protection change.
Original highlight worktree and patch remain preserved at `/workspace/ide-behavior-tests`
and `/workspace/.tmp/ide-provider-evidence/prior-highlight.patch` (SHA-256
612c796a3f72c6765195d2c93eeb2dbf69744c2924a39e04a8a609c24a3d1822).
New worktree `/workspace/ide-provider-tests`, branch `local/ide-provider-tests`, based
on PR63 0439c1c plus that patch. Read-only fetch confirms main
57f49750879955e2bdc29b8acf452e6ff95531eb, tree identical to 0439c1c; PR63 is now merged
externally (parent reports 2026-10-16 01:27:09 UTC). No history rewrite needed.
Chỉ cục bộ; giữ patch trước; main có cùng tree với base PR63.

Toolchain verified before edits: .NET 9.0.319, Node 22.23.3; actionlint 1.7.12 retained.
Baseline `npm run test:coverage`: 101 pass, 2225/4248 lines = 52.37%, all 16 production
modules. Use unchanged c8 inventory before/after; no exclusions. No normalized .NET
branch totals relied on: known normalization defect is a separate local task.

## Gaps → acceptance / Khoảng trống → nghiệm thu

| Module / Mã | Baseline covered/lines | Behavior tests / Hành vi | Target / Mục tiêu |
| --- | --- | --- | --- |
| extension.ts | 0/964 | Future activation, disposal, process failure/cancel and config changes | Keep visible; real host pending |
| dashboard-panel.ts | 0/222 | Future panel lifecycle/message validation | Keep visible |
| sql-queries-tree-provider.ts | 0/238 | Future scan/config/provider replacement | Keep visible |
| codelens-provider.ts | 0/111 | Exact file matching, sort/group, cancellation, dirty doc, cap, clear/event | ≥90% module lines |
| hover-provider.ts | 0/101 | Exact file/range, overlapping findings, migration, safe markdown, clear | ≥90% module lines |
| findings-tree-provider.ts | 0/199 | Filter/group/count, navigation coordinates, independent replacement/clear | ≥90% module lines |
| quick-fix-provider.ts | 0/126 | Rule dispatch, diagnostic code forms, cancellation; never invent schema edits | ≥90% module lines |
| decoration-manager.ts | 98/98 | Prior production lifecycle/range tests retained | Preserve |
| VS SolutionTrustGate | Windows historical only | Config change/delete invalidates current consent, live consent distinct | Compile locally; Windows execution pending |

Batch total target >60% lines with same 16-file scope (source edits may alter line count).
>90% overall is NOT promised by this batch: extension.ts alone exceeds the remaining
10% allowance. Remaining modules and real editor/Windows acceptance require subsequent
work. New tests invoke production providers through explicit recording API mocks;
no copied-helper tests or claims of editor runtime. TDD before minimal defect fixes.
Mục tiêu batch >60%; không hứa >90% tổng thể; mock API không phải editor thật.

README quality links describe different measures: Codecov line/branch coverage,
OpenSSF Scorecard security-practice score out of 10, Best Practices badge criteria,
CI status, static SDK/license badges. They cannot be averaged or relabelled a common
quality score. Read-only official Scorecard API and Best Practices project JSON returned
403; current >9/10 attainment unverified. Exact Codecov 6% flag still unverified (403
reported earlier). No account/workflow settings changes to chase scores.
Không đánh đồng coverage với Scorecard hoặc badge; không xác minh được số hiện tại.

Sequence: baseline; failing production behavior tests; minimal fixes; same-scope
coverage; VS compile; independent review; unchanged local gate with SKIP_ACT=1;
EN/VI documentation and patch hashes. Evidence in `/workspace/.tmp/ide-provider-evidence`.

## Implementation and evidence / Thực thi và bằng chứng

| Requirement / Yêu cầu | Production source | Regression tests / Kiểm thử | Evidence |
| --- | --- | --- | --- |
| Exact file identity, cancellation and grouping | `src/DataGuard.VSCode/src/ui/codelens-provider.ts` | `providers-behavior.test.ts`: two roots sharing suffix, dirty doc, cancellation during work, 50/51 cap, events and clear | Red cross-file failure → green; 111/111 lines |
| Correct locations and text-safe hover | `hover-provider.ts`, `redaction.ts` | Multi-line boundary, parser → hover/tree, overlapping diagnostics, dynamic Markdown payload, migration/code context | Red range/path/text failures; review regression red → green; hover 105/105 |
| Distinct files and stable provider state | `findings-tree-provider.ts` | Duplicate basenames, filters by rule/message/path/severity, all group modes, counts/navigation, defensive copies, replace/clear | Red duplicate-group failure → green; 198/198 lines |
| Honest manual remediation, no fabricated edits | `quick-fix-provider.ts`, `redaction.ts` | Structured codes, unknown rules, cancellation, no editor open/workspace mutation/false success, shared labels | Red automatic-edit and code-form failures → green; 98/103 lines |
| Config consent lifecycle | `src/DataGuard.VisualStudio/SolutionTrustGate.cs` unchanged | `tests/DataGuard.VisualStudio.Tests/SolutionTrustGateTests.cs`: disk config changes/deletion, separate live consent, revocation | Full solution build passes; Windows execution pending |

Test source: `src/DataGuard.VSCode/src/ui/providers-behavior.test.ts` (16 production
behavior tests). Standard `npm test` now explicitly includes it, matching coverage
discovery. Existing redaction title expectation updated to the new manual-review
contract; the interim failure is retained in `npm-test-old-title-failure.log`.
No new copied-helper tests. API doubles record commands, ranges, edits, events and
Markdown text/code calls; they do not verify actual VS Code rendering.

- Baseline command: `source /workspace/.setup/activate.sh; npm run test:coverage`
  from extension directory: 101 pass, 0 fail/skip, 2225/4248 lines (52.37%).
- First TDD: `npm run compile && node --test out/ui/providers-behavior.test.js`:
  **7 pass / 7 fail** before production changes (`red.log`).
- Independent review found parser integration, stale shared titles, and standard-test
  discovery gaps. Added two integration regressions first: **14 pass / 2 fail**
  (`review-red.log`), fixed parser/titles and test command. Review rerun: **16/16 pass**;
  no remaining blockers. Cancellation getter tests are implementation-sensitive;
  actual cancellation and editor timing remain host acceptance work.
- `npm test` and `npm run test:coverage`: **117 pass, 0 fail, 0 skip**.
  Same 16-file inventory: **2737/4228 lines = 64.73%**. Raw c8 TypeScript branches
  **430/500 = 86.00%** (not normalized .NET branches). Do not sum these across suites.
- Covered line count increases by **512**. Source denominator falls by **20**:
  unsafe placeholder remediation removed (-23 quick-fix lines), dynamic hover text
  handling adds 4, unused path import removed (-1 tree line). No production file,
  difficult branch, or coverage exclusion was removed to improve the score. The
  targeted modules report 100% lines for CodeLens/hover/tree and 95.14% for quick-fix through behavior assertions; this is not
  100% editor quality or a claim that every runtime scenario was tested.
- `dotnet build DataGuard.sln --configuration Release`: PASS, 0 warnings/errors,
  includes new Windows net472 test compilation. No Windows TDD execution claimed.
- Local gate command: source activation, add `/workspace/.tools/actionlint-1.7.12`
  to PATH, then `SKIP_ACT=1 ./scripts/verify_local_gates.sh`. Uses supported local
  execution permission for known mutex/temp-path sandbox requirements. No hook edits.
  TruffleHog external verification and act/Docker are omitted by this existing mode;
  script wording about separately verified checks/hosted CI is not execution evidence.

**VI:** 16 test mới gọi module production; đỏ trước xanh được ghi log. Tổng 117 test
TS đạt; coverage 64,73% trên đủ 16 file. Tổng dòng giảm 20 do bỏ sửa tự động đoán mò
và thay đổi hợp lệ được nêu rõ; không loại file khó. Test VS chỉ compile, chưa chạy Windows.

## Remaining path beyond 90% / Đường tiếp tục vượt 90%

Current unexecuted modules: extension 964 lines, dashboard-panel 222, SQL tree 238
(total 1424). Other modules retain 67 uncovered lines. With the current denominator,
strictly >90% needs at least 3806/4228 hits: **1069 additional covered lines**.
Do not turn this arithmetic into a promise or add tests merely to execute statements.

1. Exercise activation/command registration/disposal, output/diagnostic clearing,
   trusted-workspace/config/provider decisions and cancelled/failed CLI runs in the
   actual extension module with controlled services and synthetic process fixtures.
2. Exercise dashboard create/reveal/dispose and malformed/stale IPC actions; SQL tree
   report replacement/filtering/provider-specific commands and error states.
3. Cover meaningful remaining security/coordinator branches, then remeasure the full
   unchanged file inventory. Extend synthetic end-to-end CLI/LSP scenarios as needed.
4. Run real editor-host acceptance when official VS Code/runtime/display are available;
   previous official downloads returned 403 and were not retried via alternate routes.
   Windows VS diagnostic/config/UI runtime and coverage require Windows. Mock-based
   numeric coverage can improve without proving those platform behaviors.

Scorecard >9/10 is separate: parent audit reports Branch-Protection 8, Code-Review 0
(0/8), Maintained 0 (<90 days) at main57f4975/job116762627142. These are parent-supplied
observations, not a locally fetched aggregate. Human collaboration/history, time and
owner-approved policy changes cannot be achieved by local tests or fabricated approvals.
The separate `scorecard-docs.patch` corrects Passing/Silver/Gold to 5/7/10 using official
Scorecard evaluator/checker source, labels 7.9 at3656e0e/2026-10-05 historical and rejects
multi-account/AI approval gaming. No external badge answers, identities or settings changed.

**VI:** Còn thiếu ít nhất 1069 dòng được kiểm thử có ý nghĩa để vượt 90% theo tổng hiện
tại. Cần activation/process/config, dashboard/SQL tree và nghiệm thu editor/Windows.
Điểm >9/10 cần điều kiện độc lập về quy trình/người/thời gian; không thể giả lập bằng test.


## Final local gates / Gate cục bộ cuối

Unchanged `SKIP_ACT=1 ./scripts/verify_local_gates.sh`: **PASS (exit 0)**.
Topology/docs/actionlint, locked restore, Release/analyzer builds, format verification,
five non-live C# suites, 60% line gate and all-dependency NuGet audit passed. C#:
**2213 pass, 0 fail, 5 Windows junction skips**, union lines **17018/20978 = 81.12%**.
Workspace-loading warnings were emitted by format; build reports 0 warnings/errors.
No normalized C# branch claim. Original highlight patch/worktree remain unchanged.

Final TS run after gate cleanup: both standard and coverage commands **117/117 pass**.
Removed two non-asserting `registerFindings` calls during final test-quality review;
that unused registration method remains uncovered rather than padding the result.
Final **64.73%** supersedes preliminary 64.85% in `after.log`; use
`final-coverage.log`/`final-summary.json`. No production changes after independent
review. / Đã bỏ lời gọi không có assertion; không nâng coverage chỉ bằng thực thi dòng.

Final artifacts: incremental `providers-only.patch` (apply after preserved highlight
patch), cumulative `extensions-cumulative.patch` (includes highlight + providers,
apply to tree at main57f4975), and separate `scorecard-docs.patch`. Never apply both
incremental and cumulative patches. `tested-files.sha256` records tested source/doc
files; `patches.sha256` records deliverables. All stored only under
`/workspace/.tmp/ide-provider-evidence/`; no upload or publication.

## Authorized draft publication / Xuất bản draft được phép — 2026-10-16

The user subsequently explicitly approved pushing maintenance branches and creating
draft PRs (parent supplied confirmation). Earlier local-only statements above describe
the completed preparation phase, not the current authorization. No merge, release,
account/credential or protection changes are authorized.
Người dùng đã xác nhận push nhánh bảo trì và tạo draft PR; không merge/phát hành/đổi tài khoản.

Publication worktree `/workspace/ide-maintenance-publish`, branch
`fix/ide-provider-behavior`, starts at freshly fetched main
`57f49750879955e2bdc29b8acf452e6ff95531eb`. Its tree matches the former PR63 base.
Applied the preserved cumulative extension patch; retained all earlier worktrees/patches.
The separate Scorecard documentation correction and branch-normalizer fix are excluded
from this PR. No edits to hooks, workflows, coverage scope or locks.

Re-run standard TS tests, all-file coverage, full solution build, unchanged repository
gates and independent review before publication. Windows/runtime CI outcomes will be
reported for the exact pushed SHA; local compilation alone is not Windows execution.

Fresh publication-worktree verification passed: standard TS and all-file coverage
117/117, 64.73% lines; full solution Release build 0 warnings/errors; unchanged
`SKIP_ACT=1` local gate passed with 2213 C# passes and 5 junction skips; this fresh
run measured 17013/20978 = 81.10% lines (distinct from earlier 81.12% runs). Independent
prepublication review verified byte-identical tested source, identical base tree,
no unrelated edits, and no blockers. Earlier local-only patches remain preserved.
These are prepublication working-tree results; remote SHA/CI status are reported
separately rather than relabelling them as completed hosted runs.
Đã kiểm chứng lại trước push; không đổi nhãn kết quả cục bộ thành kết quả hosted.
