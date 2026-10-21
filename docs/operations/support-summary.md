# Visual Studio support summary

[Tiếng Việt](support-summary.vi.md) · [Candidate acceptance](../testing/vs-beta-acceptance.md)

The Visual Studio menu **Export Support Summary** opens an immutable preview. Review it, choose **Save locally…**, then choose the destination. Cancel either dialog to write nothing. Saving is local only; there is no upload or telemetry endpoint. Sharing the saved file is a separate user decision. **View Diagnostic Logs** continues to open the local diagnostic log; that file is not a support summary and must not be shared automatically.

The summary contains strictly validated extension/host version and commit when available, a generated operation identifier, an outcome label and exit/duration information and up to 200 diagnostic entries from the latest handled operation in this session. Entries contain an opaque file number, rule ID, severity and numeric line/column. Unknown metadata remains unknown, including provider when unavailable. The identifier is local to the summary, not distributed tracing correlation. The export is capped at 64 KiB and states when diagnostic collection is truncated. It does not represent every retained Error List item; a failed run can leave older UI diagnostics visible. Actual UI behavior still needs installed-host acceptance.

Raw log files, diagnostic messages, file paths, SQL, source, configuration, environment values, connection strings, ActivityLog, audit logs/keys and crash dumps are excluded. Do not add them manually to work around missing context. Report a minimal synthetic reproduction separately when needed. This first implementation is Visual Studio only; VS Code has no equivalent exporter in this change.

## Local logging boundaries

Known secrets, connection metadata, URLs, paths and SQL text are conservatively removed before the VS Output and disk sinks. Input over 32768 characters is replaced entirely before scanning; records are bounded to 8192 UTF-8 bytes of sanitized payload (excluding timestamp envelope). Disk storage keeps at most one 1 MiB active file and one 1 MiB backup (`.1`), with best-effort writes. An oversized old active file is discarded on the next successful rotation, rather than preserved as an oversized backup. Existing historical log contents are not retroactively sanitized. Arbitrary prose cannot be guaranteed anonymous, so logs remain unsuitable for automatic export.

Sanitization flattens multiline Output text; evaluate banner readability during Windows acceptance. Detailed logging is configurable and synchronous; bounded size does not prove UI responsiveness under slow storage. Error List messages/navigation retain their existing behavior; this patch changes diagnostic logging boundaries. ActivityLog contents are not read or collected. Audit storage and other CLI/VS Code/telemetry sinks have separate contracts and are not made safe by this change.

## Verification and limits

Host-free regressions cover long secrets, bearer/connection/SQL/path/control input, size limits, rotation and summary allowlists. The production helper files are linked into Core.Tests; these execute the helper behavior but are not counted as production instrumentation in the canonical coverage union, and do not cover installed-editor UI. Windows preview/save/cancel, lifecycle and actual output-pane behavior require the [candidate checklist](../testing/vs-beta-acceptance.md). No percentage here claims whole-product correctness or coverage.

The fixed public option text `'--ide-safe'` remains readable in CLI rejection messages. This does not exempt other quoted arguments, credential assignments or complete SQL statements from redaction.

Display-only Beta transition: the menu/Output pane is **StackAccord (Beta)**; the visible settings path is **Tools > Options > StackAccord**. The canonical Options/automation category remains `DataGuard`; command IDs, CLI/config, diagnostics and file names remain unchanged. Verify identity/settings retention on the exact Windows artifact.
