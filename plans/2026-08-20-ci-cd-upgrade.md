# Plan: CI/CD Pipeline Upgrade — eco_support_net_oracle (DataGuard)

**Ngày**: 2026-08-20
**Trạng thái**: Decision-complete, sẵn sàng implement
**Cơ sở**: Vi phẫu 2 workflow files + research 5 agent (GitHub Docs, OpenSSF, OWASP, sigstore, NuGet, SLSA, dotnet-docker)

---

## 1. Findings tổng hợp (redteam kết quả vi phẫu + research)

### 🔴 Critical — workflow chắc chắn fail
| # | Vấn đề | File |
|---|--------|------|
| C1 | SDK `8.0.x` nhưng **mọi project target net9.0** (src + tests) → build fail | cả 2 |
| C2 | Job `codeql`: job-level `permissions: {security-events: write}` **ghi đè** top-level → mất `contents: read` → checkout fail | ci.yml |
| C3 | Dockerfile là **Node.js di sản** tham chiếu `packages/*` không tồn tại → build image fail | cả 2 |
| C4 | `trufflesecurity/trufflehog@main` — mutable ref (không immutable) | ci.yml |
| C5 | Pin cosign `v2.6.0` **dính lỗ hổng GHSA-fx35-mq7g-6g98** (fix v2.6.5/v3.1.3) | release.yml |

### 🟠 High — logic sai
| # | Vấn đề | File |
|---|--------|------|
| H1 | csproj hardcode `Version=0.1.0-alpha.1` → pack ra version sai vs tag `v*`; `--skip-duplicate` nuốt lỗi lần release 2+ | release.yml |
| H2 | `continue-on-error: true` trên test → CI xanh dù test fail | ci.yml |
| H3 | `dotnet list --vulnerable` **luôn exit 0** → scan không bao giờ chặn build (continue-on-error chỉ là nửa vấn đề) | ci.yml |
| H4 | `[ -z "${{ secrets.NUGET_API_KEY || '' }}" ]` — **script injection** (CWE-094) | release.yml |
| H5 | `base: ${{ github.base_ref }}` = branch name, không phải SHA | ci.yml |
| H6 | Image naming 3 nơi khác nhau: `.../dataguard:latest` (ci), `...:v*` (release), `.../eco-support-mcp:latest` (README) | cả 2 |
| H7 | Custom CodeQL queries (5 file `.ql` + qlpack) **không được dùng** (config-file bị comment) | ci.yml |
| H8 | `DataGuard.Core.csproj` trỏ `RepositoryUrl` = `github.com/DataGuard/DataGuard` (repo không tồn tại) | csproj |

### 🟡 Medium
- M1: SBOM tool không thống nhất: `Microsoft.Sbom.DotNetTool` (ci) vs `Microsoft.Sbom.Tool` (release)
- M2: SBOM install không pin version (không reproducible)
- M3: Workflow-level permissions quá rộng (`packages`, `security-events`, `id-token` cho mọi job)
- M4: `softprops/action-gh-release@v1` — v2 EOL (Node 20), không upload được asset vào immutable release
- M5: Không caching NuGet; không có `packages.lock.json`
- M6: Không có `dependabot.yml` cho github-actions
- M7: `actions/attest-build-provenance@v3` → v4 là wrapper; khuyến nghị `actions/attest@v4`
- M8: Sign dùng `--output-signature` rời rạc → nên `--bundle` (mặc định từ cosign v3.1)
- M9: SBOM không đính kèm GitHub Release; không attest
- M10: `setup-qemu-action` thiếu cho multi-arch arm64 (release docker job)
- M11: Trigger `branches: develop` vô dụng (branch không tồn tại)
- M12: `.github/.DS_Store` file rác

---

## 2. Quyết định thiết kế

| Quyết định | Lựa chọn | Lý do |
|---|---|---|
| D1 SDK | `DOTNET_VERSION: 9.0.x` + `Directory.Build.props` default → `net9.0` | Mọi project đã net9.0; props net8.0 là bẫy cho project mới |
| D2 Versioning | **Pack-time override** `-p:PackageVersion=$VERSION -p:Version=$VERSION` từ tag/input. KHÔNG thêm MinVer vào solution (repo đang có WIP, 9 csproj — ít xâm lấn nhất). Note: MinVer là nâng cấp tương lai | Research: MinVer recommended nhưng pack-time override là fallback hợp lệ, không đụng build hệ thống |
| D3 Publish | **Trusted Publishing** (`NuGet/login@v1`) primary + **fallback NUGET_API_KEY** (secret check qua env var, không shell injection) | API keys giới hạn 30 ngày từ 17/08/2026, hết hạn hết 01/11/2026 |
| D4 Release action | **`gh` CLI**: draft → upload → publish. Bỏ softprops | Immutable-release compatible, zero third-party dependency |
| D5 Cosign | `cosign-installer@v4` + `cosign-release: v3.1.3`, sign/verify bằng **`--bundle`** (`.sigstore.json`) | Sửa GHSA; bundle là chuẩn từ v3.1 |
| D6 Attestation | `actions/attest@v4` (provenance cho nupkg). Bỏ attest-build-provenance@v3 | v4 = wrapper mới; GitHub khuyến nghị actions/attest |
| D7 SBOM | Thống nhất `Microsoft.Sbom.DotNetTool` **pin `--version 4.1.5`**; đính kèm SBOM vào GitHub Release | Reproducible; SPDX 2.2 JSON tương thích attest |
| D8 Docker | **Dockerfile .NET 9 mới** build `DataGuard.Cli` (console app): `sdk:9.0` → `runtime:9.0` (không aspnet), `USER $APP_UID`, `ENTRYPOINT ["dotnet", "DataGuard.Cli.dll"]`; multi-arch + `setup-qemu-action`; image = **`ghcr.io/${{ github.repository }}`** (1 package = repo name) | CLI console app; naming thống nhất; underscore hợp lệ trên GHCR |
| D9 CI gates | Bỏ mọi `continue-on-error` trên gate; vulnerable scan parse `--format json` fail thật sự; upload artifacts `if: always()` | Fail-fast đúng nghĩa |
| D10 TruffleHog | Pin `@bcfcf73aaf4759d4dadc2783177c245a02792318 # v3.97.0`, `only_verified: true`, bỏ base/head (action tự xử lý PR event) | Immutable; giảm false positive |
| D11 CodeQL | Thêm `contents: read` + `actions: read`; tạo `.github/codeql-config.yml` enable 5 custom queries | Fix C2; dùng tài sản có sẵn |
| D12 Pinning | Pin SHA cho actions research đã xác minh (checkout, setup-dotnet, upload-artifact, codeql-action, trufflehog, docker buildx/build-push); tag + comment cho action còn lại; thêm `dependabot.yml` (github-actions, weekly, groups) | OpenSSF/OWASP; Dependabot tự cập nhật SHA |
| D13 Caching | `actions/cache` với key `hashFiles('**/*.csproj','**/*.props')` — KHÔNG tạo lock files (tránh đụng WIP, 9 csproj mới). Note: `setup-dotnet cache: true` yêu cầu lock file → chọn cache action thay thế | An toàn cho repo đang WIP |
| D14 Secrets | `NUGET_USER` (Trusted Publishing) + `NUGET_API_KEY` (fallback) — qua env var, check bằng `if: env.X != ''` | CWE-094 fix |
| D15 Vulnerable scan | Fail build khi có vulnerable package (qua JSON parse) | Security gate đúng nghĩa |
| D16 Naming | Docker image duy nhất `ghcr.io/${{ github.repository }}`; cập nhật README dòng docker pull | Thống nhất 3 chỗ |

---

## 3. Files thay đổi

| File | Hành động |
|---|---|
| `.github/workflows/ci.yml` | Rewrite: fix C1/C2/C4, H2/H3/H5, D9/D10/D11/D12, bỏ docker-build cũ → smoke test image mới |
| `.github/workflows/release.yml` | Rewrite: fix C1/C3/C5, H1/H4/H6, D2-D8, M1/M2/M4/M7/M8/M9/M10 |
| `Dockerfile` | Rewrite hoàn toàn: .NET 9 CLI, multi-stage, non-root, multi-arch ready |
| `.github/codeql-config.yml` | MỚI: enable `.github/codeql/queries` |
| `.github/dependabot.yml` | MỚI: github-actions weekly |
| `Directory.Build.props` | `net8.0` → `net9.0` (default) |
| `src/DataGuard.Core/DataGuard.Core.csproj` | Sửa `RepositoryUrl`/`PackageProjectUrl` → `thanhnt-sm/eco_support_net_oracle` |
| `.github/.DS_Store` | Xóa (file rác) |
| `README.md` | Cập nhật dòng docker pull → naming mới |
| docs/* (SOLUTION.md, COMPONENT_INTERACTION.md, STAGE_FLOW.md, contributing.md, architecture.md, USAGE.md, cli.md, FIX_PLAN.md + .vi.md, sitemap registry) | Co-update theo AGENTS.md in-flight hook |
| `plans/ACTIVE_SESSION_REGISTER.md` | Ghi session mới |

## 4. Test plan
1. `actionlint` (cài qua brew) trên cả 2 workflow — 0 lỗi
2. YAML parse (python yaml.safe_load)
3. `dotnet build DataGuard.sln -c Release` — 0 errors (baseline đã verify: 0 errors)
4. `dotnet test` (nếu Docker sẵn — Testcontainers Oracle/MsSql) hoặc test project không cần container
5. Docker build local nếu Docker daemon chạy — smoke `docker run --rm <image> --help`
6. Code review agent (QC cuối) đối chiếu mọi finding

## 5. Rủi ro / cần user action
- **Trusted Publishing**: cần đăng ký policy trên nuget.org (owner/repo/workflow) + secret `NUGET_USER`. Fallback API key vẫn hoạt động đến 01/11/2026
- WIP chưa commit (3 file src) — không đụng tới, không commit vào scope này
- README mô tả product cũ (npm/Node eco-support) — chỉ sửa dòng docker pull, không đại tu