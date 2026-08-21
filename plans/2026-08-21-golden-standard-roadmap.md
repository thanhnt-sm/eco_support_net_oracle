# Golden Standard Roadmap — DataGuard Workspace → Bộ tiêu chuẩn vàng tái sử dụng

> **Mục đích**: biến workspace này thành (1) sản phẩm đạt mọi tiêu chuẩn của GitHub / NuGet.org / VS & VS Code Marketplace / môi trường doanh nghiệp–ngân hàng khó tính nhất, và (2) **framework/khuôn mẫu** owner dùng lại cho các sản phẩm khác.
> **Cơ sở**: inventory trực tiếp 2026-08-21 (post hardening `aa863f9`) — mọi mục "Đã có" đều verify bằng lệnh; mục "Thiếu" là kết quả `ls`/`grep` thực tế.
> **Nguyên tắc**: mỗi mục phải có lệnh verify + điều kiện (nhiều mục cần owner secrets/decision — không tự ý làm).

---

## 1. Inventory hiện tại (verified 2026-08-21)

### Đã có (mạnh)

| Trục | Bằng chứng |
|---|---|
| CI: build/test + TruffleHog + Docker + CodeQL (5 custom queries) | `.github/workflows/ci.yml`, `.github/codeql/queries/*.ql` |
| Release: cosign + build provenance + NuGet Trusted Publishing | `.github/workflows/release.yml` (`NuGet/login`, `sigstore/cosign-installer`, `attest-build-provenance`) |
| Marketplace: VSIX build + attestation | `.github/workflows/marketplace.yml` |
| Dependabot (actions + nuget, grouped, weekly) | `.github/dependabot.yml` |
| Pin actions theo SHA full | mọi `uses:` đều SHA-pinned (verify grep) |
| Permissions tối thiểu trong workflow | `ci.yml:13,209` |
| SECURITY.md, CONTRIBUTING (vi/en), CHANGELOG, LICENSE MIT, README vi/en | `ls *.md` |
| .editorconfig, .gitattributes, .gitignore, .githooks, .env.example, Dockerfile, .dockerignore | `ls -a` |
| SBOM + checksum trong release (cần verify run thật) | release.yml artifacts |
| MinVer versioning từ git tag | `version` in `0.1.1-alpha.0.38+<sha>` |
| Test: 129 pass, coverage gate-in-progress (Core 50.3%) | phiên hardening hôm nay |

### Thiếu (gap → việc cần làm)

| # | Gap | Chuẩn nào đòi hỏi |
|---|---|---|
| G1 | Không CODEOWNERS | GitHub community standards + review enforcement |
| G2 | Không ISSUE_TEMPLATE / PULL_REQUEST_TEMPLATE | GitHub community standards |
| G3 | Không CODE_OF_CONDUCT.md / SUPPORT.md / FUNDING.yml | GitHub community profile 100% |
| G4 | Không OSS Scorecard workflow + badge | OpenSSF best practice, "chứng chỉ" tự động cao nhất của GitHub |
| G5 | Coverage gate chưa chặn CI (chưa fail dưới ngưỡng) | Engineering chuẩn doanh nghiệp |
| G6 | snupkg (symbol packages) chưa publish | NuGet.org chuẩn cao |
| G7 | Branch protection / signed commits / tag protection — cấu hình server-side, không làm từ repo được | GitHub security posture |
| G8 | Marketplace publish 2 extension bị chặn bởi owner secrets (PAT, publisher verify) | VS/VSCode Marketplace |
| G9 | NuGet Trusted Publishing cần owner secret `NUGET_USER` migrate trước 01/11/2026 | NuGet.org |
| G10 | Không có API docs public (DocFX/docfx.metadata) + XML docs chưa full | NuGet/enterprise |
| G11 | Testcontainers (DB thật) + benchmark còn thiếu | Banking-grade QC |
| G12 | Warning debt 4×SA1000 trong test project | 0-warning shipping standard |

---

## 2. Lộ trình theo cấp chuẩn

### Cấp 1 — GitHub community standards + badges (tự động, không cần secret)

| Việc | Verify | Trạng thái |
|---|---|---|
| CODEOWNERS (`@thanhnt-sm`) | `git ls-files .github/CODEOWNERS` | ⬜ quick-win phiên này |
| ISSUE_TEMPLATE: bug.yml, feature.yml + config.yml | mở Settings→Community standards thấy ✅ | ⬜ quick-win |
| PULL_REQUEST_TEMPLATE.md | PR mới tự có checklist | ⬜ quick-win |
| CODE_OF_CONDUCT.md (Contributor Covenant) + SUPPORT.md | community profile ✅ | ⬜ quick-win |
| Scorecard workflow (`ossf/scorecard-action`) + badge README | Scorecard ≥ 7.0 mục tiêu | ⬜ quick-win (badge hiện sau run đầu) |
| Coverage badge (reportgenerator + badge trong CI) | README badge live | ⬜Phase sau (cần thêm step CI) |

### Cấp 2 — GitHub security & automation (tối đa tự động hóa)

| Việc | Điều kiện | Trạng thái |
|---|---|---|
| Branch protection: require PR review (CODEOWNERS), require status checks (CI + CodeQL), require signed commits, require linear history | owner thao tác Settings→Branches (không thể từ repo) | ⬜ owner |
| Tag protection rule `v*` + release chỉ từ tag | owner Settings→Tags | ⬜ owner |
| CI coverage gate: `--collect:"XPlat Code Coverage"` + fail khi union < 45% (nâng dần lên 60%) | thêm step CI | ⬜Phase sau |
| Lint/format gate: `dotnet format --verify-no-changes` (loại trừ nợ SA1000 hiện có qua .editorconfig suppression hoặc fix nốt) | fix 4 warning còn lại | ⬜Phase sau |
| Renovate (tùy chọn, mạnh hơn Dependabot cho lockfile) | owner bật app | ⬜ optional |
| Auto-changelog từ conventional commits (git-cliff hoặc GTs) | thêm step release | ⬜Phase sau |

### Cấp 3 — NuGet.org chuẩn cao nhất

| Việc | Điều kiện | Trạng thái |
|---|---|---|
| Trusted Publishing `NUGET_USER` migrate (hạn 01/11/2026) | owner secret | ⬜ owner-blocked |
| snupkg publish đầy đủ 10 packages | sửa release.yml thêm `-p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg` | ⬜Phase sau |
| Package metadata chuẩn mọi package: RepositoryUrl/LICENSE/tags/readme icon | audit từng csproj | ⬜Phase sau |
| API docs (DocFX) publish GitHub Pages | thêm workflow | ⬜Phase sau |
| XML docs full cho public API Core | build `/warnaserror:CS1591` check | ⬜Phase sau |

### Cấp 4 — VS & VS Code Marketplace

| Việc | Điều kiện | Trạng thái |
|---|---|---|
| Publisher verify + `VSCE_PAT` publish VS Code extension | owner secrets — runbook đã có `docs/marketplace-publishing.md` | ⬜ owner-blocked |
| `VS_MARKETPLACE_PAT` + VS 2022 Experimental Instance smoke → VSIX publish | owner (Windows CI) | ⬜ owner-blocked |
| VSIX signing certificate | owner cert | ⬜ owner-blocked |

### Cấp 5 — Banking-grade posture (không claim certification khi chưa audit độc lập)

| Việc | Trạng thái |
|---|---|
| Offline-first profile docs (snapshot default, zero telemetry, redaction) — đã có từ hardening | ✅ phần lớn |
| Least-privilege DB role runbook (read-only + EXECUTE) | ⬜ docs |
| Testcontainers cho Oracle/SqlServer thật | ⬜ P2 backlog |
| Benchmark + performance regression gate (bỏ claim "~ms" tới khi có số) | ⬜ P2 backlog |
| Zero-warning shipping code | ⬜ 4 SA1000 test-only còn lại |

### Cấp 6 — Framework tái sử dụng ("bộ khuôn vàng")

Mục tiêu: owner copy workspace này cho sản phẩm mới và có ngay toàn bộ nền. Việc:
1. Trích cấu trúc lặp lại thành **template repo checklist**: `docs/golden-standard/TEMPLATE_CHECKLIST.md` (mọi file chuẩn + cấu hình server-side + secrets cần thiết).
2. Document hóa các pattern đáng giá: SHA-pinned actions, permission tối thiểu, trusted publishing, exit-code contract, test đỏ→xanh, golden corpus, telemetry zero-egress, credential zero-trust — mỗi pattern 1 trang `docs/golden-standard/PATTERNS.md`.
3. CI tự kiểm chuẩn: workflow `standards-audit.yml` kiểm repo tự thân (CODEOWNERS tồn tại, actions pinned, permissions set…) — "repo tự chứng minh mình đạt chuẩn".

---

## 3. Thứ tự thực thi đề xuất

1. **Ngay (quick-wins, không cần secret)**: G1–G4 + workflow standards-audit (Cấp 1 + khởi đầu Cấp 6).
2. **Owner 5 phút trên GitHub Settings**: G7 branch/tag protection (Cấp 2).
3. **Phiên kế tiếp**: coverage gate + format gate + snupkg + DocFX (Cấp 2–3).
4. **Khi owner sẵn sàng secrets**: G8/G9 publish (Cấp 3–4).
5. **Liên tục**: nâng coverage → 60%, Testcontainers, benchmark (Cấp 5).

## 4. Nguyên tắc bất biến khi triển khai

- Evidence-first: mọi mục hoàn thành phải kèm lệnh verify + output.
- Không claim compliance certification (SOC 2/PCI/ISO) khi chưa có audit độc lập.
- Không đưa secret vào repo/workflow; mọi secret qua GitHub Actions secrets + Trusted Publishing.
- Mỗi việc 1 commit conventional; build 0 errors, không warning mới, toàn test pass trước commit.
