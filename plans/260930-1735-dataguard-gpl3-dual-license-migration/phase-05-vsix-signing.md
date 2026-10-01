---
phase: 5
title: "VSIX signing (non-blocking cho v0.4.0)"
status: pending
priority: P3
effort: "4h (song song/sau P6; chờ Certum, mua cert do owner)"
dependencies: [1, 2]
---

# Phase 5: VSIX signing

<!-- Updated: Red Team + Validation Session 1 - V1/C1: KHÔNG chặn v0.4.0 (P6 không phụ thuộc P5) nhưng phụ thuộc P2 vì cùng sửa README/SECURITY.md; S3 attestation kiểm trên asset v0.3.0; S5 provenance cho .signed.vsix; C8 Certum = checkbox owner -->

## Overview
VSIX chưa ký: `release.yml` ký `.nupkg` bằng cosign keyless và attest `*.nupkg`, `*.vsix`, `dataguard-*.zip`; Visual Studio vẫn hiện "Digital Signature: None" vì cosign/attestation không đổi chữ ký OPC. Phase này **không chặn v0.4.0**: nếu chưa có cert lúc tag, phát hành **không ký** với tài liệu xác minh (`docs/release-verification.md`) — đó là nhánh mặc định. **Không mua/đăng ký gì; việc của owner là gate thủ công.**

## Requirements
- Functional: (1) `docs/release-verification.md` với lệnh **đã chạy thật** trên asset v0.3.0 (đã attest; `*.nupkg.sigstore.json` tồn tại) — vì dry-run không tạo attestation (`release.yml:700-703,822-826`); (2) nếu ký: script ký + script xác minh **ghim thumbprint/subject của signer trong repo**, bundle cosign keyless cho `.signed.vsix`; (3) `SECURITY.md:35-36,73-74` khớp thực tế (hứa attestation VSIX).
- Non-functional: không secret/khóa trong repo/CI; không dependency build; không hứa "Verified publisher"; **không bao giờ** dựa vào `.sha256` trần làm bằng chứng (S5).

## Architecture — ma trận quyết định
| Lựa chọn | Chi phí | Điều kiện | Người dùng thấy trong VS | Trạng thái |
|---|---|---|---|---|
| Tự ký (self-signed) | 0 | — | Marketplace từ chối; local báo không tin cậy | Loại |
| SignPath Foundation | 0 | Chỉ OSI **không** dual-license thương mại | — | Loại |
| Azure Artifact Signing | thấp | Không cho cá nhân ở Việt Nam | — | Loại |
| **Certum Open Source Code Signing** (SimplySign) | ~€49/năm [UNVERIFIED] | Cá nhân; **chưa rõ** có chấp nhận GPL+commercial | Có chữ ký; publisher "Open Source Developer, <tên>" [UNVERIFIED] | **Đề xuất, chờ Certum; không chặn** |
| Không ký + xác minh tài liệu hóa | 0 | — | "Digital Signature: None"; toàn vẹn kiểm ngoài băng tần | **Mặc định cho v0.4.0** |
| VS/VS Code Marketplace | 0 | Publisher verified | VS Marketplace counter-sign: UNVERIFIED | Song song |
| NuGet author-signing | cert | — | nuget.org đã repository-sign | Bỏ (YAGNI) |

<!-- Updated: Red Team + Validation Session 1 - S5: provenance cho bản ký -->
Luồng nếu ký (máy owner): CI dựng VSIX chưa ký + attestation → owner tải → ký bằng `sign` CLI với cert Certum (SimplySign cần OTP → không CI) → `verify-vsix-signature.ps1` (đối chiếu thumbprint/subject ghim trong `scripts/vsix-signer.json`) → `cosign sign-blob --yes --bundle DataGuard.VisualStudio-<ver>.signed.vsix.sigstore.json` (keyless, identity owner) → đính `*.signed.vsix` + bundle vào Release **như asset bổ sung**. Ký đổi byte ⇒ SHA-256/attestation CI chỉ đúng với bản chưa ký; tài liệu nói rõ hai bản.

## Related Code Files
<!-- Updated: Red Team + Validation Session 1 - S5 SECURITY.md, thumbprint pin; README link chuyển từ P6 sang P5 -->
- Create (chỉ nhánh ký): `scripts/sign-vsix.ps1`, `scripts/verify-vsix-signature.ps1` (`System.IO.Packaging.PackageDigitalSignatureManager`, `Add-Type -AssemblyName WindowsBase` — chắc chắn trên Windows PowerShell 5.1; pwsh 7 [UNVERIFIED]), `scripts/vsix-signer.json` (thumbprint + subject; không secret), `scripts/tests/vsix-signature/` fixtures (VSIX nhỏ chưa ký + ký + bị sửa 1 byte; nếu chưa có cert, fixture "ký" tạo bằng cert test tự ký chỉ để kiểm logic FAIL/PASS của script — ghi rõ).
- Create (mọi nhánh): `docs/release-verification.md` (SHA-256; `gh attestation verify <file> --repo thanhnt-sm/eco_support_net_oracle`; `cosign verify-blob --bundle <f>.sigstore.json --certificate-identity-regexp '^https://github.com/thanhnt-sm/eco_support_net_oracle/\.github/workflows/release\.yml@.*$' --certificate-oidc-issuer https://token.actions.githubusercontent.com <f>` (`release.yml:265-268`); mô tả trung thực người dùng thấy gì; hai bản ký/không ký).
- Modify: `docs/marketplace-publishing.md` (mục ký: ký → verify → publish; counter-sign chưa kiểm chứng), `SECURITY.md:35-36,73-74` (nêu VSIX chưa ký OPC; attestation cho bản CI; `.signed.vsix` có bundle cosign riêng), `README.md`/`README.vi.md` (+1 link tới `docs/release-verification.md` — **chuyển từ P6 sang đây** để không có link treo nếu P5 xong sau tag).
- Không sửa `release.yml`/`marketplace.yml`.

<!-- Updated: Red Team + Validation Session 1 - TDD: Tests-first -->
## Tests-first (Red → Green)
1. **Attestation syntax trên asset thật** (S3; GREEN phải đạt trước khi viết docs): `TMP=$(mktemp -d); gh release download v0.3.0 -p 'DataGuard.Core.0.3.0.nupkg*' -p 'dataguard-vscode-0.3.0.vsix' -p 'dataguard-0.3.0-win-x64.zip' -D "$TMP"`; `gh attestation verify "$TMP/dataguard-vscode-0.3.0.vsix" --repo thanhnt-sm/eco_support_net_oracle` → exit 0; `gh attestation verify "$TMP/dataguard-0.3.0-win-x64.zip" --repo thanhnt-sm/eco_support_net_oracle` → exit 0; `cosign verify-blob --bundle "$TMP/DataGuard.Core.0.3.0.nupkg.sigstore.json" --certificate-identity-regexp '^https://github.com/thanhnt-sm/eco_support_net_oracle/\.github/workflows/release\.yml@.*$' --certificate-oidc-issuer https://token.actions.githubusercontent.com "$TMP/DataGuard.Core.0.3.0.nupkg"` (tham số theo `release.yml:265-268`) → exit 0; VSIX VS từ `marketplace.yml` run gần nhất: `gh run download <id> -n <artifact>` + `gh attestation verify` → exit 0. Ghi lệnh chính xác (không theo trí nhớ) vào docs.
2. **Script xác minh (chỉ nhánh ký), viết test trước**: `pwsh -File scripts/tests/vsix-signature/run.ps1` (hoặc PowerShell 5.1): RED trước khi có script; GREEN = `verify-vsix-signature.ps1 -Path unsigned.vsix` → exit ≠0; `-Path tampered.vsix` → exit ≠0; `-Path signed.vsix -ExpectedThumbprint <sai>` → exit ≠0; `-Path signed.vsix` (thumbprint từ `scripts/vsix-signer.json`) → exit 0.
3. Secret scan: `git diff main...HEAD | grep -ciE "BEGIN (RSA|PRIVATE)|\.pfx|password"` → 0.

## Implementation Steps
1. **Chạy Tests-first #1 và ghi output** (đây là RED/GREEN thực cho tài liệu; nếu lệnh fail → sửa lệnh, không sửa docs theo phỏng đoán).
2. Viết `docs/release-verification.md` từ output #1; sửa `SECURITY.md`, `docs/marketplace-publishing.md`; thêm link README EN/VI. (Xong bước này = nhánh mặc định hoàn tất; v0.4.0 có thể tag.)
3. **Gate owner G-S1** (song song): hỏi Certum (GPL-3.0-only + bán giấy phép thương mại riêng có được không; bằng chứng; VSIX OPC/SHA-256/RSA; SimplySign không tương tác?). Ghi câu trả lời vào phase.
4. Nếu **có** và owner mua: viết fixtures + test (#2) trước, rồi `verify-vsix-signature.ps1`, `sign-vsix.ps1`, `vsix-signer.json`; ký VSIX nightly thử; cài trên VS 2022 sạch, chụp dialog; cosign bundle cho bản ký; cập nhật docs; đính asset bổ sung vào release hiện hành (v0.4.0 hoặc sau).
5. Nếu **không/không rõ**: ghi vào ADR (P6) kèm điều kiện mở lại (Certum chấp nhận; có pháp nhân → Azure Artifact Signing/SignPath).

## Success Criteria
<!-- Updated: Red Team + Validation Session 1 - C8: Certum là checkbox owner; kiểm bằng lệnh -->
- [ ] Tests-first #1: 4 lệnh verify exit 0 trên asset v0.3.0/marketplace; output dán vào `docs/release-verification.md` (ngày chạy).
- [ ] `SECURITY.md` không còn hứa điều chưa có (VSIX ký OPC); `grep -n "release-verification.md" README.md README.vi.md` ≥ 1 mỗi file.
- [ ] (Owner) Đã hỏi Certum — ngày: ____; trả lời: ____ / chưa → nhánh mặc định.
- [ ] Nhánh ký: Tests-first #2 4 ca đạt; `scripts/vsix-signer.json` có thumbprint; bundle cosign cho `.signed.vsix` verify được.
- [ ] Tests-first #3 = 0; trufflehog CI xanh.

## Risk Assessment
| Rủi ro | K×T | Giảm thiểu |
|---|---|---|
| `.signed.vsix` upload tay không có provenance (S5) | TB×TB | Thumbprint ghim trong repo + cosign bundle keyless; không dựa `.sha256` trần |
| Certum từ chối vì dual-license | TB×Thấp | Nhánh mặc định đã đủ; không chặn v0.4.0 |
| Người dùng kỳ vọng "Verified" nhưng thấy cảnh báo | Cao×Thấp | Tài liệu trung thực; không dùng "trusted" |
| Lệnh verify trong docs sai vì viết theo trí nhớ | TB×TB | Tests-first #1 bắt buộc chạy trên asset thật |
| Lộ khóa/OTP | Thấp×Cao | Ký chỉ trên máy owner |
| Rollback | — | Xóa asset `.signed.vsix` + bundle; docs/script revert |
