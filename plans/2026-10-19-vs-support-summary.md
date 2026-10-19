# Visual Studio support summary and beta acceptance / Tóm tắt hỗ trợ và nghiệm thu beta

Baseline: `76f67935b2d72d08c4c49cbd36b875aded329d7a` (main after PR76). Local branch: `fix/vs-support-bundle`. No publication, workflow dispatch, credential or protection changes are part of this batch. PR77 Unicode length work remains separate.

## Prioritized evidence and acceptance

| Priority | Reproduction and affected source | Patch / acceptance |
|---|---|---|
| P1 | `DataGuardLogger.Redact`: password longer than512 characters leaves a tail; `OutputPaneWriter` writes original text to VS Output after redacting only its disk mirror. Synthetic bearer/connection/SQL/path inputs demonstrate both boundaries. | Shared bounded privacy policy before both sinks; adversarial tests must fail before and pass after. Preserve Error List diagnostics and navigation. Redaction is conservative, not an anonymity guarantee. |
| P1 | Logger appends without rotation; startup header bypasses redaction; ActivityLog harvesting reads an arbitrary-size file. | Bound records and active+backup files, route startup through sanitizer, remove unnecessary ActivityLog read. Test rotation and oversized legacy files. Existing raw logs are not automatically safe to share. |
| P1 | Existing “View Diagnostic Logs” opens a raw local file. There is no safe support export contract. | Add a separate explicit Export Support Summary preview/save command. Allowlist bounded metadata and diagnostic coordinates; no raw logs/config/SQL/source/credentials/paths. Cancel writes nothing, no upload. Preserve original View command. |
| P2 | VS Code SQL-shape output includes query snippets; CLI/SARIF/activity/audit output is not universally sanitized. | Backlog: separate sink review and tests. Do not include these surfaces in a support summary or claim a cross-editor exporter. |
| P2 | Package checks cannot verify installed VS 18.10.2 behavior, navigation, lifecycle or upgrade. | Human acceptance of exact artifact hash remains a release blocker; use bilingual checklist. |

No P0 defect was confirmed in this bounded logging/export batch. Independent review added escaped/unterminated credential regressions and timeout-reporting normalization. Multiline Output formatting and installed UI ownership/focus/DPI remain P2 acceptance items.

## Compatibility and scope

Keep command 0x0103, add 0x0108, keep provider/CLI/diagnostic semantics and technical publisher IDs. No dependency upgrade, new telemetry, automatic support transfer or broad logging framework. Unknown provider/SHA remains unknown rather than guessed. Production helpers linked into Core.Tests permit host-free tests; those tests do not exercise VS UI or installed-host behavior.

Giữ command cũ, thêm command xuất riêng; không đổi contract provider/CLI/rule hay publisher ID. Dữ liệu chưa biết phải ghi unknown. Test helper trên Linux không thay thế nghiệm thu UI trong Visual Studio thật. Không thu thập hoặc gửi log tự động.

## Source → tests → documentation

- `LogPrivacyPolicy.cs`, `BoundedLogFile.cs`, `DataGuardLogger.cs`, `OutputPaneWriter.cs` → `VisualStudioLogPrivacyTests.cs` and Windows redactor tests → `docs/operations/support-summary.md` / `.vi.md`.
- `SupportSummary.cs`, `DataGuardPackage.Support.cs`, package outcome/publication hooks, `Commands/Menus.vsct` → `SupportSummaryTests.cs` plus pending Windows UI acceptance → same support guide.
- Existing `marketplace-release.yml` and `scripts/marketplace_release.py` remain unchanged → existing workflow/script gates → `docs/testing/vs-beta-acceptance.md` / `.vi.md`.

## Validation record

Capture red/green logs, independent review, full solution build, canonical local gates (uploads disabled), Node and Python checks, docs sync and bounded deterministic measurements under ignored local evidence directory. Record skips/failures explicitly. No claim of 100% correctness or whole-product coverage. Final local evidence report records actual results; this plan does not itself assert success.
