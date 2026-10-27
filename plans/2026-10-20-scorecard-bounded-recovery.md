# Scorecard bounded recovery / Phục hồi Scorecard có giới hạn

> Historical branch evidence / Bằng chứng của nhánh nguồn: test totals, ancestry and performance below describe the original slice, not the combined build. See [local integration baseline](2026-10-20-local-integration-baseline.md) for the integrated scope; do not add these test totals. Các số đo dưới đây không phải benchmark của HEAD tích hợp.


Baseline: main `39a030e1a99b75e92d8bf68f8bdc62428ac5923e`; separate branch `fix/scorecard-bounded-recovery`. Runtime output testing branches 94d58d9 and 2f6e315 are preserved and excluded.

Evidence: run 39420559904 / job 118622417175, 2026-10-20 12:18:38Z GitHub GraphQL internal error before report generation. Pinned upstream action `fa7117db83c52d787ae5a89f350999701505d78e/main.go` runs Scorecard before formatting and publishes after reports exist. README workflow restrictions permit only approved action families in the publishing job; no shell retry or job env/defaults. This proves a failed upstream request, not a general outage.

P1: one native retry, original evidence retained, clean same-SHA checkout before retry; final failure/error preservation. P1: mandatory scan outcome/report verification and successful SHA-alignment verifier before SARIF upload. P2: hosted observation of recovery/admission; never imply local tests establish external service behavior.

Mapping: `.github/workflows/scorecard.yml`; `scripts/verify_scorecard_report.py`; `scripts/tests/test_scorecard_workflow.py`, `scripts/tests/test_verify_scorecard_report.py`; `docs/security/scorecard-readiness.md` and `.vi.md`.

Acceptance: recovery after first operational failure; two failures remain failing; evidence/checkout failure not masked; missing/invalid reports after reported success rejected; complete reports after partial publication failure not authorized; no fallback to prior failed-attempt artifacts; revision mismatch blocks SARIF; no findings filtering, permissions or security policy changes. Tests run red before implementation, then green, with actionlint and workflow policy checks. Full canonical hooks remain required for push. Hosted exact-head checks and actual publishing behavior are separate evidence, pending execution.

Compatibility: verifier CLI now requires --scan-outcome; workflow and synthetic callers updated together. Actual repo.commit remains authoritative diagnostic metadata. API publication precedes verifier by upstream design, so a mismatched public badge cannot be prevented/retracted by this downstream check. Retrying may duplicate publication; no exactly-once claim. Two attempts, no custom backoff because publishing-job restrictions prohibit shell steps.

VI: Sửa trên nhánh riêng từ main; giữ nguyên công việc runtime. Retry tối đa hai lượt, giữ report gốc, checkout sạch, không che lỗi cuối. Verifier bắt buộc scan success/đủ report và SHA khớp trước upload SARIF. Test tổng hợp, lint và hook không thay bằng chứng hosted. Chưa merge hay phát hành Marketplace; không đổi quyền/rule/secrets.
