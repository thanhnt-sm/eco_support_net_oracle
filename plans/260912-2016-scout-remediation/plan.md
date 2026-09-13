---
title: Scout remediation — Terra execution / Sol assurance
description: >-
  Full claim delivery: scout remediation plus missing health, advisory, IDE,
  semantic, integrity and performance capabilities; Terra execution with Sol gates
status: in-progress
priority: P1
branch: main
tags: []
blockedBy: []
blocks:
  - 260820-marketplace-extensions
created: '2026-09-12T13:20:09.274Z'
createdBy: 'ck:plan'
source: skill
---

# Scout remediation — Terra execution / Sol assurance

## Overview

**Owner scope amendment — 2026-09-12:** người dùng chọn **triển khai cả tính năng còn thiếu để đáp ứng toàn bộ claim**, không chọn docs-only. [Scope amendment](scope-amendment.md) và phases8–15 bắt buộc, ưu tiên cao hơn câu docs-only/conditional-backlog trong bản cũ. Phase7 là foundation verification, không final closure. WIP execution-evidence/docs từ session khác được giữ nguyên; bước này chỉ cập nhật plan.

Thiết kế remediation từ [Luna scout](../260912-1936-luna-src-audit/reports/summary.md), baseline `93bf7288324dd746669ad09c5e2a592adc772748`. Bước hiện tại chỉ tạo plan/research/red-team/handoff; mọi phase triển khai vẫn Pending. Không source fix, commit, push hay publish trong bước lập kế hoạch.

Đọc theo thứ tự: [finding ledger](findings-ledger.md), [quyết định và research](research/solution-research.md), [core design](reports/core-design.md), [safety design](reports/safety-design.md), [completion audit hiện trạng](reports/completion-audit.md), các phase, [handoff Terra/Sol](session-handoff.md), [red-team](reports/red-team.md).

Trong bộ tài liệu này, ưu tiên: workspace/user rules → accepted red-team amendments + plan/phase acceptance → ledger → research/design/advisory background. Trình tự sơ bộ trong researcher reports không thay thế DAG/sole-writer của plan. Nếu hai acceptance thực sự xung đột, Terra phải đưa Sol giải quyết trước edit, không tự chọn điều kiện dễ hơn. Phần triển khai hiện đã có bằng chứng local được ghi trong execution evidence; các external/owner gates vẫn không được suy diễn thành đã đóng.

## Scope và quyết định mặc định

- Xử lý đủ 60 source IDs và 6 nhóm bổ sung; không đánh đồng “đã có phương án” với “đã sửa”. Ledger giữ false positive, duplicates, historical và blocked riêng.
- Sửa confirmed defects; provisional findings cần characterization trước. Triển khai missing capabilities theo claims ledger; remove/downgrade claim không phải cách đóng feature-gap.
- Health host, online CVE/score, IDE settings/assess/realtime, semantic analysis/code actions, integrity verification và performance evidence là required delivery, không optional backlog. Claim tuyệt đối/mâu thuẫn cần measurable contract hoặc owner gate, không tự ghi done.
- Giữ `--offline --assembly` là Manual; Snapshot offline là validate với persisted schema và không connection. Live drift không thể suy ra từ tự so snapshot.
- Public APIs ưu tiên additive: không đổi return type hay positional constructor/deconstruction đang public chỉ bằng thêm optional parameter. Phase 1 chốt compatibility trước sửa.
- Không tự execute EF app/factory, Oracle procedures, DB migrations hoặc workflow publish. Trusted compiled EF artifact là opt-in, metadata unavailable phải hiện rõ, không empty-success.
- F6 tracked `.pyc` được giữ nguyên nếu chưa có manifest và owner approval. DB/Windows thiếu môi trường vẫn blocked, không được ghi resolved.

## Điều phối

Session mới dùng GPT-5.6 Terra làm executor/sole writer; GPT-5.6 Sol read-only advisor theo checkpoint. Sol thúc tiến độ bằng bằng chứng và yêu cầu revise, không có quyền git push. Default không có nhiều writer, không đụng source ngoài lease của batch. `Program.cs`/shared contracts/baseline luôn tuần tự.

DAG: 1 → 2 → 3; 3 → 4 và 5; 4 + 5 → 6 → 7. Các nhánh là dependency logic, không tự cấp quyền concurrent writers. Đọc [Sol protocol](session-handoff.md) để resume bằng files, không dùng agent IDs của session cũ.

Expansion DAG:8 after1;9 after3/4/8/13;10 after4/8;11 after5/8/10/12;12 after2/5/8;13 after4/8;14 after2/3/8/12;15 after7/9/10/11/12/13/14. Đọc phase8 trước source edits; execution theo DAG không theo số phase. Sole Terra writer, Sol kiểm cả capability closure mới.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Baseline and contracts](./phase-01-baseline-and-contracts.md) | In progress |
| 2 | [Core and provider correctness](./phase-02-core-and-provider-correctness.md) | In progress |
| 3 | [CLI snapshot and configuration](./phase-03-cli-snapshot-and-configuration.md) | In progress |
| 4 | [Security reporting and resilience](./phase-04-security-reporting-and-resilience.md) | In progress |
| 5 | [Tooling and IDE safety](./phase-05-tooling-and-ide-safety.md) | In progress |
| 6 | [Documentation and disposition](./phase-06-documentation-and-disposition.md) | In progress |
| 7 | [Verification and closure](./phase-07-verification-and-closure.md) | In progress |
| 8 | [Claim contract and coverage](./phase-08-claim-contract-and-coverage.md) | In progress |
| 9 | [Health host and probes](./phase-09-health-host-and-probes.md) | In progress |
| 10 | [Online advisory and health score](./phase-10-online-advisory-and-health-score.md) | In progress |
| 11 | [IDE feature completion](./phase-11-ide-feature-completion.md) | In progress |
| 12 | [Semantic analysis and code actions](./phase-12-semantic-analysis-and-code-actions.md) | In progress |
| 13 | [Integrity and trusted plugins](./phase-13-integrity-and-trusted-plugins.md) | In progress |
| 14 | [Performance and claim evidence](./phase-14-performance-and-claim-evidence.md) | In progress |
| 15 | [Full capability acceptance](./phase-15-full-capability-acceptance.md) | In progress |

## Dependencies

Plan này chặn readiness của [Marketplace plan](../260820-marketplace-extensions/plan.md), không thay thế các release/owner gates của plan đó. Luna scout là input đã hoàn tất, không dependency đang chặn. Historical EcoSupport plan không bị tái kích hoạt.

## Red Team Review

Expanded-scope review:4 Sol reviewers,12 raw →10 accepted obligations (2 Critical,8 High), all4 final design rechecks GO. [Expanded review and mandatory XR gates](reports/full-claims-red-team.md). CP8 census/owner contracts remain future prerequisites; no feature is claimed delivered.

Historical seven-phase review: four Sol reviewers, 20 raw →15 consolidated findings (4 Critical,11 High),15 accepted into phase requirements,0 rejected. [Adjudication/evidence](reports/red-team.md). These corrections remain required, but old GO does not certify the expanded scope.

## Validation Log

[Expanded validation](reports/full-claims-validation.md):15-phase DAG and exact60→66 coverage checked;20 FC groups and10 XR obligations retained. Docs existence/link/stub/diff checks pass; semantic claim census is required at CP8, not falsely reported complete here.

Historical seven-phase checks: two Sol GO verdicts;60 scout IDs →66 parent rows +15 RT children. [Original checks and limitations](reports/plan-validation.md). Expanded scope adds20 FC groups and15 phases; occurrence census remains a required Phase8 task. No product build/test/source-fix claim from this planning step.

## Execution Success Criteria

- Mỗi ID có disposition, owner Terra, acceptance, bằng chứng và Sol verdict; alias chỉ đóng khi canonical đóng.
- Source defects có failing-before/passing-after test hoặc tái hiện có lý do; docs bị ảnh hưởng cập nhật cùng batch, EN/VI parity kiểm theo nghĩa.
- Không false-clean khi truncated, unavailable metadata, thiếu schema hoặc integration chưa chạy.
- Full Release build + cả 4 C# test projects + Node suite + docs/link checks; live DB và Windows là gates riêng, bằng chứng phải gắn tree/commit đang kiểm.
- Final report có `closed + blocked + open = 66`; không tuyên bố tất cả resolved khi còn blocked. Có thể hoàn thành disposition planning mà implementation vẫn chưa hoàn tất.

## Handoff và skill routing

Risk acceptance: [30 scenarios và five-perspective prediction](reports/risk-scenarios.md), verdict CAUTION. Các S IDs phải map vào AC ledger trong CP1.

Đã dùng `ck:scout` input, `ck:plan`, `ck:research`, `ck:brainstorm`, `ck:predict`, `ck:scenario`, `ck:sequential-thinking`, `ck:project-organization` cho phân rã, research, quyết định, adversarial scenarios và placement; `ck:journal` ghi milestone. Bảng route chi tiết ở [research](research/solution-research.md).

Session thực thi đọc và dùng skills khi task match: `ck:cook`, `ck:debug`/`ck:fix`, `ck:backend-development`, `ck:databases`, `ck:security`, `ck:code-review`, `ck:test`, `ck:docs`, `ck:project-management`. Không bật tất cả skills vô điều kiện. `ck:team` Claude/Opus-specific không áp dụng; dùng native collaboration giữ đúng Terra/Sol. Task tools không có thì phase files + ledger là persistent task state.

Lệnh handoff dành cho session mới: `$ck:cook /Volumes/Data/101.AI/GitHub/eco_support_net_oracle/plans/260912-2016-scout-remediation/plan.md`. Copy prompt đầy đủ ở [session-handoff.md](session-handoff.md), không chạy cook trong bước lập kế hoạch này.
