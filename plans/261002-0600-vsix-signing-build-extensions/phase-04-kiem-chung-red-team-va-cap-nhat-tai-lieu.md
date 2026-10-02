---
phase: 4
title: "Kiem chung red-team va cap nhat tai lieu"
status: completed
priority: P2
effort: "1h"
dependencies: [3]
---

# Phase 4: Kiem chung red-team va cap nhat tai lieu

## Overview
Tiến hành kiểm thử toàn diện (red-team review, secret scan, edge cases) và cập nhật tài liệu hướng dẫn song ngữ (`extension-build-guide.vi.md` và `extension-build-guide.md`) để người dùng/owner biết cách sử dụng tính năng ký số khi chạy `build-extensions.bat`.

## Requirements
- **Functional**:
  1. Kiểm thử các ca biên (edge cases):
     - Ký thất bại khi đường dẫn cert không đúng → Báo lỗi rõ, exit code ≠ 0.
     - Ký thất bại khi password sai → Báo lỗi rõ, exit code ≠ 0.
     - Chạy bình thường không cờ ký → Exit code 0, hash SHA-256 khớp.
     - Chạy có ký → Exit code 0, hash SHA-256 khớp với file đã ký, `verify-vsix-signature.ps1` trả về 0.
  2. Cập nhật tài liệu:
     - `docs/05-operations/extension-build-guide.vi.md`
     - `docs/05-operations/extension-build-guide.md`
     - Bổ sung mục "Ký số VSIX (Tùy chọn)" với ví dụ chạy cả bằng PowerShell lẫn Batch file.
- **Non-functional**:
  - Quét an toàn mã nguồn: Không để sót file cert test hoặc mật khẩu trong git status.
  - Vượt qua tất cả repo gates hiện hành (`scripts/check-license-consistency.py`, `scripts/check-workflow-policy.py`).

## Implementation Steps
1. Thực hiện quét bí mật và kiểm tra trạng thái git:
   ```bash
   git status
   git diff --name-only | grep -E "\.(pfx|p12|cer|crt)$" # Phải trả về rỗng
   ```
2. Cập nhật `docs/05-operations/extension-build-guide.vi.md`:
   - Thêm phần "Ký số VSIX cho Visual Studio (Tùy chọn)":
     ```text
     ### Ký số VSIX (Tùy chọn)
     Mặc định, gói VSIX được build ở chế độ chưa ký số (Digital Signature: None).
     Nếu bạn có chứng chỉ Code Signing (.pfx) hoặc chứng chỉ trong Windows Store:

     # Chạy qua CMD / Batch:
     set DATAGUARD_VSIX_CERT_PASSWORD=mat_khau_cert
     scripts\build-extensions.bat -CertificatePath "C:\duong_dan\cert.pfx"

     # Hoặc qua Thumbprint trong Certificate Store (CurrentUser\My):
     scripts\build-extensions.bat -CertificateThumbprint "THUMBPRINT_HEX"
     ```
3. Cập nhật `docs/05-operations/extension-build-guide.md` tương đương bằng tiếng Anh.
4. Chạy các script gate kiểm tra tính nhất quán:
   ```powershell
   python scripts/check-license-consistency.py
   python scripts/check-workflow-policy.py
   ```

## Success Criteria
- [x] Không có file nhạy cảm (`.pfx`, `.p12`) xuất hiện trong git index.
- [x] Tài liệu hướng dẫn mô tả chính xác cú pháp chạy với `build-extensions.bat` và `build-extensions.ps1`.
- [x] Mọi pre-commit / local gate hiện hành đều PASS.

## Risk Assessment
| Rủi ro | Mức độ | Biện pháp giảm thiểu |
|---|---|---|
| Lỗi cú pháp khi cập nhật markdown làm hỏng license gate | Thấp | Chạy `python scripts/check-license-consistency.py` ngay sau khi sửa docs |
