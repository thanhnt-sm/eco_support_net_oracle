---
title: "Scout remediation — Terra execution / Sol assurance"
date: 2026-09-12
session: "260912-2016-scout-remediation-planning"
type: milestone-journal
status: planning
---

# Journal — Scout remediation planning

## Context

Luna scout là đầu vào cho kế hoạch remediation tại baseline `93bf7288324dd746669ad09c5e2a592adc772748`.
Phạm vi gồm 60 source findings và 6 nhóm verification bổ sung (V01–V06).
Mỗi dòng ledger hiện vẫn `open`; chuẩn bị disposition không đồng nghĩa đã sửa hay đã verify.
Kế hoạch phải xử lý cả defect, false positive, historical, related và external gate một cách minh bạch.

## What Happened

Plan, findings ledger và solution research đã được đọc để chốt dependency, acceptance và evidence contract.
Online research dùng nguồn primary của Microsoft, Oracle, npm, Testcontainers và VS Code.
Các kết luận được giữ ở mức thiết kế: compiled EF artifact có trust rõ ràng, bounded incomplete result,
fail-closed cho encryption, metadata-only Oracle cursor, config precedence và claim boundaries trung thực.
Red-team: bốn Sol reviewer đưa20 nhận xét, hợp nhất15 điểm (4 Critical,11 High), đều đã bổ sung acceptance vào plan. Không điểm nào được coi là đã sửa source.
Phases 1–7 đều `Pending`, và execution evidence sẽ chỉ được tạo trong session thực thi tương lai.

## Reflection

Rủi ro lớn nhất là biến một phương án tốt hoặc một test “Passed” thành bằng chứng shipped/clean giả.
Vì vậy status phải phân biệt complete, incomplete, cancelled, failed và blocked_external/blocked_owner.
Các claim remote CVE, health host, IDE surface, plugin sandbox và benchmark cần owner/design riêng,
không được âm thầm mở rộng scope của bugfix và docs truth correction.
Compatibility công khai, persisted schema/hash semantics và điều kiện DB/Windows phải được kiểm độc lập.

## Decisions

Session sau dùng Terra làm executor/sole writer; Sol là read-only advisor tại các checkpoint và có quyền yêu cầu revise.
Dùng native collaboration cho Terra/Sol; `ck:team` không tương thích với mô hình Claude/Opus nên không dùng.
Không source fix hay build/test sản phẩm, DB execution, migration, publish, commit hoặc push trong turn lập kế hoạch. Research online và kiểm tra docs/link/ledger đã thực hiện.
Giữ nguyên audit cũ đang untracked và không cleanup `.pyc` hay tài sản historical khi thiếu manifest/owner approval.
Mọi row phải có reproduction, after behavior, command/assertion count, tree fingerprint, docs và Sol verdict.

## Next Steps

Terra session đọc đầy đủ phase files, ledger và handoff trước khi lấy lease; Program.cs/shared contracts làm tuần tự.
Sol kiểm tra baseline, compatibility và adversarial evidence; external DB, Windows và release gates không được suy diễn.
Sau mỗi batch, cập nhật ledger và `reports/execution-evidence.md`; tổng cuối phải thỏa `closed + blocked + open = 66`.
Theo dõi riêng15 RT child AC; parent không đóng trước child liên quan.
Prompt handoff tương lai: [$ck:cook plan](../../plans/260912-2016-scout-remediation/plan.md).
Chỉ sau khi các phase có evidence mới chạy full Release build, bốn C# test projects, Node suite và docs/link checks.
