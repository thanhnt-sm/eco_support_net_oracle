---
title: "DataGuard: chuyen MIT sang dual license GPL-3.0-only + Commercial (tu v0.4.0)"
description: "Chuyển giấy phép DataGuard từ MIT sang GPL-3.0-only kèm giấy phép thương mại (thỏa thuận theo licensee); sửa tại chỗ ADR EULA+private repo; contributor agreement trong CONTRIBUTING, thông báo AI/crawl, ký VSIX không chặn release."
status: pending
priority: P1
effort: 26h
branch: feat/gpl3-dual-license
tags: [license, gpl-3.0, dual-license, legal, signing]
blockedBy: []
blocks: []
created: 2026-09-30
---

# DataGuard: chuyen MIT sang dual license GPL-3.0-only + Commercial (tu v0.4.0)

## Overview

Quyết định owner (chốt, 2026-09-30): dual license **GPL-3.0-only** + **giấy phép thương mại** (thỏa thuận theo từng licensee) cho công ty muốn tích hợp vào sản phẩm closed-source. Lý do: owner muốn PolyForm Noncommercial; Anthropic "Claude for Open Source" Terms §2.3 yêu cầu giấy phép OSI; owner đã nói "PolyForm chỉ khi thỏa Anthropic, nếu không thì GPLv3 + Commercial". Plan **sửa tại chỗ** `docs/decisions/ADR-20260930-dataguard-license-after-mit.md` (Option 1 EULA + private bị bác).

Giới hạn phải nói thẳng ở README/FAQ:
- GPL-3.0 **không** cấm dùng thương mại/sao chép; copyleft chỉ khi *phân phối* tác phẩm dẫn xuất. Repo public **không tắt fork** được (GitHub ToS D.5). ≤v0.3.0 và binary đã phát hành **vẫn MIT vĩnh viễn**.
- GPLv3 §10 cấm "further restrictions" ⇒ thông báo AI/TDM chỉ là bảo lưu quyền (ý định), không phải điều kiện cấp phép.
- <!-- Updated: Red Team + Validation Session 1 - V2 --> Văn bản §7 additional permission và điều khoản thương mại **chưa qua luật sư** — owner chấp nhận rủi ro; README/FAQ phải nói rõ. 31/325 commit đồng tác giả Claude (USCO "thin copyright" — chỉ nêu rủi ro).

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Compatibility gate GPL-3.0](./phase-01-compatibility-gate-gpl-3-0.md) | Completed |
| 2 | [Legal text va metadata migration](./phase-02-legal-text-va-metadata-migration.md) | Completed |
| 3 | [AI crawl notice rewrite](./phase-03-ai-crawl-notice-rewrite.md) | Pending |
| 4 | [Contribution CLA](./phase-04-contribution-cla.md) | Pending |
| 5 | [VSIX signing](./phase-05-vsix-signing.md) | Pending |
| 6 | [Verification ADR va release v0.4.0](./phase-06-verification-adr-va-release-v0-4-0.md) | Pending |

<!-- Updated: Red Team + Validation Session 1 - effort thực tế: P1 3h, P2 12h, P3 2h, P4 2h, P5 4h (song song, không chặn), P6 6h (+ ruleset/tag owner) -->

## Dependencies

<!-- Updated: Red Team + Validation Session 1 - V1: P6 phụ thuộc [2,3,4]; P5 song song/không chặn -->
P1 (BLOCKING) → P2 → P3; P1 → P4; P2 → P5 (cùng sửa README/SECURITY.md nên phải sau P2; song song với P3/P4); **{P2,P3,P4} → P6**. **P5 không chặn v0.4.0**: nếu chưa ký lúc tag → phát hành không ký kèm `docs/release-verification.md`. Sở hữu file: P2 = LICENSE, `docs/legal/*`, csproj/props, Dockerfile, README/docs/grants, gate script; P3 = `.github/copilot-instructions.md`, `docs/legal/AI-USAGE-NOTICE.md`, mục AI trong README (sau P2); P4 = CONTRIBUTING*, PR template, `docs/contributing.md`, `.mailmap`; P5 = `docs/release-verification.md`, `docs/marketplace-publishing.md`, `SECURITY.md`, `scripts/*vsix*.ps1`, link README; P6 = ADR + index, version bump, CHANGELOG heading, `release.yml` guard, `check-workflow-policy.py`. Agent song song → `isolation: worktree`.

## Phát hiện then chốt (P1, kiểm chứng 2026-09-30) và quyết định D1–D6

<!-- Updated: Red Team + Validation Session 1 - thay bằng D1–D6 (ck-predict, owner ủy quyền) -->
- Graph: MIT 135, Apache-2.0 12, PostgreSQL 1 — tương thích. **Oracle.ManagedDataAccess.Core** và **Microsoft.Data.SqlClient.SNI.runtime** không tương thích nguyên trạng và được phân phối qua **mọi kênh** (Cli tool nupkg `PackAsTool` — có đẩy nuget.org; zip RID; ghcr image; VSIX) mà chưa kênh nào có văn bản giấy phép.
- **D1** `GPL-3.0-only`. **D2** bundle Oracle/SNI + §7 additional permission (mẫu FSF; đích danh Oracle ODP.NET, MS SNI, Visual Studio, VS Code + mệnh đề chung; bỏ mệnh đề library-source); nguyên văn Oracle FDHUT + SNI licence trong `docs/legal/THIRD-PARTY-NOTICES.md`, sao chép vào mọi kênh. **D3** `DataGuard.Contracts` = MIT (nằm trong binary khách; Core đọc reflection `ManualContractSource.cs:33-83`). **D4** gói analyzer chỉ build-time (`IncludeBuildOutput` `Analyzers.csproj:11` hiện `true`), SqlClassification/LanguageServer `IsPackable=false` — điều tra tests-first, có fallback FAQ. **D5** AGPL cho Host: không (reopen khi có đối thủ SaaS). **D6** central `PackageLicenseExpression=GPL-3.0-only` + `Authors`/`Copyright` trong `Directory.Build.props`; không `PackageLicenseFile`/nhánh Y/Z (nuget.org từ chối `WITH LicenseRef-…`, HTTP 400); không header `.cs`; **không file root mới** — `docs/legal/{THIRD-PARTY-NOTICES,ADDITIONAL-PERMISSIONS}.md`; zero edit guard scripts.

## Quy tắc chung

- Làm trên `feat/gpl3-dual-license`. <!-- Updated: Red Team + Validation Session 1 - F1/V3 --> **Điểm không đảo ngược sớm nhất = `git push`/PR** (VSIX artifact + attestation trên PR); merge → nightly GPL (chấp nhận, snapshot nightly MIT cuối vào CHANGELOG); tag → release/NuGet/ghcr. Owner duyệt diff trước push; không push tag `v*`/`dry_run=false` để thử.
- Mọi phase có mục **Tests-first (Red → Green)**; Implementation Steps bắt đầu bằng chạy lệnh RED và ghi kết quả. Lệnh CI dùng `python3`.
- Verify: `dotnet restore/build DataGuard.sln`, test bị ảnh hưởng, `./scripts/verify_docs_sync.sh`, licence gate, `check-workflow-policy.py` (chi tiết P6).

## Validation Log

<!-- Updated: Red Team + Validation Session 1 - quyết định owner V1–V4 -->
| # | Quyết định owner (2026-09-30) | Áp dụng |
|---|---|---|
| V1 | Giữ 6 phase; P6 phụ thuộc [2,3,4]; P5 không chặn. Giữ gate SPDX nhưng sửa: `\bMIT\b`, một `FILE_LIST` dùng chung, allow-list `path | marker | justification` kiểu `check-nuget-licences.py`, test âm (`[MIT](LICENSE)`, `| MIT |`, badge `license-MIT`), không bắn "MySqlConnector (MIT" | plan.md, P2, P5, P6 |
| V2 | Không gate luật sư; ship v0.4.0 với §7 mẫu FSF; README/FAQ nói rõ chưa qua luật sư; luật sư = điều kiện mở lại | P1, P2, P6 |
| V3 | Snapshot asset+SHA-256 nightly MIT cuối vào CHANGELOG/ADR trước merge; merge sát tag; chấp nhận nightly GPL; P6 nêu 3 sự kiện phân phối | P2, P6 |
| V4 | README chỉ "liên hệ <email placeholder>; điều khoản theo licensee"; không `COMMERCIAL-LICENSE.md`/`CLA.md`; CONTRIBUTING một đoạn contributor agreement; bot hoãn với tiền đề S6 | P2, P4 |

## Red Team Review

<!-- Updated: Red Team + Validation Session 1 - 4 reviewer; các finding trùng gộp một hàng -->
| # | Finding | Sev | Disposition | Áp dụng |
|---|---|---|---|---|
| S1 | Tag `v*`/`dry_run=false` từ bất kỳ nhánh nào publish thật (`release.yml:553,651,703,746`); rulesets = [] | Critical | Accept: ruleset tag (owner) + `if` guard + rule policy có test | P6 |
| S2/V3 | Merge main đã publish binary (`installers.yml` nightly; `marketplace.yml` attest trên PR) | High | Accept | P6 |
| S3/F3/A3 | Dry-run không tạo attestation (`release.yml:700-703,822-826`) | High | Accept: test verify trên asset v0.3.0; post-tag 1h + unlist + 0.4.1; bỏ "attestation tạo được" khỏi dry-run | P5, P6 |
| S4/A2 | Oracle/SNI vào nuget/zip/ghcr/VSIX không kèm licence; Dockerfile không có label licenses | High | Accept: bảng kênh + notice mọi kênh + LABEL + assert | P1, P2 |
| S5 | `.signed.vsix` upload tay không provenance; SECURITY.md hứa attestation | High | Accept: pin thumbprint, cosign bundle, sửa SECURITY.md | P5 |
| S6 | "contents: write chỉ cho nhánh" không tồn tại | Medium | Accept: bot hoãn, tiền đề (a)–(d) | P4 |
| S7/A7 | 170/144 commit hai identity; 31 Claude co-author; 3 CommandCodeBot | Medium | Accept: tuyên bố sở hữu + hash list trong ADR; `.mailmap`; `git rev-list --count` | P4, P6 |
| S8/F2/A8/C5 | PackageLicenseFile/nhánh Y-Z; Authors per-csproj | High | Accept-modified → D6; test 1 licence element/nupkg, Contracts không chứa GPL | P2 |
| F1 | Irreversibility đặt sai chỗ (push/PR, không phải merge) | High | Accept: duyệt diff trước push; rollback sửa | P2, P6 |
| F4 | `gh repo view --json licenseInfo` = null | Medium | Accept: REST `license?ref=` ; chuyển P2 → P6 | P6 |
| F6 | Pack bằng `DataGuard.CrossPlatform.slnf`; Cli tool có lên nuget dù comment :397 | Medium | Accept: 13 nupkg liệt kê | P2, P6 |
| F7 | Invariant version ở `ci.yml:189-197`, không phải `assert-vsix.ps1:64` | Medium | Accept: derivation chính xác | P6 |
| A1/D4 | Analyzers `IncludeBuildOutput=true` rơi DLL vào bin khách | High | Accept: D4 tests-first + fallback FAQ | P1, P2 |
| A4/F5/V1 | Gate regex bắn nhầm/quá hẹp | Medium | Accept: regex + allow-list + test âm | P2 |
| A5 | copilot-instructions.md không phải bề mặt TDM | Medium | Accept: pointer 2 dòng; notice ở README + docs/legal; ADR không claim DSM opt-out | P3, P6 |
| A6 | 15 nupkg v0.3.0 (không 13); 10 project thiếu metadata (không 7) | Medium | Accept: liệt kê đủ | P1, P2 |
| C1 | P5 chặn release | Critical | Accept-modified: giữ P5, P6 không phụ thuộc | plan.md, P5, P6 |
| C2 | Gộp còn 3 phase | High | **Reject** (owner giữ 6) | — |
| C3 | Gate quá phức tạp | High | Accept-modified: giữ, sửa regex/scope; một call trong `verify_docs_sync.sh`; `python3` | P2 |
| C4/V2/V4 | COMMERCIAL-LICENSE/CLA draft không luật sư | High | Accept: không commit; wording V2/V4 | P2, P4 |
| C6 | File root mới cần nới guard | Medium | Accept: không file root mới | P2 |
| C7 | docs/decisions untracked; nghi thức Superseded | Medium | Accept: sửa ADR tại chỗ, một hàng index | P6 |
| C8 | Tiêu chí không kiểm bằng lệnh (gh licence, Certum) | Medium | Accept: Certum = checkbox owner; đếm bằng lệnh | P5, P6 |

## Câu hỏi mở

<!-- Updated: Red Team + Validation Session 1 - bỏ D1–D3/luật sư/bot/cấu trúc (đã chốt) -->
1. Certum có chấp nhận cert Open Source cho GPL-3.0-only kèm bán giấy phép thương mại? (P5, owner hỏi)
2. VS Marketplace có counter-sign VSIX? SimplySign có tự động hóa CI? (P5)
3. nuget.org có cần gì khác ngoài `GPL-3.0-only`/`MIT` expression không? (kiểm khi push thật, P6 post-tag)
4. Email liên hệ thương mại, mô hình giá, pháp nhân/luật áp dụng của owner (placeholder trong README).
5. D4: lật `IncludeBuildOutput` có phá analyzer loading trong VS (`CHANGELOG.md:57`) hay quick-fix compile? (P2 tests-first quyết)
6. Tag cũ v0.1.0–v0.2.1: binary/ghcr image dưới MIT — có liệt kê/giữ nguyên? (P6 bước 10 chỉ liệt kê)
7. Điều khoản output của CommandCode (3 commit `CommandCodeBot`) — có ràng buộc bản quyền không?
8. Ngưỡng định lượng Anthropic chưa đạt (0 star, chưa có trên NuGet) — không claim đủ điều kiện.
9. `.mailmap` không nằm trong `ROOT_FILE_PATTERN` (`anti_garbage_guard.sh:6`) — owner có nới pattern không? (P4 bước 4)
10. Checkbox PR template trỏ CONTRIBUTING (không file CLA) — giữ hay bỏ? (P4, mặc định giữ)
