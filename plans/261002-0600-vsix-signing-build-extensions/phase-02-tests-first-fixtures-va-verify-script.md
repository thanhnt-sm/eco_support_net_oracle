---
phase: 2
title: "Tests-first fixtures va verify script"
status: completed
priority: P2
effort: "1.5h"
dependencies: [1]
---

# Phase 2: Tests-first fixtures va verify script

## Overview
Xây dựng kịch bản kiểm thử TDD (Red → Green) hoàn chỉnh trước khi tích hợp vào luồng build: tạo fixture VSIX tối giản, viết script kiểm tra tính hợp lệ của chữ ký số (`scripts/verify-vsix-signature.ps1`), và bộ test tự động kiểm tra 4 trạng thái chữ ký (unsigned, signed, tampered, mismatched thumbprint).

## Requirements
- **Functional**:
  1. Tạo fixture `minimal-unsigned.vsix` hợp lệ về mặt cấu trúc zip (gồm `[Content_Types].xml` và `extension.vsixmanifest`).
  2. Triển khai `scripts/verify-vsix-signature.ps1`:
     - Tham số: `-VsixPath` (bắt buộc), `-ExpectedThumbprint` (tùy chọn), `-ExpectedSubject` (tùy chọn).
     - Kiểm tra sự tồn tại của chữ ký số OPC trong file VSIX.
     - Kiểm tra tính nguyên vẹn (validity) của chữ ký số.
     - So khớp thumbprint/subject nếu được chỉ định.
     - Exit code 0 khi hợp lệ; Exit code 1 khi không có chữ ký, chữ ký bị hỏng (tampered) hoặc sai thumbprint.
  3. Xây dựng runner test `scripts/tests/vsix-signature/run.ps1` kiểm tra đầy đủ 4 ca kiểm thử.
- **Non-functional**:
  - Không để lại certificate rác trong Windows Certificate Store sau khi chạy test.
  - Fixture nhẹ (<10KB), không phụ thuộc vào toàn bộ output build của Visual Studio.

## Architecture
```
scripts/tests/vsix-signature/
  ├── fixtures/
  │   └── minimal-unsigned.vsix      <- ZIP chứa manifest & content types
  ├── helpers.psm1                   <- Hàm tạo/xóa cert test
  └── run.ps1                        <- Test suite (Red -> Green)
```

## Related Code Files
- Create: `scripts/verify-vsix-signature.ps1`
- Create: `scripts/tests/vsix-signature/fixtures/minimal-unsigned.vsix`
- Create: `scripts/tests/vsix-signature/run.ps1`

## Tests-first (Red → Green)
1. **Red**:
   - Chạy `pwsh -File scripts/tests/vsix-signature/run.ps1` trước khi hoàn thiện `verify-vsix-signature.ps1` và `sign-vsix.ps1` → Test suite báo RED (thất bại do thiếu script hoặc chưa ký).
2. **Green**:
   - Ca 1: `verify-vsix-signature.ps1 -VsixPath minimal-unsigned.vsix` → Trả về exit code 1 (PASS kỳ vọng).
   - Ca 2: Ký file fixture bằng cert test → `verify-vsix-signature.ps1 -VsixPath minimal-signed.vsix` → Trả về exit code 0.
   - Ca 3: Sửa 1 byte trong file đã ký → `verify-vsix-signature.ps1 -VsixPath minimal-tampered.vsix` → Trả về exit code 1.
   - Ca 4: Verify file đã ký với `-ExpectedThumbprint "0000000000000000000000000000000000000000"` (sai thumbprint) → Trả về exit code 1.

## Implementation Steps
1. Tạo thư mục `scripts/tests/vsix-signature/fixtures/`.
2. Tạo script tạo `minimal-unsigned.vsix` từ XML template tối giản bằng `System.IO.Compression.ZipArchive`.
3. Viết `scripts/verify-vsix-signature.ps1` sử dụng `System.IO.Packaging.PackageDigitalSignatureManager` (`Add-Type -AssemblyName WindowsBase` hoặc `System.IO.Packaging`):
   ```powershell
   $package = [System.IO.Packaging.Package]::Open($VsixPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read)
   $sigManager = New-Object System.IO.Packaging.PackageDigitalSignatureManager($package)
   if (-not $sigManager.IsSigned) { exit 1 }
   $verifyResult = $sigManager.VerifySignatures($true)
   if ($verifyResult -ne [System.IO.Packaging.VerifyResult]::Success) { exit 1 }
   ```
4. Viết `scripts/tests/vsix-signature/run.ps1` kết nối 4 ca test trên.

## Success Criteria
- [x] Chạy `scripts/tests/vsix-signature/run.ps1` pass 100% (4/4 ca kiểm thử).
- [x] Mọi cert tạm thời được dọn dẹp sạch sẽ qua khối `finally`.

## Risk Assessment
| Rủi ro | Mức độ | Biện pháp giảm thiểu |
|---|---|---|
| `WindowsBase` không khả dụng trên pwsh 7 (Linux) | Thấp | Kiểm tra OS platform; nếu không phải Windows, báo skip kèm lý do môi trường |
