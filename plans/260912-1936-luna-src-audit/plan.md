---
title: 'GPT Luna scout DataGuard: source, plans, research, docs'
description: >-
  Rà soát toàn bộ src, đối chiếu plans/research/docs và xác minh build/test bằng
  GPT Luna.
status: completed
priority: P2
branch: main
tags:
  - docs
  - audit
blockedBy: []
blocks: []
created: '2026-09-12T12:37:46.480Z'
createdBy: 'ck:plan'
source: skill
---

# GPT Luna scout DataGuard: source, plans, research, docs

## Overview

Kết quả: [Summary](reports/summary.md), [Traceability](reports/traceability.md), [Inventory](reports/inventory.md), [Verification](reports/verification.md).

Rà soát source tại commit `93bf7288324dd746669ad09c5e2a592adc772748` (baseline sạch), đối chiếu hiện thực với kế hoạch, nghiên cứu và tài liệu; báo cáo tiếng Việt cùng bằng chứng thực chạy. Baseline: 89 file tracked src, 25 plans, 50 research, 143 docs. Không sửa production, cập nhật plan cũ, commit, push hoặc publish.

Agent chính điều phối; 8 worker dùng `gpt-5.6-luna`, reasoning `medium`, context riêng; tối đa 6 worker chạy đồng thời. Hai worker tài liệu gửi danh mục yêu cầu trước, rồi các worker source đối chiếu. Verification chạy độc lập, chỉ một worker sở hữu build output. Nhiệm vụ đọc được chia lô nhỏ, timeout phải để lại danh sách chưa đọc và được xử lý riêng.

## Phân công

| Worker | Phạm vi sở hữu | Báo cáo |
|---|---|---|
| Core engine | Abstractions, Models, Sources, Rules, Validation, PublicApi; manifest/lock Core | reports/core-engine.md |
| Core services | Assessment, AutoDetection, Baseline, Plugins, Reporting, Security, Telemetry | reports/core-services.md |
| Adapters | Bốn thư mục database adapter; đọc chéo parser SQL Server trong Core | reports/adapters.md |
| Tooling | Contracts, Analyzers, CodeFixes | reports/tooling.md |
| CLI/IDE | Cli, VSCode, VisualStudio | reports/cli-ide.md |
| Plans/research | Toàn bộ plans/research có tại baseline | reports/plans-research.md |
| Docs | Toàn bộ docs có tại baseline | reports/docs.md |
| Verification | .NET, VSCode test, docs-sync, giới hạn Visual Studio | reports/verification.md |

## Đầu ra và tiêu chí

- `reports/inventory.md`: ownership, trạng thái đọc, loại trừ và coverage file.
- `reports/traceability.md`: yêu cầu → source/caller → test → trạng thái, bằng chứng file:dòng.
- `reports/summary.md`: kết luận, ưu tiên P1/P2/P3, kết quả thực thi và phần chưa xác minh.
- Mọi file baseline có trạng thái; mọi finding có nguồn và độ tin cậy. Không coi source existence hoặc test cũ là bằng chứng runtime mới.
- Phân loại: khớp, một phần, chưa tìm thấy implementation, tài liệu lệch, định hướng/lịch sử, chưa đủ bằng chứng.
- Raw logs tại `.tmp/luna-src-audit-260912-1936/` (gitignored); báo cáo reviewable nằm trong thư mục plan.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Baseline and requirements](./phase-01-baseline-and-requirements.md) | Completed |
| 2 | [Source audit and verification](./phase-02-source-audit-and-verification.md) | Completed |
| 3 | [Synthesis and handoff](./phase-03-synthesis-and-handoff.md) | Completed |

## Dependencies

Các plan master/implementation, marketplace, agentize, workspace rationalization và fix-issues-gaps là nguồn đối chiếu; audit không thay đổi sản phẩm nên không tạo quan hệ chặn giả hoặc sửa trạng thái các plan này.

Scaffold bằng `npm exec --yes --package claudekit-cli@4.5.2 -- ck plan create`; gói `claudekit` được skill nêu không cung cấp executable `ck` trong môi trường này. Các chuyển trạng thái phase tiếp tục qua CLI.
