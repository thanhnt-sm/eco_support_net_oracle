# Branch normalization correction — 2026-10-15

## Publication follow-up — 2026-10-16

The user explicitly approved pushing maintenance branches and creating draft PRs;
merge and release remain owner decisions. The historical local-only restriction
below applied to the original batch and has now been superseded for this focused
coverage fix. No security settings, gates or Marketplace pause are changed.

Revalidated on current main `57f49750879955e2bdc29b8acf452e6ff95531eb`, which
merged PR63. Preserved patch applies cleanly in a new isolated worktree; the other
risk patches remain separate. Verified SDK 9.0.319 and Node 22.23.3. Executing the
24 current regression tests against main's old normalizer produced 25 failing
assertions; all 24 pass with the fix. Publication-run evidence is retained at
`/workspace/.tmp/risk-maintenance/coverage-publish/`; previous raw evidence and
patch artifacts remain untouched. Independent review passed with no blockers. The unchanged pre-push gate passed:
2213 .NET tests passed, five Windows-only skips, line coverage 17019/20978
(81.13%). Fresh normalized reports retain this exact line count. Python: 142
passed, one existing skip. Node: 101 passed on the quiet rerun, 50.08% lines and
78.51% branches; the concurrent run's 1567 ms performance failure is preserved
without weakening its 500 ms assertion. Build/analyzers, workflow checks,
dependency audit, formatting and docs gates passed. The default hook omits act
and verified-secret scanning; neither is claimed as separately run. Remote commit and hosted CI results must be verified
separately and are not implied by local success.

Người dùng đã cho phép đẩy nhánh bảo trì và tạo draft PR; không cho phép merge,
phát hành hoặc thay đổi bảo mật. Bản sửa được kiểm tra lại trên main mới; các bản
vá rủi ro khác vẫn tách riêng. Kết quả CI từ xa phải được xác minh độc lập.

## Scope and priority

P1 confirmed at PR63 `0439c1ca66727cbe087e05f5fabc3d64f248dfa6`:
`scripts/coverage_manifest.py` compared branch flags only with `true`, whereas
Coverlet 10.1.0 emits `True`. All five inspected normalized reports incorrectly
reported 0/0 branches. Existing line metrics are unaffected. This isolated branch
contains only the fourth patch; the prior three patches and raw evidence remain
unchanged. No commit, upload, push, PR update, or publication is authorized.
Marketplace publication stays paused.

## Source → acceptance → evidence

| Source/test | Acceptance | Result |
| --- | --- | --- |
| `scripts/coverage_manifest.py` | Case-insensitive boolean and XML 1/0; exact represented counts; reject inconsistent data | Implemented |
| `scripts/tests/test_coverage_manifest.py` | Real collector casing, multiple conditions, no-branch lines, malformed input, generated exclusions, round trip | Initial 22 tests: 20 failing assertions; corrected 24 tests pass |
| `scripts/tests/fixtures/coverlet-10.1.0-branches.xml` | Actual line-element excerpts; synthetic wrapper explicitly identified | 5/10 retained, synthetic input 7/14; method copies not doubled |
| EN/VI `docs/guides/coverage-branch-normalization*` | Raw and normalized scopes explicit; no invented branch union | Updated |
| `plans/2026-10-15-coverage-integrity.md` | Historical branch claim corrected without rewriting historical evidence | Correction added |

Independent reviewer ran 22 tests and independently checked all ten raw reports,
line unions, retained branch totals and normalization idempotence. No blocker.
Reviewer identified Coverlet percentage truncation; corrected the comment and
added actual `40.9% (9/22)` regression (24 total focused tests after additions).

## Exact metric interpretation

The pinned collector counts internal branches at root, including some branches
without emitted source lines. See [CoberturaReporter](https://github.com/coverlet-coverage/coverlet/blob/10a8051216eeaebb661624eef348ee768693fed9/src/coverlet.core/Reporters/CoberturaReporter.cs)
and [percentage truncation](https://github.com/coverlet-coverage/coverlet/blob/10a8051216eeaebb661624eef348ee768693fed9/src/coverlet.core/CoverageDetails.cs).
The normalized denominator consists of represented retained class lines. It is
not interchangeable with the raw collector denominator. Method copies cannot be
counted again. No new source exclusion or line denominator change was introduced.

| Suite report | Baseline raw branches | Baseline retained | After three patches raw | After three patches retained |
| --- | --- | --- | --- | --- |
| Core | 9426/14809 | 8885/13727 | 9518/14845 | 8977/13763 |
| Golden corpus | 1772/14533 | 1768/13451 | 1772/14569 | 1768/13487 |
| Code fixes | 438/1371 | 436/1367 | 438/1371 | 436/1367 |
| Analyzers | 724/1259 | 724/1259 | 724/1259 | 724/1259 |
| Observability | 227/362 | 227/362 | 227/362 | 227/362 |

Core baseline raw = retained + generated 189/406 + unrepresented 352/676.
Golden baseline raw = retained + generated 0/406 + unrepresented 4/676.
Code fixes raw = retained + unrepresented 2/4. No aggregate branch percentage is
claimed. Unrepresented branches are not fabricated in normalized XML.

The ten raw SHA256 hashes remain unchanged. Both inventories still contain 176
measured production source files. Line unions remain baseline **17018/20978**
(81.12%) and after three patches **17089/21006** (81.35%). The earlier normalized
0/0 branch values are invalid, not evidence of absent branches. Raw collector
counts remain valid for their original scope. TypeScript LCOV is not processed by
this script. Windows raw artifacts and hosted Codecov ingestion were not verified;
no assertion is made about their branch values or historical ingestion behavior.

## Reproduction and retained evidence

From this checkout, run `python3 -m unittest discover -s scripts/tests -p test_coverage_manifest.py`.
Run `scripts/coverage_manifest.py` with `--input` pointing to a fresh suite directory,
`--root` to its source checkout, `--output` to a new directory, and `--expect 5`.
Use existing raw files without modifying their source paths or bytes.

Local evidence: `/workspace/.tmp/risk-maintenance/coverage-fix/` contains `red.log`,
`green.log`, `invariants.json` (all input hashes and raw/retained/generated/missing
counts), `baseline-corrected-normalized/`, `three-patch-normalized/`, and gate logs.
The earlier invalid normalized outputs remain preserved in their original paths.

## Validation and limitations

Unchanged pre-push and pre-commit hooks passed. Fresh .NET: 2213 passed, five
Windows-only skips; production measured lines 17017/20978 (81.12%). This isolated
fourth-patch run does not contain the prior three source patches. Reprocessing
identical raw inputs preserves their line totals exactly; fresh execution differs
by a net one covered line from the retained baseline (seven line hit states
changed in the scanner/tokenizer; see `fresh-line-difference.json`).
Python: 142 passed, one existing skip (143 total); focused normalizer: 24 passed.
Node: 101 passed; 2127/4247 lines (50.08%), 274/349 branches (78.51%). An initial
Node run hit sandbox EROFS; the next run exceeded the existing 500 ms performance
threshold (777 ms) during .NET work. Both failures are retained; a quiet rerun
passed without assertion or threshold changes. Release build/analyzers, actionlint,
workflow policy, dependency audit, formatting, topology and docs checks passed.
Raw and normalized fresh reports are preserved under `fresh-results/` and
`fresh-normalized/`. Prior patch artifacts were rehashed and remain unchanged.
Unchanged hook execution does not publish anything. Its default SKIP_ACT=1 omits
act and verified-secret scanning; stock wording claiming separate verification
is not evidence those checks ran. No Windows runtime, VS Code editor host,
production database, hosted CI or external coverage upload is performed here.

## Tóm tắt tiếng Việt

Lỗi P1: bộ chuẩn hóa chỉ nhận `true` nhưng Coverlet xuất `True`, khiến tổng nhánh
sau chuẩn hóa sai thành 0/0. Bản vá thứ tư tách riêng, không sửa ba bản vá trước
hoặc dữ liệu gốc, không đẩy mã hay xuất bản. Kiểm thử lỗi trước/sửa sau và đánh
giá độc lập xác nhận số nhánh đúng, không đếm lặp bản sao phương thức.

Tổng nhánh gốc có thể chứa nhánh không có dòng nguồn trong XML. Tổng chuẩn hóa
chỉ phản ánh bản ghi nguồn được giữ lại; không so sánh hai tỷ lệ như cải thiện
độ bao phủ. Danh sách 176 tệp và tổng dòng 17018/20978, 17089/21006 không đổi.
Hash gốc được giữ nguyên trong `invariants.json`. Không có khẳng định độ bao phủ
100%, kết quả Windows, editor host hoặc Codecov từ xa.
