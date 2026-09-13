---
phase: 3
title: Synthesis and handoff
status: completed
effort: ''
---

# Phase 3: Synthesis and handoff

## Overview

Tổng hợp báo cáo có thể kiểm tra lại và danh sách việc cần xử lý tiếp.

## Implementation Steps

1. Đối chiếu ledger của mọi worker với baseline; xử lý mọi file thiếu trước khi tuyên bố đầy đủ.
2. Đọc lại bằng chứng finding quan trọng và xử lý kết luận trùng/mâu thuẫn.
3. Viết inventory, traceability, summary tiếng Việt; phân biệt static evidence và runtime evidence.
4. Kiểm tra link báo cáo, git diff và docs-sync sau khi tạo artifact; ghi handoff theo workspace rule.

## Success Criteria

- Summary có mức khớp theo module, ưu tiên P1/P2/P3 và phần chưa xác minh.
- Không thay đổi production/plan cũ; không commit/push.
- Mọi báo cáo liên kết được; nếu còn unread phải ghi chưa hoàn tất.
