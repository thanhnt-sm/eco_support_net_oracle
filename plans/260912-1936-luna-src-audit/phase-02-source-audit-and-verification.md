---
phase: 2
title: Source audit and verification
status: completed
effort: ''
---

# Phase 2: Source audit and verification

## Overview

Đọc đầy đủ source theo ownership, truy vết caller/test và chạy verification hiện có.

## Implementation Steps

1. Năm Luna source đọc code/config/license/manifest/lockfile, mỗi lô 3–5 file hoặc đoạn khoảng 500 dòng; đọc chéo dependency khi cần.
2. Nối yêu cầu với implementation, caller và test; ghi path:dòng, độ tin cậy và tác động.
3. Verification: restore locked-mode, build Release, test solution; npm ci và npm test cho VS Code; docs-sync.
4. Visual Studio cần Windows/VS SDK: nếu môi trường không hỗ trợ, ghi blocker. Không dùng stale binaries khi build thất bại.
5. Không sửa code hoặc dependency để làm test xanh; lỗi môi trường và lỗi sản phẩm được phân biệt.

## Success Criteria

- 89 file src baseline có trạng thái đọc; không còn phần source chưa xử lý.
- Build/test có lệnh, exit code, số pass/fail/skip hoặc blocker rõ ràng.
- Worker chỉ ghi báo cáo thuộc ownership; raw logs gitignored.
