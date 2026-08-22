# Golden Standard — Template Checklist

> **Mục đích**: khuôn mẫu để owner copy workspace DataGuard cho sản phẩm .NET mới và có ngay nền tảng chuẩn GitHub / NuGet.org / Marketplace / enterprise.
> Mỗi mục kèm lệnh verify. Mục `[OWNER]` cần thao tác server-side hoặc secrets — không làm được từ repo.

## 1. Community & docs (Cấp 1)

| File | Chuẩn | Verify |
|---|---|---|
| `README.md` | Landing page sản phẩm, quickstart, badges | đọc thấy quickstart + ≥3 badge |
| `LICENSE` | MIT đơn nhất (không multi-license conflict) | `head LICENSE` |
| `CONTRIBUTING.md` (+ `.vi.md` nếu bilingual) | workflow dev, commit convention | tồn tại |
| `CODE_OF_CONDUCT.md` | Contributor Covenant v2.1 | tồn tại |
| `SUPPORT.md` | kênh hỗ trợ: issues, discussions, security advisory | tồn tại |
| `SECURITY.md` | private vulnerability disclosure; không email domain không thuộc owner | tồn tại + link advisory |
| `CHANGELOG.md` | Keep a Changelog format | có mục `[Unreleased]` |
| `.github/CODEOWNERS` | `* @<owner>` mọi change cần review | `git ls-files .github/CODEOWNERS` |
| `.github/ISSUE_TEMPLATE/bug_report.yml` | YAML form | tồn tại |
| `.github/ISSUE_TEMPLATE/feature_request.yml` | YAML form | tồn tại |
| `.github/ISSUE_TEMPLATE/config.yml` | blank_issues_enabled=false + contact links | tồn tại |
| `.github/PULL_REQUEST_TEMPLATE.md` | checklist review | tồn tại |

## 2. CI/CD & security gates (Cấp 2)

| Thành phần | Yêu cầu | Verify |
|---|---|---|
| `.github/workflows/ci.yml` | build → test → format gate → coverage gate → vuln gate → TruffleHog → CodeQL | `actionlint` clean |
| Coverage gate | fail dưới ngưỡng (DataGuard dùng 60%) | grep threshold trong ci.yml |
| Format gate | `dotnet format --verify-no-changes` | step trong ci.yml |
| `.github/workflows/release.yml` | cosign sign+verify, SBOM, provenance attestation, gh CLI draft→publish, Trusted Publishing | SHA-pinned actions |
| `.github/workflows/scorecard.yml` | OSSF Scorecard, read-only permissions | actionlint + badge README |
| `.github/workflows/standards-audit.yml` | repo tự kiểm chuẩn checklist này | chạy xanh trên main |
| `.github/dependabot.yml` | nuget + github-actions groups, weekly | tồn tại |
| Pin actions theo **full commit SHA** | mọi `uses:` | `grep -E 'uses:' .github/workflows \| grep -vE '@[0-9a-f]{40}'` rỗng |
| Permissions tối thiểu | mỗi workflow khai báo `permissions:` | grep `^permissions:` |
| `codeql-config.yml` + custom queries | paths-ignore + query suites | tồn tại |

## 3. Build & versioning

| Thành phần | Yêu cầu | Verify |
|---|---|---|
| `Directory.Build.props` | TreatWarningsAsErrors=true, Nullable enable, SourceLink, MinVer, deterministic CI build | `dotnet build` 0 warning |
| `packages.lock.json` mỗi project | `RestorePackagesWithLockFile=true`, restore `--locked-mode` | file tồn tại + restore pass |
| Versioning từ git tag | MinVer, prefix `v` | tag `v0.1.0` → version đúng |
| `.editorconfig` | style nhất quán; suppression phải có comment lý do + ngày gỡ | tồn tại |
| `.gitignore` / `.gitattributes` / `.dockerignore` | chuẩn .NET | tồn tại |

## 4. Test quality

| Thành phần | Yêu cầu | Verify |
|---|---|---|
| Unit tests ≥ 60% line coverage Core | coverlet + gate CI | coverage report ≥ threshold |
| Golden corpus tests | fixture JSON exact-match diagnostics | test suite riêng |
| Integration tests (Testcontainers) | auto-skip khi Docker unavailable | `[SkippableFact]` / try-catch pattern |
| Test isolation | env-var-sensitive test dùng `[Collection("Sequential")]` + IDisposable cleanup | không flaky khi chạy 5 lần liên tiếp |
| Zero-warning build | TreatWarningsAsErrors=true toàn solution | `dotnet build` → 0 Warning(s) |

## 5. Supply chain (NuGet.org chuẩn cao)

| Thành phần | Điều kiện | Verify |
|---|---|---|
| Trusted Publishing (OIDC) | `[OWNER]` secret `NUGET_USER`; hạn migrate API key 01/11/2026 | release run xanh |
| snupkg symbol packages | `-p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg` | nupkg kèm snupkg trên nuget.org |
| Package metadata | RepositoryUrl, License MIT, tags, readme, icon từng csproj | `dotnet pack` + kiểm nuspec |
| SBOM | CycloneDX cho NuGet package | artifact release |
| Provenance | `actions/attest@v4` | attestation trên GHCR/nuget |
| Vulnerability gate | `dotnet list package --vulnerable --include-transitive` = 0 | lệnh trả "no vulnerable" |

## 6. Marketplace (VS Code + Visual Studio)

| Thành phần | Điều kiện | Verify |
|---|---|---|
| VS Code extension | trusted workspace, no-shell, SARIF private | `src/<Name>.VSCode/` |
| VS 2022 VSIX | net472, Tools command, Windows CI build | `src/<Name>.VisualStudio/` |
| Publisher verify + PAT | `[OWNER]` secrets `VSCE_PAT`, `VS_MARKETPLACE_PAT` | publish run xanh |
| VSIX signing cert | `[OWNER]` cert | signed vsix |
| Runbook | docs/marketplace-publishing.md | tồn tại |

## 7. Server-side cấu hình `[OWNER]`

- [ ] Branch protection: require PR review (CODEOWNERS), status checks (CI + CodeQL), linear history
- [ ] Tag protection rule `v*`
- [ ] Secrets: `NUGET_USER`, `NUGET_API_KEY` (fallback), `VSCE_PAT`, `VS_MARKETPLACE_PAT`

## 8. Docs vận hành

| Doc | Nội dung | Verify |
|---|---|---|
| `docs/enterprise-banking-profile.md` | least-privilege DB role, offline-first config, exit-code contract, compliance discipline | tồn tại |
| Exit-code table | trong README | bảng 0/1/2 |
| Runbook publish | marketplace-publishing.md | tồn tại |
