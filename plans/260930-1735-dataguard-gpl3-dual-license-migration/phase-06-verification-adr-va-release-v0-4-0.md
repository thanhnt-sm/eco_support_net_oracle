---
phase: 6
title: "Verification ADR va release v0.4.0"
status: pending
priority: P1
effort: "6h (+ ruleset/tag/push do owner)"
dependencies: [2, 3, 4]
---

# Phase 6: Verification ADR va release v0.4.0

<!-- Updated: Red Team + Validation Session 1 - V1: phụ thuộc [2,3,4], P5 song song/không chặn; S1 guard publish + ruleset tag; V3 ba sự kiện phân phối; S3 attestation post-tag; C7 ADR sửa tại chỗ; F4/F6/F7 lệnh kiểm chính xác -->

## Overview
Sửa ADR **tại chỗ** thành quyết định GPL-3.0 + Commercial, bump version, chạy toàn bộ gate, dry-run Release, thêm guard publish, rồi chuyển cho owner merge + tag `v0.4.0`. **Không phụ thuộc P5**: nếu VSIX chưa ký lúc tag → phát hành không ký kèm `docs/release-verification.md` (nếu P5 chưa xong phần docs, P6 tự viết mục xác minh tối thiểu trong release notes bằng lệnh đã chạy ở P5 Tests-first #1).

## Requirements
- Functional: ADR sửa tại chỗ (không "Superseded", không file mới, một hàng index); mọi gate xanh; dry-run Release xanh và artifact mang đúng giấy phép + notice; guard publish có test; post-tag verify attestation/OCI label/nupkg với fallback unlist + 0.4.1.
- Non-functional: commit tách cụm, conventional; không secret; không `git clean -fdx`; không push tag/`dry_run=false` để "thử".

## Architecture
<!-- Updated: Red Team + Validation Session 1 - V3/F1: ba sự kiện phân phối; S1 entry conditions -->
**Ba sự kiện phân phối công khai (theo thứ tự thời gian):**
1. **Push/PR** — `ci.yml:202-208` upload VSIX artifact (7 ngày); `marketplace.yml:4-6` chạy trên `pull_request` và attest VSIX (:126-130, :252-256) không có `if:`; dry-run `release.yml` giữ artifact 30 ngày. ⇒ Nội dung GPL + binary đầu tiên lộ **tại push** (P2 đã yêu cầu owner duyệt diff trước push).
2. **Merge vào `main`** — `installers.yml:9-14` chạy trên push main, job :350-352 xóa/tạo lại release `nightly` (:448-460) ⇒ **binary GPL công khai đầu tiên** = nightly. Chấp nhận (V3); snapshot nightly MIT cuối đã ghi ở CHANGELOG (P2 bước 8); merge càng sát tag càng tốt.
3. **Tag `v0.4.0`** — `release.yml:3-6` (`push.tags v*`) → release + NuGet (OIDC :649-672) + Docker (:744-748) + attestation (:700-703). Sau đó chỉ có unlist/0.4.1.

**Guard publish (S1):** hôm nay bất kỳ tag `v*` trên bất kỳ nhánh nào, hoặc `dry_run=false` từ bất kỳ nhánh nào, đều publish thật (`release.yml:553,651,703,746`: `if: github.event_name == 'push' || inputs.dry_run == false`); `gh api repos/thanhnt-sm/eco_support_net_oracle/rulesets` = `[]`. Điều kiện vào P6:
- (Owner) tạo ruleset trên `refs/tags/v*`: chỉ owner tạo tag; không bypass cho Actions. Kiểm: `gh api repos/thanhnt-sm/eco_support_net_oracle/rulesets -q 'length'` ≥ 1.
- (Agent) sửa 4 job publish: `if: ${{ (github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')) || (inputs.dry_run == false && github.ref == 'refs/heads/main') }}` — hoặc `environment: release` với required reviewer (owner chọn; `if` là mặc định vì không cần cấu hình repo). Thêm rule vào `scripts/check-workflow-policy.py` (viết test RED trước ở `scripts/tests/test_check_workflow_policy.py`): mọi job trong `release.yml` có `contents: write`/`id-token: write`/`packages: write` phải có `if` chứa `startsWith(github.ref, 'refs/tags/v')`.

**Docker licence label (handoff từ P1/advisor, 2026-09-30):** `docker/metadata-action` (`release.yml:772-786`) tự gắn `org.opencontainers.image.licenses` từ license repo GitHub (v0.3.0 = `MIT`; sau đổi LICENSE sẽ là `GPL-3.0`, không phải `GPL-3.0-only`) và `labels:` của build-push-action (:794) ghi đè `LABEL` Dockerfile. Sửa trong P6 (P6 sở hữu `release.yml`): thêm vào step metadata `labels: |` newline `org.opencontainers.image.licenses=GPL-3.0-only`. Assert #8 giữ nguyên. Câu hỏi mở: Observability 0.1.0 (ghim `MinVerSkip`) đã có trên nuget.org chưa — `--skip-duplicate` sẽ giữ bản cũ không licence.

**Version invariant (F7):** `ci.yml:189-197` assert `ExtensionVersion.Fallback` == manifest; `release.yml:491-500` ghi đè Fallback từ tag; `installers.yml:253-258` giữ Fallback đã commit. Derivation P6: `Select-String src/DataGuard.VisualStudio/ExtensionVersion.cs -Pattern 'Fallback = "([^"]+)"'` == `source.extension.vsixmanifest` `Identity/@Version` == `package.json` `version` == `scripts/build-extensions.ps1:26` == `0.4.0`.

## Related Code Files
<!-- Updated: Red Team + Validation Session 1 - C7 ADR sửa tại chỗ; S1 workflow guard; bỏ README link (chuyển P5) -->
- Modify (ADR, tại chỗ): `docs/decisions/ADR-20260930-dataguard-license-after-mit.md` — giữ tiêu đề file; đổi `Status: Decided` → `Status: Decided (revised 2026-09-30: GPL-3.0-only + Commercial)`; giữ "## Options considered" + "## Lens verdicts" + "## Evidence" của council; viết lại "## Decision"/"## Rationale": Option 1 (EULA + private) **bị bác** vì Anthropic "Claude for Open Source" Terms §2.3 yêu cầu OSI (đọc lại nguồn, ghi ngày) + owner ruling "PolyForm chỉ khi thỏa Anthropic, nếu không GPLv3 + Commercial"; ghi D1–D6, V1–V4; giới hạn trung thực (không cấm dùng thương mại/sao chép; không tắt fork; §10 không cấm AI training — P3 chỉ là tuyên bố ý định, **không** "đã khai báo DSM opt-out"; ≤v0.3.0 MIT vĩnh viễn); §7/thương mại **chưa qua luật sư** (V2); hoãn TDMRep/ai.txt, bot CLA (tiền đề S6), CLA text, VSIX signing (điều kiện mở lại); chuỗi sở hữu (S7): tuyên bố có ngày của owner rằng `thannt <thannt@thannts-MacBook-Pro.local>` và `thanhnt-sm <thanhnt.sm@gmail.com>` là một người (`git shortlog -sne`: 170/144; `.mailmap` P4), dependabot 11 commit chỉ lockfile, **liệt kê hash** 31 commit `Co-Authored-By: Claude` (`git log --format='%h %s' --grep='Co-Authored-By: Claude' HEAD`) và 3 `CommandCodeBot <noreply@commandcode.ai>`; tổng commit lấy từ `git rev-list --count HEAD` lúc viết (hôm nay 325), không hard-code; rủi ro USCO "thin copyright" cho phần AI viết; khuyến nghị owner (tùy chọn) bật `required_signatures` từ commit relicense; câu hỏi mở. Gate G0–G2 cũ: G0/G1 đóng bằng tuyên bố sở hữu + bảng kênh P1; G2 (luật sư) → điều kiện mở lại.
- Modify: `docs/decisions/README.md` (sửa **một hàng**: Decision → "GPL-3.0-only + Commercial từ v0.4.0; Option 1 bị bác (Anthropic OSI + owner); ≤v0.3.0 MIT", Status `Decided (revised)`), `.github/workflows/release.yml` (4 `if` guard :553,651,703,746), `scripts/check-workflow-policy.py` + `scripts/tests/test_check_workflow_policy.py` (rule mới), `CHANGELOG.md` (heading :6 "Unreleased — next release must be tagged v0.3.0" → `[0.3.0] — 2026-09-30`; mở `[0.4.0]` chứa mục P2), `plans/ACTIVE_SESSION_REGISTER.md` (mục phiên mới).
- Bump v0.4.0: `src/DataGuard.VisualStudio/ExtensionVersion.cs:19`, `src/DataGuard.VisualStudio/source.extension.vsixmanifest:4`, `src/DataGuard.VSCode/package.json:5`, `src/DataGuard.VSCode/package-lock.json:3,9`, `scripts/build-extensions.ps1:26`. **Không** đổi `MIN_CLI_VERSION` (`src/DataGuard.VSCode/src/ide-safe-contract.ts:9`). NuGet/CLI version từ tag qua MinVer.
- `docs/decisions/` hiện **untracked** → commit ADR đã sửa + README trong một commit. Không tạo `ADR-...-gpl3-dual-license.md`.

<!-- Updated: Red Team + Validation Session 1 - TDD: Tests-first (pre-merge / post-tag) -->
## Tests-first (Red → Green)
**Pre-merge (chạy trên nhánh, GREEN trước khi merge):**
1. Guard policy: viết test RED trong `scripts/tests/test_check_workflow_policy.py` (fixture release.yml không có `startsWith(github.ref, 'refs/tags/v')` → FAIL) → `python3 -m unittest scripts/tests/test_check_workflow_policy.py -v` RED → sửa script + `release.yml` → GREEN; `python3 scripts/check-workflow-policy.py` exit 0.
2. Licence detection (F4; `gh repo view --json licenseInfo` trả null — không dùng): `gh api "repos/thanhnt-sm/eco_support_net_oracle/license?ref=feat/gpl3-dual-license" -q .license.spdx_id` → `GPL-3.0` (RED hôm nay: `MIT` trên main).
3. Nuspec set (F6): `TMP=$(mktemp -d); dotnet pack DataGuard.CrossPlatform.slnf -c Release -o "$TMP/nupkg"` (giống `release.yml:114-118`) → `ls "$TMP"/nupkg/*.nupkg | wc -l` = 13; danh sách khớp P2 Tests-first #2; mỗi nuspec đúng 1 `<license>`; Contracts = MIT.
4. Version invariant (F7): `pwsh -c '$a=(Select-String src/DataGuard.VisualStudio/ExtensionVersion.cs -Pattern "Fallback = \"([^\"]+)\"").Matches[0].Groups[1].Value; $b=([xml](Get-Content src/DataGuard.VisualStudio/source.extension.vsixmanifest)).PackageManifest.Metadata.Identity.Version; $c=(Get-Content src/DataGuard.VSCode/package.json | ConvertFrom-Json).version; "$a $b $c"'` → `0.4.0 0.4.0 0.4.0`; `grep -n '"0.4.0"' scripts/build-extensions.ps1` = 1 dòng; `grep -n "0.3.0" src/DataGuard.VSCode/src/ide-safe-contract.ts` vẫn có (MIN_CLI_VERSION).
5. Notice trong artifact (P1/P2 assert tái chạy trên artifact dry-run): `gh run download <dry-run id>` → `unzip -l dataguard-0.4.0-win-x64.zip | grep -cE "THIRD-PARTY|ADDITIONAL|LICENSE"` = 3; Cli nupkg = 3; VS VSIX/VS Code VSIX `THIRD-PARTY` = 1 mỗi cái; nuspec 13 gói đúng.
6. Sweep còn sót: `python3 scripts/check-license-consistency.py` exit 0; `git grep -n PolyForm -- . ':!CHANGELOG.md' ':!docs/decisions' ':!plans' ':!research' ':!brainstorm'` = 0; `git grep -nE "COMMERCIAL-LICENSE\.md|\bCLA\.md" -- . ':!plans'` = 0; `git ls-files NOTICE COMMERCIAL-LICENSE.md CLA.md | wc -l` = 0 (không file root mới; `THIRD-PARTY-NOTICES.md`/`AI-USAGE-NOTICE.md` trong `docs/legal/` là hợp lệ).
7. Build/test: `dotnet restore DataGuard.sln`; `dotnet build DataGuard.sln -c Release` (0 warning); `dotnet test` cho `tests/DataGuard.Core.Tests`, `tests/DataGuard.GoldenCorpus.Tests`, `tests/DataGuard.Analyzers.Tests`, `tests/DataGuard.VisualStudio.Tests`; `python3 scripts/check-nuget-licences.py`; `python3 -m unittest discover -s scripts/tests -v`; `./scripts/verify_docs_sync.sh`; `scripts/assert-vsix.ps1 -VsixPath <vsix> -ExpectedVersion 0.4.0`.
**Post-tag (chạy trong 1 giờ sau tag; fail → unlist + cắt 0.4.1):**
8. `gh attestation verify dataguard-0.4.0-win-x64.zip --repo thanhnt-sm/eco_support_net_oracle` exit 0 (và vscode vsix, 1 nupkg); `cosign verify-blob --bundle DataGuard.Core.0.4.0.nupkg.sigstore.json --certificate-identity-regexp '^https://github.com/thanhnt-sm/eco_support_net_oracle/\.github/workflows/release\.yml@.*$' --certificate-oidc-issuer https://token.actions.githubusercontent.com DataGuard.Core.0.4.0.nupkg` exit 0; `docker buildx imagetools inspect ghcr.io/thanhnt-sm/eco_support_net_oracle:0.4.0 --format '{{json .}}' | grep -o '"org.opencontainers.image.licenses":"[^"]*"'` = `GPL-3.0-only` (tag `0.4.0` theo `type=semver,pattern={{version}}` `release.yml:781`); `docker run --rm --entrypoint ls ghcr.io/thanhnt-sm/eco_support_net_oracle:0.4.0 /app | grep -c THIRD-PARTY` = 1; `gh api "repos/thanhnt-sm/eco_support_net_oracle/license" -q .license.spdx_id` = `GPL-3.0`; nếu nuget push thành công: `curl -s https://api.nuget.org/v3/registration5-semver1/dataguard.core/0.4.0.json | grep -o '"licenseExpression":"[^"]*"'` = `GPL-3.0-only`.

## Implementation Steps
1. **Chạy các lệnh RED (Tests-first #1, #2, #3, #4, #6) và ghi kết quả.** Điều kiện vào: P2/P3/P4 GREEN; owner xác nhận ruleset tag (`gh api .../rulesets` ≥ 1) — nếu chưa, ghi BLOCKED-ruleset, các bước 2–7 vẫn làm, **bước 8 không làm**.
2. Guard publish: test RED → sửa `check-workflow-policy.py` → sửa 4 `if` trong `release.yml` → GREEN (#1).
3. Sửa ADR tại chỗ + `docs/decisions/README.md` (nội dung ở Related Code Files); commit `docs(adr): revise licence decision to GPL-3.0-only + commercial`.
4. Bump version (danh sách); CHANGELOG heading; `grep -rn "0\.3\.0" scripts .github src/DataGuard.VSCode/src tests` — mỗi hit còn lại phải là MIN_CLI_VERSION/lịch sử. Chạy #4.
5. Chạy #3, #6, #7 → GREEN.
6. Push nhánh, mở PR (P2 đã có owner duyệt diff trước push; **lần push này là sự kiện phân phối #1**). Chờ CI xanh (gồm step gate mới, licence allow-list, workflow-policy).
7. Dry-run: `gh workflow run release.yml --ref feat/gpl3-dual-license -f tag=v0.4.0 -f dry_run=true` (**bắt buộc `--ref`**; `release.yml:435` `MinVerVersionOverride`; tag chưa cần tồn tại). **Không push tag `v*` nào, không `dry_run=false`.** Chạy #5 trên artifact dry-run. Tiêu chí: artifact đúng licence + notice; không job publish chạy (`gh run view <id> --json jobs -q '.jobs[] | select(.conclusion=="skipped") | .name'` gồm 4 job publish). **Không** kỳ vọng attestation ở dry-run (`release.yml:822-826`).
8. **Gate owner (merge)**: checklist — README "chưa qua luật sư" + email placeholder có mặt; #2 = GPL-3.0; #5 đạt; snapshot nightly MIT đã ở CHANGELOG — **chạy lại** `gh release view nightly --json publishedAt,assets -q '.publishedAt, (.assets[] | "\(.name) \(.digest)")'` ngay trước merge, nếu digest đổi thì amend CHANGELOG (V3). Merge → `installers.yml` publish nightly GPL (sự kiện #2, chấp nhận). Kiểm `gh api .../license` trên main = GPL-3.0.
9. **Gate owner (tag)** — ngay sau merge: kiểm NuGet key/OIDC (v0.3.0 từng 403 — `NUGET_USER`/`NUGET_API_KEY` secrets), `git tag v0.4.0 && git push origin v0.4.0` (sự kiện #3). **Post-tag trong 1 giờ**: chạy #8; bất kỳ lệnh nào fail → `gh release edit v0.4.0 --draft` / unlist nuget (`dotnet nuget delete` = unlist) / xóa tag ghcr → sửa → `v0.4.1`. Bài học v0.3.0: workflow fail giữa chừng → xóa tag + release lỗi rồi tag lại **chỉ khi chưa có asset công khai**.
10. Sau phát hành: release notes nêu GPL-3.0-only + Commercial + MIT ≤v0.3.0 + link `docs/legal/*`; CHANGELOG liệt kê kênh đã phát hành theo MIT (Releases v0.1.0–v0.3.0, nightly ≤ snapshot, ghcr tags cũ); `plans/ACTIVE_SESSION_REGISTER.md`.

## Success Criteria
<!-- Updated: Red Team + Validation Session 1 - kiểm bằng lệnh; bỏ "attestation tạo được" khỏi dry-run -->
- [ ] Tests-first #1–#7 exit 0 trước merge; output dán vào PR body.
- [ ] ADR sửa tại chỗ, có tuyên bố sở hữu có ngày, danh sách hash co-author, `git rev-list --count`; `docs/decisions/README.md` một hàng; cả hai đã commit (`git status --porcelain docs/decisions` rỗng).
- [ ] `gh api repos/thanhnt-sm/eco_support_net_oracle/rulesets -q length` ≥ 1 (owner) trước bước 9.
- [ ] Dry-run xanh; 4 job publish `skipped`; artifact #5 đạt.
- [ ] Post-tag #8 toàn bộ exit 0 trong 1 giờ, hoặc đã unlist + mở 0.4.1.

## Risk Assessment
<!-- Updated: Red Team + Validation Session 1 - S1/S3/F7 -->
| Rủi ro | K×T | Giảm thiểu / Rollback |
|---|---|---|
| Tag `v*` nhầm/`dry_run=false` từ nhánh lạ publish thật (S1) | TB×Cao | Ruleset tag (owner) + `if` guard có test; không bao giờ tag để thử |
| Attestation/OCI label chỉ kiểm được sau tag (S3) | Cao×TB | Lệnh đã test trên v0.3.0 (P5 #1); post-tag 1 giờ + unlist + 0.4.1 |
| Version lệch Fallback/manifest/package.json (F7) | TB×Thấp | Derivation #4; `ci.yml:189-197` bắt |
| Merge publish nightly GPL trước tag (V3) | Chắc chắn×Thấp | Chấp nhận; snapshot MIT cuối; merge sát tag |
| Đổi nhầm `MIN_CLI_VERSION` | Thấp×TB | Ghi "không đổi"; `ide-safe-contract.test.ts:33` fail |
| NuGet push 403 (lặp lại v0.3.0) | TB×TB | `continue-on-error` đã có (`release.yml:653`); kiểm secret trước tag; push tay từ artifact nếu cần |
| Sót MIT/PolyForm | TB×Thấp | #6 + gate |
