---
title: "Tich hop ky so VSIX vao build-extensions bat va ps1"
description: "Cấu hình tùy chọn ký số VSIX (OPC digital signature) vào luồng build local build-extensions.bat và build-extensions.ps1, hỗ trợ PFX và Windows Certificate Store, tính lại SHA-256 sau khi ký, đi kèm bộ test TDD fixtures và kịch bản xác minh."
status: completed
priority: P2
effort: 6h
branch: feat/vsix-signing-build-extensions
tags: [vsix, signing, build-extensions, tdd, security, visual-studio]
blockedBy: [260930-1735-dataguard-gpl3-dual-license-migration]
blocks: []
created: 2026-10-02
---

# Tich hop ky so VSIX vao build-extensions bat va ps1

## Overview

Theo mặc định của dự án DataGuard, các gói VSIX xuất ra từ Visual Studio SDK là các gói **chưa ký số (unsigned OPC package)**. Khi cài đặt trên Visual Studio 2022 thông qua `VSIXInstaller.exe`, hộp thoại cài đặt sẽ hiển thị cảnh báo `Digital Signature: None`.

Kế hoạch này giải quyết yêu cầu: **Cho phép chạy `scripts/build-extensions.bat` (và script nền tảng `scripts/build-extensions.ps1`) có thể tự động ký số VSIX khi lập trình viên / release owner cung cấp chứng chỉ số**, đồng thời:
1. **Bảo toàn tính tương thích ngược**: Nếu không truyền cert / flag ký, build vẫn chạy 1-click unsigned bình thường, không làm gãy CI hoặc luồng build của developer khác.
2. **Bảo mật tuyệt đối**: Không lưu trữ file `.pfx` hoặc mật khẩu trong repo (`*.pfx`, `*.p12` đã nằm trong `.gitignore`). Hỗ trợ đọc mật khẩu từ biến môi trường hoặc Windows Certificate Store.
3. **Tính toàn vẹn (Integrity Invariant)**: Bắt buộc tính toán lại hash SHA-256 (`.sha256`) **sau khi ký**, vì việc ký số chèn các entry chữ ký số vào archive zip làm thay đổi SHA-256 của file.
4. **Khớp nối với Phase 5 (Dual-license Migration)**: Hiện thực hóa các script cốt lõi `scripts/sign-vsix.ps1`, `scripts/verify-vsix-signature.ps1` và test fixtures đã được định nghĩa tại `plans/260930-1735-dataguard-gpl3-dual-license-migration/phase-05-vsix-signing.md`.

---

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Nghien cuu va thiet lap tooling ky VSIX](./phase-01-nghien-cuu-va-thiet-lap-tooling-ky-vsix.md) | Completed |
| 2 | [Tests-first fixtures va verify script](./phase-02-tests-first-fixtures-va-verify-script.md) | Completed |
| 3 | [Trien khai sign-vsix va tich hop build-extensions](./phase-03-trien-khai-sign-vsix-va-tich-hop-build-extensions.md) | Completed |
| 4 | [Kiem chung red-team va cap nhat tai lieu](./phase-04-kiem-chung-red-team-va-cap-nhat-tai-lieu.md) | Completed |

---

## Red-Team & Adversarial Findings (Strict Gates)

- **Finding 1 (Silent Stale Hash)**: Ký VSIX sau khi đã tính hash SHA-256 sẽ dẫn đến file `.sha256` không khớp với file `.vsix` thực tế trên đĩa.
  - *Mitigation*: Tái cấu trúc bước staging trong `build-extensions.ps1`: `Stage VSIX` → `Sign VSIX (nếu có)` → `Compute SHA-256 Hash` → `Write .sha256`.
- **Finding 2 (Fragile Tooling Dependency)**: Bắt buộc máy phải cài `OpenVsixSignTool` sẽ làm gãy `build-extensions.bat` trên máy dev thông thường.
  - *Mitigation*: Chỉ kiểm tra tool khi `-SignVsix` hoặc `-CertificatePath` được chỉ định. Tự động tìm kiếm theo thứ tự: `OpenVsixSignTool` trong PATH / dotnet tool → `VsixSignTool.exe` từ Visual Studio SDK (`vswhere`) → `WindowsBase` `PackageDigitalSignatureManager` fallback.
- **Finding 3 (Secret Leaks via Command History)**: Người dùng gõ mật khẩu cert trực tiếp trên command prompt (`build-extensions.bat -Password secret`).
  - *Mitigation*: Khuyến nghị và ưu tiên tham số `-CertificatePasswordEnv` (đọc từ biến môi trường `DATAGUARD_VSIX_CERT_PASSWORD`) hoặc SecureString / Windows Certificate Store.
- **Finding 4 (Environment Isolation)**: Windows PowerShell 5.1 và PowerShell Core (pwsh 7) có thể khác biệt về cách load `WindowsBase` assembly cho OPC signatures.
  - *Mitigation*: Kịch bản ký và verify sử dụng script tương thích song song hoặc ưu tiên CLI chuẩn `OpenVsixSignTool` / `VsixSignTool`.

---

## Validation Questions & Decisions

1. **Q: `build-extensions.bat` chuyển tiếp tham số như thế nào?**
   - **Decision**: `build-extensions.bat` sử dụng cú pháp `%*` để chuyển tiếp toàn bộ tham số vào `build-extensions.ps1`. Khi người dùng gọi:
     ```cmd
     scripts\build-extensions.bat -CertificatePath "C:\certs\mycert.pfx" -CertificatePasswordEnv "MY_CERT_PASS"
     ```
     PowerShell sẽ parse đúng các named parameters.
2. **Q: File VSIX của VS Code (`dataguard-vscode-*.vsix`) có cần ký bằng tool này không?**
   - **Decision**: Không. VS Code marketplace và VS Code extension dùng cơ chế ký khác của Marketplace (VSCE / `ovsx`), không sử dụng chuẩn OPC `VsixSignTool` của Visual Studio. `Sign-Vsix` chỉ áp dụng cho Visual Studio extension (`artifacts/visualstudio/dataguard-visualstudio-*.vsix`).

---

## Dependencies & Handoff

- **BlockedBy**: `plans/260930-1735-dataguard-gpl3-dual-license-migration/phase-05-vsix-signing.md` (đồng bộ spec và vị trí file `scripts/sign-vsix.ps1`, `scripts/verify-vsix-signature.ps1`).
- **Cook Command**: `/ck:cook plans/261002-0600-vsix-signing-build-extensions`
