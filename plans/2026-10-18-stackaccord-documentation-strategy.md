# StackAccord documentation and long-term strategy implementation plan

Status: research synthesized and canonical documentation implemented; customer discovery, product acceptance and complete legacy-prose reconciliation remain pending. Original Library v3 source is preserved on `local/stackaccord-documentation-strategy`, base `ca68a3159425495f771da866188df75a8237ab4b`. Publication preparation uses separate branch `docs/stackaccord-strategy` on main `121b05d166c359c1d520d035dca40e86ea9cca1f`, which includes PR #73 and merged PR #74. The scope is 97 Markdown files and two encoding-QA Python files; no production-source, repository/technical-identity, or license-text changes. PR #74 source readiness is incorporated with bounded manual package evidence recorded below, without claiming installed-editor acceptance; no Marketplace release is authorized.

## Authorized outcome and boundaries

Create a coherent product plan for .NET/React/database workflows, security/performance, enterprise/banking and human/AI-agent use, with a consistent StackAccord brand direction and synchronized documentation. This authorizes planning and documentation development, not claiming future features, accepting proposed ADRs on behalf of the owner or publishing an extension. Do not merge local release fixes into this baseline merely to make claims look current.

## Initial deliverables

- `docs/product/README.md` / `.vi.md`: draft canonical index, authority order and provisional brand/mission/vision language.
- `docs/product/source-map.md`: 12 initial capability IDs, implementation/partial/reference/planned distinctions, named source/tests and 10 documentation contradictions.
- `docs/product/traceability.md`: requirement→capability→code→test/experiment→docs→release mapping, stage gates G0–G5, security/performance/human-agent/enterprise evaluation requirements.
- Historical-context annotations on obsolete Rust/MCP vision, dated roadmaps and mixed-era Marketplace plan; legacy product overview points to evidence mapping. No historical body or legal text removed.
- Documentation hub links to this draft work without pretending the proposed final strategy documents already exist.

## Prioritized implementation sequence

| Priority | Work | Acceptance |
|---|---|---|
| P0 | Preserve source/worktree/license/technical-identity boundaries; map authority and stale claims | Exact baseline, zero source/legal diffs, all new references resolve |
| P0 | Prevent planned/partial features and historical results from becoming release claims | Each capability/claim has status, source/test evidence and limitations; tests present are not counted as newly passed |
| P0 | Intake external market/full-stack/security/performance/agent findings | Dated evidence rows distinguish fact/inference; unresolved contradictions stay open; no invented market conclusions |
| P1 | Finalize one strategy/mission/vision and stage-gated roadmap after research | Backend-first current value; React/API and agent scope explicit; existing Proposed ADRs get an explicit decision path |
| P1 | Reconcile root README, PRODUCT, overview, feature showcase, pain-points, roadmap and Marketplace pages | EN/VI claim IDs agree; historical evidence remains linked; no unsupported availability, performance or certification promises |
| P1 | Create versioned capability/release-claims ledgers and evaluation plans | Every public capability maps to requirement/code/test/docs/artifact; installed-host acceptance remains separate |
| P2 | Expand research backlog/pilot protocols and automation for documentation drift | Run meaningful link/schema/claim checks; avoid duplicated parallel strategies and superficial keyword-only validation |

## Research incorporated and delivered

Primary-source capability/security research is recorded in `docs/product/research-backlog.md` with observations, inferences, limitations and experiments. Bilingual `strategy`, `roadmap` and `marketplace-copy` files now provide the canonical wording. `capabilities`, `release-claims` and `evaluation` separate implementation, validation, release, proposed controls and unmeasured budgets. Traceability maps requirements to modules, roles and experiments.

Consumer surfaces receive canonical links and explicit historical-claim qualifications; this is not a full rewrite of all legacy prose. SA-DOC-015 tracks remaining sentence-level reconciliation. No customer interviews, pilot results, paid commitments, trademark clearance or compliance certification are asserted.

## Verification for this documentation batch

Run repository docs synchronization/license consistency, inspect relative links in new/annotated files, and `git diff --check`. Do not run or claim .NET/Node product tests for doc-only edits; preserve earlier branch-specific evidence as such. A later source patch still requires the real .NET environment and relevant gates. No generated product artifacts or benchmark numbers are created here.

Tiếng Việt: Đã tổng hợp nghiên cứu và hoàn thành bộ chiến lược, lộ trình, nội dung Marketplace EN/VI cùng sổ năng lực/tuyên bố, đánh giá và backlog. Chưa thực hiện discovery khách hàng hoặc nghiệm thu artifact/host. Giữ nguyên DataGuard về kỹ thuật, lịch sử và giấy phép; vẫn tạm dừng xuất bản. Việc đối chiếu toàn bộ câu chữ tài liệu cũ còn ở SA-DOC-015.

## SA-DOC-015 bounded follow-up

Source-scoped corrections, independent review and exhaustive tracked-path classification are recorded in [reconciliation](../docs/product/reconciliation.md) and [inventory](../docs/product/documentation-inventory.md). Maintained high-risk pages were corrected; full semantic unification is not claimed. Deferred files and owner decisions remain explicit. No source/legal/technical identity changes; previous patches remain separate.

## Library v3 lineage and PR preparation / Nguồn Library v3 và chuẩn bị PR

The existing Library file `libfile_0b48a02a4ad88191bf645b5cf63bbc5f`, version 3, has ZIP SHA-256 `c47436f8864571324f3392d56f2493ff017e8d856bd29d881765990e51c2616f`. Its 158-document audit and standalone HTML remain historical deliverables; this source-publication task does not replace that ZIP or regenerate strategy. The original worktree and exact patch are preserved. Main PR #73 changes optional dispatch normalization; its source and tests remain unchanged. PR #74 is now merged: source metadata 0.4.0, engine ^1.91.0 and release identity checks are preserved. The two EN/VI VS Code guide conflicts were resolved by retaining the source-reconciled guide and updating its metadata, rather than restoring superseded prose. Manual release-path validation passed in run 39176474395; installed-host acceptance remains pending.

Tiếng Việt: Giữ nguyên ZIP Library v3 và nguồn gốc; chuẩn bị PR tài liệu riêng trên main có PR #73 và PR #74. Mã nguồn PR #74 không thay thế bằng chứng phát hành/nghiệm thu; không đổi ID kỹ thuật hoặc công bố Marketplace.

## Post-#74 conflict integration / Tích hợp sau PR #74

Main was rechecked at `121b05d166c359c1d520d035dca40e86ea9cca1f` before resuming the preserved non-rewriting merge. Only the EN/VI VS Code guides had Git conflicts; retain their source-reconciled behavior/limitations while updating source metadata to 0.4.0 and engine ^1.91.0. Existing source, workflows and scripts match that main byte-for-byte; the two documentation-QA tools remain the only added scripts. Original Library v3 and worktree remain unchanged.

[Build-only evidence](../docs/product/release-claims.md) records manual run39176474395 and its exact artifact/source/hash; Windows rejection harness and strict binary-identity check passed, both publication jobs skipped. Installed-editor acceptance and Marketplace publication are not established. Fresh integration checks:264 Python tests run,263 passed,one existing live-API skip;158-document encoding scan clean;1,186 relative links resolve;594inventory rows with67current/74deferred/300historical/153reviewed. Repository hooks and exact-head hosted checks remain required before merging PR #75.

Tiếng Việt: Đã đối chiếu main sau #74 và giải quyết hai conflict theo nội dung, giữ nguyên mã nguồn và các giới hạn nghiệm thu. Build-only Windows đã có bằng chứng đúng artifact/hash, nhưng chưa nghiệm thu editor hoặc phát hành Marketplace. PR #75 cần kiểm tra lại trên head cuối cùng trước khi merge.
