---
phase: 4
title: "Contribution CLA"
status: pending
priority: P2
effort: "2h"
dependencies: [1]
---

# Phase 4: Contribution CLA

<!-- Updated: Red Team + Validation Session 1 - V4: không commit CLA.md draft; chỉ một đoạn trong CONTRIBUTING(.vi); S6 bot hoãn với tiền đề cứng; S7 .mailmap -->

## Overview
Để owner giữ quyền bán giấy phép thương mại, đóng góp ngoài phải kèm quyền cấp phép lại. Không có thỏa thuận thì đóng góp mặc định theo giấy phép repo (GitHub ToS D.6 "inbound = outbound" [recheck trước khi trích]) = chỉ GPL → không đưa được vào bản thương mại. DCO không đủ. Chưa có contributor ngoài (0 star/0 fork) → phase giữ **tối thiểu**: một đoạn trong CONTRIBUTING EN/VI yêu cầu **liên hệ owner để ký thỏa thuận contributor trước khi mở PR**; **không** commit `CLA.md` (văn bản đầy đủ = điều kiện mở lại khi có PR ngoài đầu tiên). Kèm `.mailmap` gộp hai identity của owner (S7) làm bằng chứng chuỗi sở hữu cho ADR (P6).

## Requirements
- Functional: CONTRIBUTING(.vi) có đoạn contributor agreement; `docs/contributing.md:305` hết sai lịch sử (Apache-2.0 + PolyForm); `.mailmap` map `thannt <thannt@thannts-MacBook-Pro.local>` → `thanhnt-sm <thanhnt.sm@gmail.com>`; dependabot được miễn (chỉ lockfile).
- Non-functional: không dependency/toolchain; không bot; `CODE_OF_CONDUCT.md` không đổi; không file root mới ngoài `.mailmap` (**kiểm**: `.mailmap` không nằm trong `ROOT_FILE_PATTERN` của `scripts/anti_garbage_guard.sh:6` → xem bước 4).

## Architecture
<!-- Updated: Red Team + Validation Session 1 - S6: tiền đề cứng cho bot; "contents: write chỉ cho nhánh chữ ký" không phải quyền GitHub có thật -->
Luồng: contributor đọc CONTRIBUTING → liên hệ owner → ký thỏa thuận (ngoài repo) → mở PR, tick checkbox → owner kiểm khi review → merge.
**Bot CLA: hoãn.** Tiền đề cứng nếu bật sau này: (a) ruleset trên `main` chặn push từ `github-actions[bot]`; (b) chữ ký lưu ở **repo riêng** qua PAT scoped (không có quyền "contents: write chỉ cho một nhánh"); (c) thêm `cla.yml` vào `WORKFLOWS` của `scripts/check-workflow-policy.py:36-40` kèm rule: `pull_request_target` không được checkout `head.ref` của PR (hôm nay chưa có workflow `pull_request_target`); (d) ghim SHA đầy đủ (`standards-audit.yml`), `permissions:` tối thiểu, allowlist `dependabot[bot]`.

## Related Code Files
<!-- Updated: Red Team + Validation Session 1 - bỏ docs/legal/CLA.md; thêm .mailmap -->
- Create: `.mailmap` (2 dòng: gộp identity `thannt@thannts-MacBook-Pro.local` vào `thanhnt.sm@gmail.com`; kiểm bằng `git shortlog -sne` sau khi thêm → 1 dòng owner + dependabot).
- Modify: `CONTRIBUTING.md`, `CONTRIBUTING.vi.md` (thêm **một** mục "Licence & contributor agreement", 1 đoạn: đóng góp ngoài cần thỏa thuận contributor cấp cho owner quyền cấp phép lại (GPL-3.0-only **và** thương mại); liên hệ `<email placeholder>` trước khi mở PR; dependabot miễn; khai báo phần do AI sinh nếu có), `.github/PULL_REQUEST_TEMPLATE.md` (+1 checkbox: "Contributor agreement agreed with the owner (see CONTRIBUTING → Licence)" — không trỏ tới file CLA), `docs/contributing.md:305` (thay "Apache-2.0 (code) và PolyForm Noncommercial 1.0.0" bằng GPL-3.0-only + tham chiếu CONTRIBUTING).
- **Không tạo**: `CLA.md`, `docs/legal/CLA.md`, `.github/workflows/cla.yml`.

<!-- Updated: Red Team + Validation Session 1 - TDD: Tests-first -->
## Tests-first (Red → Green)
1. RED hôm nay: `grep -ciE "contributor agreement|thỏa thuận" CONTRIBUTING.md CONTRIBUTING.vi.md` → 0/0; `test -f .mailmap` → 1; `git shortlog -sne HEAD | wc -l` → 3 (thannt 170, thanhnt-sm 144, dependabot 11); `grep -n "PolyForm\|Apache-2.0" docs/contributing.md` → dòng 305.
2. GREEN đích: `grep -cE "contributor agreement" CONTRIBUTING.md` ≥ 1 và `grep -cE "thỏa thuận" CONTRIBUTING.vi.md` ≥ 1; `git shortlog -sne HEAD | wc -l` → 2; `git log --format=%aE HEAD | sort -u | wc -l` → 2; `grep -n "PolyForm\|Apache-2.0" docs/contributing.md` → 0; `git grep -n "CLA.md"` → 0; `bash scripts/anti_garbage_guard.sh` (với `.mailmap` staged) → pass; `./scripts/verify_docs_sync.sh` xanh; `python3 scripts/check-license-consistency.py` exit 0 (CONTRIBUTING nằm trong `FILE_LIST`).

## Implementation Steps
1. **Chạy các lệnh RED ở Tests-first #1 và ghi kết quả.**
2. Thêm đoạn contributor agreement vào CONTRIBUTING EN/VI (không thêm mục khác); sửa `docs/contributing.md:305`; thêm checkbox PR template.
3. Tạo `.mailmap`; chạy `git shortlog -sne HEAD` xác nhận gộp.
4. Stage `.mailmap` và chạy `bash scripts/anti_garbage_guard.sh`. Nếu bị chặn (không có trong `ROOT_FILE_PATTERN`, `anti_garbage_guard.sh:6`) → **không sửa guard trong phase này**; ghi BLOCKED-mailmap, chuyển `.mailmap` thành hạng mục owner quyết ở P6 (thêm pattern là thay đổi topology cần đọc `rules/workspace_governance.md`).
5. Chạy Tests-first #2.

## Success Criteria
<!-- Updated: Red Team + Validation Session 1 - kiểm bằng lệnh -->
- [ ] Tests-first #2 đạt toàn bộ (hoặc BLOCKED-mailmap ghi rõ + các tiêu chí còn lại đạt).
- [ ] Không có `CLA.md`/`cla.yml` trong diff; bot vẫn hoãn với tiền đề (a)–(d) ghi ở Architecture.
- [ ] `./scripts/verify_docs_sync.sh` xanh (CONTRIBUTING*.md trong `REQUIRED_DOCS`).

## Risk Assessment
| Rủi ro | K×T | Giảm thiểu |
|---|---|---|
| Nhận PR ngoài khi chưa ký thỏa thuận → mất khả năng thương mại hóa phần đó | Thấp×Cao | Đoạn CONTRIBUTING + checkbox + owner tự kiểm; nếu lỡ merge: xin thỏa thuận hồi tố hoặc viết lại |
| Bot `pull_request_target` bị lợi dụng | Thấp×Cao | Hoãn; tiền đề cứng (a)–(d) |
| `.mailmap` bị guard chặn | TB×Thấp | Bước 4: không sửa guard, đẩy lên owner |
| Rollback | — | Revert commit; chưa ai ký nên không có dữ liệu |
