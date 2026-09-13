---
date: 2026-09-12
session: "260912-2059-full-claims-scope"
status: planning-only
scope: full-documented-capability-delivery
---

# Journal: 2026-09-12 — Full claims scope

## Context

Người dùng chọn triển khai mọi tính năng còn thiếu để đáp ứng toàn bộ claim hiện có trong tài liệu.
Quyết định này thay thế giả định docs-only/conditional-backlog, nhưng không thay đổi các safety, ABI,
privacy, external-authority và no-false-clean gates. WIP execution evidence và tài liệu hiện có phải được giữ nguyên.

## What Happened

- Scope amendment mở rộng kế hoạch thành 15 phases và 20 FC capability groups; mọi group vẫn `open`.
- Phase8 sẽ lập census exhaustive cho từng claim occurrence trong tài liệu hiện tại, kể cả EN/VI peer và claim mới phát hiện.
- CP8 phải hoàn tất trước source edit; Phase12 phải đi trước Phase11 vì shared classifier/code-action surface; Phase15 là final gate.
- [Nghiên cứu mở rộng](../../plans/260912-2016-scout-remediation/research/full-claims-research.md) ghi lại nguồn chính thức Microsoft,
  OSV, NuGet signed packages, Sigstore và BenchmarkDotNet; đây là thiết kế triển khai, không phải bằng chứng feature đã shipped.
- Bốn reviewer Sol đang red-team scope mở rộng; XR01–XR06 hiện được accepted vào plan, nhưng review vẫn ongoing và chưa có final GO.
- Turn này chỉ lập kế hoạch/journal: không source fix, build/test, external execution, commit hay push.

## Reflection

Full delivery làm tăng diện tích rủi ro: FC group không đại diện cho toàn bộ occurrence, và xóa hoặc đổi wording claim không đóng feature gap.
Health host, remote advisory/score, IDE/LSP/settings, semantic/code actions, integrity/plugins và performance đều cần implementation,
regression, host/live evidence phù hợp và parity EN/VI. Absolute claims phải có contract đo được hoặc owner gate; không hạ scope âm thầm.

## Decisions Made

| Decision | Rationale | Impact |
|---|---|---|
| Implement all current-doc capabilities | Bám lựa chọn owner, không quay lại docs-only | FC01–FC20 là delivery obligations, không phải backlog tùy chọn |
| Terra sole writer; Sol read-only advisor | Giữ lease/file ownership và checkpoint độc lập | Session sau dùng native collaboration; không dùng `ck:team` |
| CP8 trước source; Phase12 trước Phase11; Phase15 cuối | Khóa contract/census và dependency dùng chung trước consumer | Không bắt đầu feature edit trước CP8 hoặc final acceptance trước Phase15 |
| Preserve execution/doc WIP | Tránh mất evidence hoặc state của session trước | Chỉ refresh scope fingerprint khi executor tiếp tục |

## Next Steps

- Session Terra đọc amendment, full-claims ledger, cả 15 phase files và expanded research trước khi lấy lease.
- Sol tiếp tục challenge XR findings và kiểm occurrence coverage; accepted không có nghĩa là implemented hoặc final GO.
- Thực thi theo DAG, ghi evidence cho parent, RT children, FC groups và từng occurrence; external/owner blockers vẫn tách biệt.
- Handoff tương lai: [plan](../../plans/260912-2016-scout-remediation/plan.md) và [session prompt](../../plans/260912-2016-scout-remediation/session-handoff.md).
