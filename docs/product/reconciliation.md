# SA-DOC-015 reconciliation outcome / Kết quả đối chiếu

Original review: 2026-10-18, branch `local/stackaccord-documentation-strategy`, baseline `ca68a3159425495f771da866188df75a8237ab4b`. That source is preserved in Library v3. PR preparation on 2026-10-19 uses `docs/stackaccord-strategy`, baseline `121b05d166c359c1d520d035dca40e86ea9cca1f`, including PR #73 and PR #74. Scope: documentation and two encoding-QA Python files; no production-source, technical rename, license-text or release change. Merged PR #74 source changes are preserved; manual Windows release-identity validation passed in run 39176474395; installed-host acceptance remains pending.

## Completed scope

The [inventory](documentation-inventory.md) enumerates every tracked Markdown/reStructuredText/text document and extensionless legal notice, plus new local docs. **An exhaustive inventory is not an exhaustive semantic audit.** `current` means source-scoped reconciliation or canonical planning; `reviewed` means claim screening only; `historical` preserves evidence; `deferred` names an owner/verification gap. Current plan status is not inferred from a date or completion checkbox.

| Concrete conflict | Correction / evidence boundary |
|---|---|
| Overview production-ready, provider parity, unsupported competitor superiority and fixed test counts | EN/VI overview now states scoped source capabilities and pending artifact/host evidence; generated README rule table remains authority |
| Full/Snapshot 100% correctness, ~ms, unsupported provider matrix | EN/VI feature showcase removes guarantees and links source-reviewed adapter guides; no performance measurements invented |
| TypeScript null union/Date/casing/schema-generation claims | Actual descriptor output shown: quoted property names, optional `?`, date-like string, unknown fallback, decimal/int64 number caveat; export returns before later validation |
| CLI mistaken for live semantic Roslyn analyzer; credential priority/audit/rotation/provenance overstated | Pain-point EN/VI descriptions corrected; source-scoped security pages explain configured resolution, optional logging, non-rotation and supplied-hash limitations |
| Provider catalogs/body parsing/configuration and type-support overclaims | Eight adapter guides reconciled; Oracle opt-in can execute routines, MySQL lacks routine result shapes, PostgreSQL signature/OUT/TABLE handling, SQL Server live vs catalog limits explicit |
| Health/telemetry/quickfix safety overclaims | Eight component guides distinguish loopback from authentication, probe checks from semantic validity, injected telemetry behavior, heuristic edits and informational VS Code actions |
| Unpublished public CLI install, imagined Marketplace availability, automatic approval/install, wrong Docker entrypoint and signing assertions | Installation/build/quickstart and relevant operational/test snippets corrected; release guides retain pause and exact artifact/host gates |
| Historical Rust/MCP architecture treated as runtime; old enterprise grants/version roadmap used as current runbook | Historical authority explicitly marked; original bodies and legal/attribution retained; broken archive-banner links corrected |
| License era and ADR status inconsistent | OpenSSF guide now reflects v0.4.0 boundary and ≤v0.3.0 historical MIT; hubs state Proposed ADR recommendations await owner decisions |

## Remaining decisions and verification — not silently closed

1. **Release owner:** merged PR #74 aligns source metadata to 0.4.0 and engine ^1.91.0. The manual binary identity path passed in run 39176474395; installed-editor acceptance remains unverified. Verify exact artifacts, host versions, same-ID upgrades and listing availability. This docs branch cannot decide or publish it.
2. **ADR owners:** existing Proposed freeze/extract/preview recommendations need disposition before expanding IDE, observability/host, assessment, plugins, autodetection or providers. Documentation does not accept them.
3. **Provider/QA owners:** live integration and supported grammar/type/permission matrices are not certified. SQL Server live `max_length`→`CharLength` differs from catalog normalization; retain as a source issue requiring independent regression work, not a docs-only fix. Oracle opt-in routine execution must remain visible.
4. **Product/security/customer owners:** proposed default parse-only boundary, enterprise controls, agent tools and budgets require implementation/evaluation; discovery, name clearance and banking acceptance remain pending.
5. **Documentation/engineering owners:** the 20 maintained deferred documents below have now completed source-level review. Runtime/host outcomes remain unverified, with precise tests listed. Other `reviewed` rows remain screening-only; no recursive audit of plan status or all other documents was performed. Full repository-wide semantic certification is not claimed.

## Evidence and verification scope

Independent reviewers checked provider, installation/release, security/health/telemetry/quickfix and final claim consistency. Local docs synchronization, policy suite, relative file links and whitespace checks are recorded in the external local evidence report. Link checking covers file existence, not external URL availability or anchor correctness. No .NET/Node product tests, live DB/cloud calls, installed-editor tests or benchmark runs are claimed for this documentation-only batch. Source/API references explain the basis; test files are not new passing runs.

Tiếng Việt: Đã sửa mâu thuẫn cụ thể trên các trang người dùng chính và tạo kiểm kê toàn bộ đường dẫn. Không đánh đồng đã kiểm kê/sàng lọc với đã xác minh từng hành vi. Các dòng `deferred` và `reviewed` vẫn giữ phạm vi chưa kiểm chứng; không tuyên bố tài liệu đã thống nhất hoàn toàn. Quyết định release/ADR/provider, thử nghiệm host và phần rà soát sâu còn lại được giữ rõ. Tạm dừng Marketplace và hai bản vá trước không thay đổi.

## Final bounded pass: all 20 maintained deferred documents

Source review completed against the same baseline; every row below moves from `deferred` to `current / 20-file source review`. This is source validation, **not runtime acceptance**. Historical observations inside mixed documents remain dated and unchanged as evidence. The 63 plan-status rows and component ADR decisions are outside this pass.

| Exact file | Source-level disposition | Expected behavior / unexecuted runtime check |
|---|---|---|
| `docs/architecture.md` | Corrected CLI/library/editor wiring and stale API/health descriptions | Exercise CLI vs library entrypoints with the same synthetic descriptors; no hosted service implied |
| `docs/SOLUTION.md` | Reconciled composition/project/API examples and version assumptions | Build/restore selected source artifact and verify matching runtime dependencies in a later implementation gate |
| `docs/COMPONENT_INTERACTION.md` | Replaced obsolete interaction pseudocode with current call boundaries | Failure/cancellation trace through extraction, execution and reporting; no DB/editor invocation here |
| `docs/DATA_FLOW.md` | Reconciled descriptor acquisition, snapshots, outputs and early export | Fixture source/snapshot/assembly branches, input trust and export-before-validation behavior |
| `docs/STAGE_FLOW.md` | Reconciled pipeline, command/status and health lifecycle | Verify exit/status/partial/cancelled outcomes per command; do not infer all-command uniformity |
| `docs/USAGE.md` | Corrected actual options, environment/config, provider/format/install/CI claims | Exact binary help plus command fixtures; live Oracle and hosted CI ingestion remain unrun |
| `docs/05-operations/best-practices.md` | Source-checked YAML keys, CI and operating examples | Parse examples under current config binding; real DB permission and CI secret boundary tests require disposable/authorized environments |
| `docs/05-operations/best-practices.vi.md` | Same corrections and qualifications in Vietnamese | Same configuration/CI acceptance as English; no separate implied pass |
| `docs/05-operations/playbook.md` | Reconciled setup, commands and operational assumptions | Walk through reviewed snapshot/config with an exact CLI artifact, then approved disposable live checks |
| `docs/05-operations/playbook.vi.md` | Same source-scoped operational corrections in Vietnamese | Same walkthrough; no publication or live action performed |
| `docs/operations/release_guide.md` | Separated local verification, tag push, workflow result and channel publication | Hermetic helper tests first; actual artifact/channel evidence requires separate release authorization |
| `docs/operations/release_guide.vi.md` | Same release boundaries and existing environment policy in Vietnamese | Same acceptance; all-platform helper success text is not channel proof |
| `docs/03-components/tooling/visual-studio-extension.md` | Reconciled options, safe/live commands, SARIF handling and lifecycle from source | Windows VS host: consent/secret handling, CLI provenance, diagnostics/location, cancellation and shutdown |
| `docs/03-components/tooling/visual-studio-extension.vi.md` | Same source behavior and host limitations in Vietnamese | Same Windows fixture matrix; source inspection is not installed-host evidence |
| `docs/03-components/tooling/vscode-extension.md` | Reconciled command registration, process/report boundaries, actions and engine metadata | VS Code host: marker/framing, consent, stale-report rejection, cancellation/process cleanup and path handling |
| `docs/03-components/tooling/vscode-extension.vi.md` | Same source behavior and host limitations in Vietnamese | Same host matrix; merged source engine/version reflected, runtime acceptance still pending |
| `docs/01-overview/pain-points-solved.md` | Replaced unsupported outcome/uniqueness claims with 12 source-scoped scenarios | Synthetic DG004 projection/threshold, DG005 mapping, DG006 conventions, DG009 CLOB fixtures; provider/editor tests separate |
| `docs/01-overview/pain-points-solved.vi.md` | Same 12 scenarios, source references and limitations in Vietnamese | Same fixtures; no customer incident or market-frequency evidence invented |
| `docs/security/scorecard-readiness.md` | Verified workflow/script contracts; historical remote observations remain historical | Missing/failed suite, actual-SHA mismatch, skipped/cancelled job, OIDC/admission/artifact/ingestion checks require authorized hosted run |
| `docs/security/scorecard-readiness.vi.md` | Same bounded readiness, verifier outputs and upload conditions in Vietnamese | Same acceptance; no hosted alert closure or score improvement asserted |

### Human decisions versus runtime evidence

**Human decisions:** merged PR #74 records the source version/engine decision; separately authorize any future publication/hosted run; decide pending component ADRs; define intended naming-convention and Oracle format contracts before fixes. No release or remaining ADR decision was silently made in this documentation pass.

**Runtime evidence:** Windows/VS and VS Code installed-host matrix; OS-specific child termination and filesystem boundaries; disposable provider metadata/permission/charset cases; artifact signing/install/upgrade; actual CI/SARIF/Scorecard ingestion. Each document describes expected behavior and the required test. Lack of that runtime evidence does not block describing the inspected source, but does block claiming runtime acceptance.

**Implementation backlog:** see [SA-SRCBUG-001–004](research-backlog.md) for SQL Server length units, ignored convention, overbroad release success text and ignored Oracle format. These are source-observed discrepancies with synthetic reproduction rationale; inferred downstream impact is explicitly not a live result. Other documented scope/policy limits are not automatically bugs.

Tiếng Việt: Cả 20 tài liệu đã hoàn tất đối chiếu ở mức nguồn; không còn trì hoãn việc đọc nguồn trong nhóm này. Chưa chạy host/database/phát hành để xác nhận runtime. Các quyết định cần owner và các test cần môi trường thật được tách riêng; không mở rộng sang 63 kế hoạch và không sửa mã.

## Vietnamese text and encoding QA / Kiểm tra tiếng Việt và mã hóa

2026-10-18 follow-up: audited the prior 97 changed Markdown documents plus maintained Vietnamese/mixed-language documents selected from the inventory and all tracked `.vi.md` files. The exact deduplicated scope is 158 files: 97 prior changed, 110 maintained Vietnamese/mixed-language, 66 supplemental tracked `.vi.md`, with overlap. The scope manifest and JSON results accompany the downloadable bundle. This is not a claim that every repository sentence was spellchecked.

Strict UTF-8, U+FFFD, control characters, common mojibake patterns and NFC checks found **no existing Vietnamese encoding corruption** in that scope. Human review instead identified missing EN/VI qualifiers, an ambiguous phrase, two unaccented Vietnamese CMD comments and one duplicated environment-variable name in an English comment. Nine documents received 15 targeted replacements; executable commands/IDs were not normalized wholesale. Historical/legal text was not rewritten.

The read-only [encoding checker](../../scripts/check_document_encoding.py) and [12 regression cases](../../scripts/tests/test_document_encoding.py) detect injected byte/control/replacement/NFD/mojibake problems and distinguish code/URLs/technical identifiers from likely unaccented prose. Language/NFC heuristics produce review findings, not automatic edits; they are not a full Markdown parser or spellchecker. Run from repository root:

```bash
python3 -m unittest discover -s scripts/tests -p test_document_encoding.py
python3 scripts/check_document_encoding.py docs/product/strategy.vi.md docs/product/roadmap.vi.md docs/product/marketplace-copy.vi.md
```

A standalone Vietnamese HTML reading copy of those three canonical documents is included as a deliverable only. It declares UTF-8, Vietnamese language, accessible line height and system font fallbacks; it fetches no fonts/assets. Actual HTML bytes were rendered in local Chromium at desktop and mobile-sized viewports. Direct `file:` navigation was blocked by browser policy, so the render harness supplied the bytes with `set_content`; opening a downloaded file on a real device remains unverified. No iPhone/Safari or universal font claim is made. Markdown itself embeds no fonts.

Human-readable bundled log copies remove ANSI color escapes; original raw logs remain in local evidence storage. ZIP payloads and names are checked for strict UTF-8/NFC, replacement characters and invalid controls. Preserve UTF-8 when opening/saving Markdown; use the included HTML for a formatted offline reading view.

Tiếng Việt: Không tìm thấy lỗi mã hóa tiếng Việt trong 158 file đã kiểm tra; không tạo ra một “lỗi font” giả để sửa. Đã sửa các sai lệch câu chữ/ý nghĩa có bằng chứng và giữ nguyên định danh kỹ thuật. Kiểm tra Chromium không thay thế kiểm tra iPhone thực; phạm vi và hạn chế được ghi trong bằng chứng kèm gói.
