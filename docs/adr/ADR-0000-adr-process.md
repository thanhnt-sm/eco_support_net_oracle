# ADR-0000: Architecture Decision Record process

- **Status:** Accepted (process document)
- **Date:** 2026-10-05
- **Scope:** `docs/adr/` only

## Context

The red-team report `plans/reports/redteam-261004-1500-source-vs-original-goals.md` (§4 "Scope creep",
recommendation 22) found that roughly half of `src/` lies outside the original DataGuard goals in
`research/muc_tieu/1.md`..`5.md`, and that no ADR compared those components with the goals. The
remediation plan `plans/261005-0900-redteam-remediation/plan.md` (Decisions, row "Out-of-scope
components") asks for one ADR per component with a `keep | freeze | extract` recommendation and no
deletion. `research/muc_tieu/5.md:17` had already proposed a `docs/adr/` folder in the original repo
layout.

The repository already contains two other decision logs. They stay where they are:

| Location | What it records |
|---|---|
| `docs/adr/` (this folder) | Product component decisions: what a component is for, whether it stays in this repository. Sequential `ADR-NNNN`. |
| `docs/decisions/` | Council-style business decisions, dated `ADR-YYYYMMDD-<slug>` (for example the licence decision). |
| `docs/observability/adr.md` | Internal design ADRs (ADR-001..015) of the observability reference implementation. |

## Decision

### Numbering and file names

- File name: `docs/adr/ADR-NNNN-<kebab-slug>.md`, four digits, sequential, never reused.
- ADR-0000 is this process. ADR-0001..0010 are the Phase 6.1 component ADRs.
- `docs/adr/README.md` lists every ADR with its recommendation and owner decision.

### Template

Every component ADR has these sections, in this order:

1. **Status** with date and the commit the measurements were taken at.
2. **Context**: what the component is and how it is wired.
3. **Original goal said**: a quote with `file:line` from `research/muc_tieu/`. If the goals are
   silent, say so and quote the nearest constraint.
4. **What exists**: LOC, test files, CI jobs, release artifacts.
5. **Risks it adds**: maintenance, attack surface, licence, release size.
6. **Options**.
7. **Recommendation**: one of `keep | freeze | extract`, with the reasons tied to the original goals.
8. **Owner decision**: `pending` until the owner fills it in.
9. **Consequences**: what changes if the recommendation is accepted.
10. **Tóm tắt (VI)**: one Vietnamese summary paragraph.

ADRs are written in English with the Vietnamese summary at the end. There are no `.vi.md` twins for
ADRs.

### Recommendation vocabulary

| Value | Meaning |
|---|---|
| `keep` | First-class product component. It is developed, documented and released like the core. |
| `freeze` | Stays in this repository and in CI. Only bug fixes, security fixes, dependency updates and changes forced by Core API changes. No new features. Its component doc says "frozen" and links the ADR. |
| `extract` | Moved to a separate repository or branch chosen by the owner, with its history (`rules/workspace_governance.md`, cleanup rule 2). In the same change it leaves `DataGuard.sln`, `DataGuard.CrossPlatform.slnf`, the workflows, the release artifacts, and the docs and links that point to it. |

`remove` is not a recommendation an ADR can make in this plan. Under governance rule 4 of `CLAUDE.md`
and cleanup rule 1 of `rules/workspace_governance.md`, a removal needs a `from → keep | extract |
rewrite | remove` manifest in `plans/` and an owner decision. An ADR can name a part as a removal
candidate for that manifest. There is no `archive/` or `legacy/` folder.

### Measurements

- LOC is every line, blank lines and comments included, of `*.cs` and `*.ts` files in the
  component, excluding `node_modules/`, `obj/`, `bin/`, `out/`, `dist/` and `*.d.ts`:
  `find <dir> -type f \( -name '*.cs' -o -name '*.ts' \) -not -path '*/node_modules/*' -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/out/*' -not -path '*/dist/*' -not -name '*.d.ts' -print0 | xargs -0 cat | wc -l`
- Test count is the number of `[Fact]`/`[Theory]` (and the LiveDb variants) attributes for .NET, and
  `test(`/`it(` calls for TypeScript.
- CI coverage comes from `.github/workflows/*.yml` and `DataGuard.CrossPlatform.slnf`. The Linux job
  `build-and-test` in `ci.yml` builds and tests every project in the filter.

### Lifecycle

`Proposed` → `Accepted` or `Rejected` → optionally `Superseded by ADR-NNNN`. Accepting the owner
decision changes only the Status and Owner decision fields. If the recommendation itself changes,
write a new ADR that supersedes the old one. The ADR does not perform the change. Implementation is a
separate change that follows `CLAUDE.md` (build, tests, docs in the same change).

## Consequences

- Each out-of-scope component has one place that records why it exists and what the owner decided.
- Reviewers can check a scope question against a quote from the original goals instead of memory.
- The numbers in an ADR are a snapshot. When a component changes a lot, re-measure and add a dated
  note instead of editing the old numbers.

## Tóm tắt (VI)

ADR-0000 quy định cách ghi quyết định kiến trúc cho DataGuard. ADR thành phần nằm ở `docs/adr/`, đánh
số `ADR-NNNN` liên tục. Quyết định kinh doanh dạng hội đồng vẫn ở `docs/decisions/`, còn ADR nội bộ của
bộ observability vẫn ở `docs/observability/adr.md`. Mỗi ADR có trích dẫn mục tiêu gốc (file và dòng),
số liệu đo được (LOC, test, CI), rủi ro, phương án, và một khuyến nghị `keep | freeze | extract`.
Trường "Owner decision" để `pending` cho đến khi chủ sở hữu quyết. ADR không được đề xuất xóa trực
tiếp: muốn xóa phải có manifest `from → keep | extract | rewrite | remove` trong `plans/` và quyết định
của owner. Nội dung ADR viết bằng tiếng Anh, cuối mỗi ADR có một đoạn tóm tắt tiếng Việt, không tạo
bản `.vi.md` riêng.
