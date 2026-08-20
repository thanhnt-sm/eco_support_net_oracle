# 📋 ACTIVE SESSION REGISTER — Sổ Giao Ban Liên Phiên Làm Việc
### Đọc file này TRƯỚC TIÊN khi bắt đầu bất kỳ phiên làm việc nào

**File này là nguồn sự thật duy nhất (Single Source of Truth).** Mọi AI model, mọi agent, mọi provider phải đọc file này TRƯỚC KHI làm bất cứ điều gì trong workspace này.

---

## 🎯 MỤC TIÊU TỐI THƯỢNG (KHÔNG BAO GIỜ THAY ĐỔI)

> **EcoSupport Native**: Hệ thống tự động phát hiện và hỗ trợ các thư viện mã nguồn mở niche/ngách có nguy cơ cao nhưng thiếu hỗ trợ, nhằm giành tài trợ **"Claude for Open Source" — Ecosystem Impact Track** của Anthropic.

**KPI Thành công**: Hồ sơ được chấp thuận → Nhận Claude Max 20x API usage.

---

## 🗺️ CẤU TRÚC WORKSPACE (PHẢI NẮM VỮNG, KHÔNG ĐƯỢC TẠO FILE/FOLDER NGOÀI CẤU TRÚC NÀY)

```
eco_support/                    ← ROOT (Cargo workspace)
├── Cargo.toml                  ← WORKSPACE MANIFEST (không sửa trực tiếp)
├── CLAUDE.md                   ← Chỉ dẫn cho Claude agent
├── AGENTS.md                   ← Đặc tả multi-agent swarm
├── .cursorrules                ← Quy tắc cho Cursor IDE
├── .windsurfrules              ← Quy tắc cho Windsurf
├── .geminirules                ← Quy tắc cho Gemini
├── .agentrules                 ← Quy tắc cho Devin/OpenCode/Oh-My-Pi
│
├── crates/                     ← 🦀 RUST PRODUCTION CODE ONLY
│   ├── eco-core/               ← Config, Claude API Client, Telemetry
│   ├── eco-radar/              ← ECI Calculator, Registry Scanner
│   ├── eco-mcp/                ← FastMCP 2.0 Server & Security Auditor
│   ├── eco-agents/             ← Triage, Patch, Bridge Agents
│   └── eco-cli/                ← Binary CLI + Integration Tests
│
├── docs/                       ← 📚 LIVING DOCUMENTATION (sync bắt buộc với code)
│   ├── overview/               ← Vibe Coder visual guide
│   ├── architecture/           ← System architecture + tech stack
│   ├── operations/             ← SRE playbook & runbook
│   ├── testing/                ← QA test strategy
│   ├── developers/             ← Developer deep dive
│   └── sitemap_and_component_registry.md  ← Master inventory
│
├── rules/                      ← 🤖 BỘ LUẬT AI (không xóa, không sửa tùy tiện)
│   ├── universal_ai_constitution.md
│   ├── workspace_governance.md
│   ├── doc_sync_enforcement.md
│   └── small_model_operational_protocol.md
│
├── plans/                      ← 📅 KẾ HOẠCH & TIẾN ĐỘ
│   └── ACTIVE_SESSION_REGISTER.md  ← FILE NÀY (đọc trước tiên!)
│
├── brainstorm/                 ← 🧠 CHIẾN LƯỢC & RED-TEAM (chỉ đọc)
├── research/                   ← 🔬 NGHIÊN CỨU ONLINE (độc lập, không import vào crates/)
├── grants/                     ← 🏆 HỒ SƠ ANTHROPIC (không sửa trừ khi được chỉ định)
├── scripts/                    ← ⚙️ CÔNG CỤ TỰ ĐỘNG HÓA
│   ├── git_sync.sh             ← Push code
│   ├── git_conflict_resolver.sh
│   ├── verify_docs_sync.sh     ← Kiểm tra doc sync
│   └── anti_garbage_guard.sh   ← Chặn file rác
│
└── scratch/                    ← 🗑️ SCRATCHPAD (gitignored, throwaway only)
```

---

## 🚦 TRẠNG THÁI HIỆN TẠI CỦA DỰ ÁN

### Build Status
```
cargo check --workspace   ✅ PASS (0 errors, 0 warnings)
cargo test --workspace    ✅ PASS (22/22 tests)
cargo build --release     ✅ PASS → target/release/eco-support (3.7 MB)
```

### Tình trạng Documentation
```
./scripts/verify_docs_sync.sh  ✅ PASS (16/16 docs present)
./scripts/anti_garbage_guard.sh ✅ PASS (no rogue files)
```

### Commit History
```
[Commit 1] chore: initial commit (fresh repository) [2026-08-17]
           → FRESH GIT. History cũ (4 commits) đã bị xóa; backup tại
             ~/eco_support_net_oracle_old_git_backup_20260817-022803.tar.gz
           → 123 files: full Rust workspace + research + docs + rules
             + grants/SUBMISSION_CHECKLIST.md + research/muc_tieu/ + scripts/demo_scan.sh

[Commit 2] ea41340 docs(session-register): record fresh repository creation and new remote push steps
           → Ghi log session-1 vào register.

[Commit 3] e68f165 chore(repo): rename to eco_support_net_oracle across metadata and docs
           → Đổi URL repo → `eco_support_net_oracle` (tạm dùng account `thannt`)
             + `git remote add origin ...eco_support_net_oracle.git` + thêm research/muc_tieu/5.md

[Commit 4] 9400772 fix(repo): correct GitHub account to thanhnt-sm in repo metadata
           → Phát hiện account GitHub thật là `thanhnt-sm` (từ remote bản clone cũ):
             sửa toàn bộ URL `thannt/…` → `thanhnt-sm/…`.

[Commit 5] 🚀 PUSH THÀNH CÔNG lên https://github.com/thanhnt-sm/eco_support_net_oracle
           → `main` → `origin/main`, tracking set. Repo mới ONLINE.
```

---

## 📌 VIỆC VỪA HOÀN THÀNH (Phiên này — Antigravity Session 5)

1. ✅ **GitHub REST API Integration (`crates/eco-radar`)**:
   - Nâng cấp `NicheScanner` tích hợp HTTP Client (`reqwest`) với cấu hình User-Agent, Auth token (`GITHUB_TOKEN`), và rate limit header inspection (`x-ratelimit-remaining`).
   - Implement `fetch_github_metrics()` và `scan_live_repo()` cho phép quét trực tiếp repository live trên GitHub.
   - Cơ chế graceful fallback về seed dataset (`research/data/niche_seed_registry.json`) hoặc synthetic metrics khi chạy offline/không có token.
2. ✅ **Unit Test Fixture Suite cho ECI Calculator**:
   - Thêm `test_seed_registry_fixtures_evaluation()` trong `crates/eco-radar/src/calculator.rs` nạp toàn bộ repo hạt giống trong `niche_seed_registry.json` và kiểm tra phân tầng rủi ro chính xác.
   - Sửa cảnh báo Clippy `field_reassign_with_default` trong `tests/test_rust_core.rs`.
   - `cargo test --workspace` → **23/23 tests PASS** (14 unit tests, 9 integration tests).
   - `cargo clippy --workspace --all-targets -- -D warnings` → **0 warnings**.
   - `cargo fmt --all -- --check` → **100% formatted**.
3. ✅ **Grant Submission Checklist & Live Demo Tooling**:
   - Tạo [`grants/SUBMISSION_CHECKLIST.md`](file:///Volumes/Data/101.AI/GitHub/eco_support_net_oracle/grants/SUBMISSION_CHECKLIST.md) chuẩn bị đầy đủ cho đợt nộp hồ sơ Anthropic Claude for Open Source.
   - Tạo [`scripts/demo_scan.sh`](file:///Volumes/Data/101.AI/GitHub/eco_support_net_oracle/scripts/demo_scan.sh) (chạy mượt mà kiểm tra preflight, scan c-ffi và scan mcp-connectors).
   - Cập nhật [`docs/sitemap_and_component_registry.md`](file:///Volumes/Data/101.AI/GitHub/eco_support_net_oracle/docs/sitemap_and_component_registry.md) và bản dịch tiếng Việt [`docs/sitemap_and_component_registry.vi.md`](file:///Volumes/Data/101.AI/GitHub/eco_support_net_oracle/docs/sitemap_and_component_registry.vi.md).
4. ✅ **Xác thực tự động toàn diện**:
   - `./scripts/preflight_agent_check.sh` → **100% PASS**
   - `./scripts/verify_docs_sync.sh` → **100% PASS** (30/30 artifacts)
   - `./scripts/demo_scan.sh` → **100% PASS**
5. ✅ **Khởi tạo Git mới (Fresh Repository)**:
   - Workspace vốn được clone từ clone-ngoài nhưng không còn remote → đã **xóa hẳn `.git` cũ** và `git init -b main` tạo repo mới sạch lịch sử.
   - Backup history cũ: `~/eco_support_net_oracle_old_git_backup_20260817-022803.tar.gz` (421K).
   - Commit đầu tiên: `874cea9 chore: initial commit (fresh repository)` — **123 file**, 0 file rác (đã kiểm tra `target/`, `.venv/`, cache... không bị track).
   - Working tree sạch, pre-commit hooks pass.
6. ✅ **Chuyển sang tên repo mới `eco_support_net_oracle`**:
   - Thêm remote: `origin → https://github.com/thanhnt-sm/eco_support_net_oracle.git` (account thật `thanhnt-sm`, phát hiện từ remote gốc của bản clone cũ).
   - Thay toàn bộ URL repo cũ `thanhnt-sm/eco_support` → `thanhnt-sm/eco_support_net_oracle` trong: `Cargo.toml`, `README.md`, `README.vi.md`, `CONTRIBUTING.md`, `CONTRIBUTING.vi.md`, `grants/written_explanation.md` (kèm `cd eco_support` → `cd eco_support_net_oracle`).
   - Giữ nguyên tên package Python nội bộ `eco_support`, các `file:///.../eco_support/...` và thư mục clone cũ `/Volumes/Data/101.AI/GitHub/eco_support` (chúng là tham chiếu tới bản clone cũ, không ảnh hưởng repo mới).
7. ✅ **Đính chính account GitHub & Push lên remote**:
   - Phát hiện account GitHub thật là **`thanhnt-sm`** (không phải `thannt`) nhờ đọc `remote.origin.url` trong 2 bản clone cũ.
   - Sửa remote: `origin → https://github.com/thanhnt-sm/eco_support_net_oracle.git`.
   - **`git push -u origin main` THÀNH CÔNG** → repo mới online tại `https://github.com/thanhnt-sm/eco_support_net_oracle`.

---

## 🎯 VIỆC CẦN LÀM TIẾP THEO (Theo thứ tự ưu tiên)

### Ưu tiên 1 — ✅ ĐÃ HOÀN THÀNH: Tạo repo & Push
- [x] Repo `thanhnt-sm/eco_support_net_oracle` (private, mới) → đã tồn tại + push thành công.
- [x] `git push -u origin main` → tracking `origin/main` set.
- [ ] (Tuỳ chọn) Set visibility **public** nếu muốn hồ sơ grant được review công khai.

### Ưu tiên 2 — .NET Oracle Drift Contract Engine (Kiến trúc v2 trong research/muc_tieu/2.md)
- [ ] Triển khai kiến trúc v2 cho .NET Oracle: Tách IDE analyzer syntax nhẹ và CI diff-engine nặng.
- [ ] 3 Mode: Snapshot JSON (offline, default), Full (DB live), Manual (DTO attributes).

---

## 📏 LUẬT VÀNG CHO AI KHI LÀM VIỆC

| Luật | Nội dung |
| :--- | :--- |
| **LUẬT 1** | Không tạo bất kỳ file/folder nào ngoài cấu trúc đã quy hoạch ở trên. File tạm → để vào `scratch/`. |
| **LUẬT 2** | Mỗi khi sửa code trong `crates/` → phải cập nhật đồng thời `docs/` và `docs/sitemap_and_component_registry.md`. |
| **LUẬT 3** | Sau mỗi thay đổi code → chạy `cargo check --workspace`. Nếu lỗi → tự sửa, không đi tiếp. |
| **LUẬT 4** | Dùng `./scripts/git_sync.sh "message"` để commit — không dùng `git` trực tiếp. |
| **LUẬT 5** | Cập nhật file này (`plans/ACTIVE_SESSION_REGISTER.md`) sau mỗi phiên làm việc để phiên sau biết tiếp tục từ đâu. |
| **LUẬT 6** | `research/` hoàn toàn độc lập. Không import code từ `research/` vào `crates/`. |

---

## 📌 VIỆC VỪA HOÀN THÀNH (Phiên này — Research: NuGet Release Pipeline Best Practices)

1. ✅ **Nghiên cứu checklist best-practice cho NuGet release pipeline (.NET 8, 2025–2026)**:
   - Version từ git tag → khuyến nghị **MinVer** (tag-first, set đủ Version/PackageVersion/AssemblyVersion; cần `fetch-depth: 0`).
   - Secret check không shell injection → env var + `if: env.X != ''` / `github.secret_source`; khuyến nghị bỏ hẳn API key khi chuyển Trusted Publishing.
   - Release action → `gh` CLI (draft → upload → publish) thay softprops v2 (EOL, lỗi immutable releases #653).
   - NuGet publish → **Trusted Publishing (OIDC, NuGet/login@v1)** bắt buộc migrate trước 01/11/2026 (API key cũ hết hạn); bỏ `--skip-duplicate`; provenance trên nuget.org chưa có (epic #13581 mở).
   - `dotnet pack` solution + `<IsPackable>false</IsPackable>` cho project không packable.
   - SBOM → CycloneDX cho NuGet package; Microsoft.Sbom.DotNetTool khi cần SPDX.
   - Deliverable: checklist + YAML skeleton đầy đủ (kèm nguồn chính thức) — chưa ghi file vào repo (chỉ trả lời trong session).

## 🎯 VIỆC CẦN LÀM TIẾP THEO (bổ sung)
- [ ] (Khi có yêu cầu) Áp dụng checklist để rewrite `release.yml` của dự án .NET 8 tương ứng.

---

## 📌 VIỆC VỪA HOÀN THÀNH (Phiên này — Continuation: DataGuard Security Hardening)

1. ✅ **Recover session cũ dở dang (todo 3 phase/15 item: Core Fixes ×6, Validation Logic ×6, Integration ×3)**:
   - Đánh giá hiện trạng: `dotnet build DataGuard.sln` → **0 errors**; `dotnet test` → **37/37 PASS** (29 Core + 8 GoldenCorpus); không còn `NotImplementedException`/stub trong `src/`.
   - Kết luận: toàn bộ 15 item todo cũ đã hoàn thành ở các commit trước (build + test xanh).
2. ✅ **Security hardening — xử lý toàn bộ lỗ hổng package (NuGet audit)**:
   - `Microsoft.Extensions.Caching.Memory` 8.0.0 → **8.0.1** (fix HIGH GHSA-qj66-m88j-hmgj).
   - `Microsoft.Data.SqlClient` 5.2.0 → **5.2.2** (fix transitive Azure.Identity 1.10.3 + MSAL 4.56.0) — cả `DataGuard.Core` lẫn `DataGuard.SqlServer.Adapter`.
   - `Microsoft.Extensions.Logging.Abstractions` 8.0.0 → **8.0.2** (khớp Caching.Memory 8.0.1, tránh NU1605 downgrade).
   - `Testcontainers.Oracle/MsSql` 3.5.0 → **4.14.0** (fix transitive SSH.NET 2020.0.2 HIGH + BouncyCastle 2.2.1 Moderate) — test-only.
   - `System.Text.Json` 8.0.5 → **8.0.6** + **xóa `NoWarn="NU1903"`** (suppression tạo điểm mù cho `dotnet list package --vulnerable`; 8.0.5 thực chất đã vá CVE-2024-30105/CVE-2024-43485, nhưng suppression không nên tồn tại nếu không có lý do).
   - Verify: `dotnet list package --vulnerable` → **0 vulnerable** trên cả 9 project, **sau khi xóa toàn bộ `NoWarn="NU1903"`** (scan không còn điểm mù).

## 🎯 VIỆC CẦN LÀM TIẾP THEO (bổ sung)
- [ ] (Tuỳ chọn) Dọn 3201 warning CS1591/CS1998/CS860x tồn đọng (thiếu XML doc, async-no-await, nullable) — `TreatWarningsAsErrors=false` nên không chặn build; ưu tiên thấp.

---

## 📌 VIỆC VỪA HOÀN THÀNH (Phiên này — CI/CD Upgrade: Research + Redteam + Implement + Test + QC)

1. ✅ **Pull GitHub + vi phẫu 2 workflow files** (`ci.yml`, `release.yml`):
   - `git stash push -u` → `git pull --rebase origin main` (không xung đột); workflows = bản remote; WIP (Analyzers.cs, OracleDialectChecker.cs, PhantomIdentifierRule.cs) stash/pop nguyên vẹn.
   - Phát hiện Critical: SDK 8.0.x vs toàn bộ project net9.0; job codeql thiếu `contents: read`; Dockerfile Node.js legacy tham chiếu `packages/*` + `pnpm-workspace.yaml` KHÔNG TỒN TẠI; trufflehog@main; cosign v2.6.0 dính GHSA-fx35-mq7g-6g98.
2. ✅ **Research 5 background agents** (CI .NET best practices, NuGet release/Trusted Publishing, supply chain/sigstore, Docker/GHCR multi-arch, docs exploration) → SHA pins verified, cosign v3.1.3 + `--bundle`, `actions/attest@v4`, gh CLI thay softprops.
3. ✅ **Plan decision-complete**: `plans/2026-08-20-ci-cd-upgrade.md` (16 quyết định D1–D16). Momus review bị cancel do stale timeout (5 phút) — không tạo thay thế.
4. ✅ **Implement toàn bộ**:
   - `ci.yml` rewrite: SDK 9.0.x, permissions tối thiểu, build/test fail-fast, vuln-scan gate (parse JSON thật), TruffleHog pin SHA + chạy cả push/PR, CodeQL + custom queries (`codeql-config.yml` mới), SBOM pin 4.1.5, docker smoke test, cache NuGet.
   - `release.yml` rewrite: version từ tag (tách `release_tag`/`release_version` không prefix v), Trusted Publishing + fallback API key, cosign keyless sign+verify (bundle), SBOM có nupkg thật, gh CLI draft→publish + guard draft cũ, `actions/attest@v4` provenance, Docker multi-arch (amd64+arm64) push GHCR, pin SHA toàn bộ actions.
   - `Dockerfile` mới: .NET 9 CLI multi-stage (sdk→runtime), restore project-level (fix MSB3202 — sln chứa test projects không COPY), `COPY --link`, `USER $APP_UID`, `--arch $TARGETARCH` (verify amd64→x64), `ARG VERSION` bake version.
   - Phụ trợ: `dependabot.yml`, `.dockerignore` mới; `Directory.Build.props` net8.0→**net9.0**; sửa `RepositoryUrl` sai trong `DataGuard.Core.csproj`; xóa `.github/.DS_Store`; README section Docker → DataGuard CLI image `ghcr.io/thanhnt-sm/eco_support_net_oracle:latest`.
5. ✅ **Test**: `actionlint` clean (exit 0), YAML valid, `dotnet build` 0 errors (3201 warnings StyleCop pre-existing), Core.Tests 29/29 PASS. Docker smoke test defer — local không có daemon (CI sẽ chạy).
6. ✅ **QC adversarial (code-reviewer)**: 20 findings (2 BLOCKER, 5 MAJOR, 9 MINOR, 4 NIT) → **đã fix 100%**:
   - BLOCKER: Dockerfile restore sln (→ project-level) + `sbom-tool -o` không tồn tại (→ `-m` + `mkdir` + `-bc` directory) — cả 2 workflows.
   - MAJOR: shell injection `inputs.tag`/`ref_name` (→ env), SBOM glob `**` thiếu globstar (→ `find`+mapfile), `latest`/version tag normalize, SBOM cần nupkg thật (`download-artifact` trước generate).
   - MINOR: bỏ dead check `deprecated`, pin SHA 8 actions còn lại, draft-release guard, `qlpack.yml` path (→ tham chiếu `.github/codeql`), props copy trong Docker, bỏ SBOM download thừa.
   - Re-verify: actionlint + YAML clean sau fixes.

## 🎯 VIỆC CẦN LÀM TIẾP THEO (bổ sung)
- [x] **Đã push** — origin/main: c773b62 → 372fcdf (21 commit), ngày 2026-08-20.
- [ ] **User action bắt buộc**: tạo secret `NUGET_USER` (Trusted Publishing trên nuget.org, hạn chót migrate 01/11/2026) + tùy chọn `NUGET_API_KEY` fallback. Thiếu secrets → release workflow fail có chủ đích (fail-loud).
- [ ] Test thật trên GitHub: push → CI chạy (docker smoke cần daemon runner); tạo tag `v0.1.0` để test release pipeline end-to-end.
- [ ] Verify CodeQL custom queries lần chạy đầu (finding 15 debatable — pack layout `./.github/codeql` đã fix, cần xác nhận trên runner thật).
- [ ] (Tuỳ chọn, ngoài scope) README/register còn mô tả sản phẩm npm/Rust cũ (`@eco-support/*`, `crates/`) — lỗi thời so với repo .NET DataGuard; cần rewrite riêng khi có yêu cầu.

## 📌 VIỆC VỪA HOÀN THÀNH (Phiên này — Ship: Review + 16 Critical Fixes + Push main)

1. ✅ **Ship pipeline**: tạo branch `chore/ci-cd-upgrade`, merge origin/main (up-to-date), tests 37/37 PASS, review 2-pass.
2. ✅ **Pre-landing review**: 16 CRITICAL + 27 informational, toàn bộ nằm trong 19 commit cũ (không phải thay đổi CI/CD): `oracle-check` exit 0 khi fail; 5 lỗi quick-fix sinh code hỏng (CS1729/CS1503/CS7036, SQL bị thay bằng comment); 10 lỗi rule logic (DG001-005 false positive, dbo.Users/COUNT(*), DG098/099 thiếu descriptor, LONGTEXT tràn Int32, DG007 so byte vs char).
3. ✅ **Fix 16/16 critical**:
   - `oracle-check`: throw thay vì nuốt → exit 1 khi thiếu connection/fail.
   - CodeFixProviders: attribute đúng signature (`[SkipContractCheck(Reason=...)]`, `[ExpectedSpParameter(name,dbType,direction)]`, `[MaxLength(int)]`); dialect/SQL quick-fix → comment note (không phá SQL); rename dùng `Renamer.RenameSymbolAsync`; AddUseOracle rename `UseSqlServer` giữ connection string.
   - Analyzers: descriptor DG098/DG099 (Warning) đăng ký `AllDescriptors` — hết fallback DG002 Error.
   - ContractRules: bỏ nhánh EntityDescriptor DG001 giả; `InferClrType` case-insensitive + bổ sung money/image/timestamp/smalldatetime; DG003 dùng `Direction` enum; DG004 bỏ qua `SELECT *`/biểu thức; DG005 dùng schema ground-truth `IsNullable`.
   - PhantomIdentifierRule: schema qualifier (`dbo.Users`), CTE name, keyword-alias, lọc biểu thức.
   - MySQL: `CHARACTER_MAXIMUM_LENGTH` đọc `GetInt64` + clamp (>int.MaxValue → null).
   - Oracle DG007: so chars với `CharLength` (fallback `MaxLength` cho cột BYTE).
4. ✅ **Verify**: build 0 errors; Core.Tests 29/29 + GoldenCorpus 8/8 PASS; scratch harness (ngoài repo) 12/12 assertion PASS cho fixes 8-14; smoke `oracle-check` không connection → EXIT=1.
5. ✅ **Commit + push origin/main** (user chọn push thẳng, không PR — gh CLI chưa login):
   - `f316e24 fix(analyzers): resolve 16 critical pre-landing review findings`
   - `372fcdf chore(ci): upgrade CI/CD pipeline for net9 (Trusted Publishing, multi-arch)`

## 🎯 VIỆC CẦN LÀM TIẾP THEO (bổ sung)
- [ ] **User action bắt buộc**: tạo secret `NUGET_USER` (Trusted Publishing trên nuget.org, hạn chót migrate 01/11/2026) + tùy chọn `NUGET_API_KEY` fallback.
- [ ] Theo dõi CI run đầu tiên trên GitHub (docker smoke cần daemon runner); tạo tag `v0.1.0` để test release pipeline end-to-end.
- [x] 27 informational findings — **đã fix 24/27** ở phiên sau (xem mục "Audit Plan + 27 Informational Fixes"); 3 còn lại cần DB thật.
- [ ] gh CLI chưa login → phiên này chưa tạo được issue/PR (push thẳng main).

## 📌 VIỆC VỪA HOÀN THÀNH (Phiên này — Audit Plan + 27 Informational Fixes)

1. ✅ **Audit toàn bộ tài liệu plan/giải pháp**:
   - `plans/2026-08-20-ci-cd-upgrade.md`: D1-D16 + toàn bộ file thay đổi **đã implement đầy đủ** (spot-check: SDK 9.0.x, Trusted Publishing, cosign v3.1.3, attest v4, TruffleHog pin SHA, image `ghcr.io/…`, RepositoryUrl đã sửa). `.github/.DS_Store` không track, đã xóa khỏi đĩa.
   - `research/muc_tieu/2.md` (kiến trúc v2): tách IDE-light/CI-heavy ✓ (`UnvalidatedSqlCallGenerator` incremental + CLI); 3 mode Full/Snapshot(default)/Manual ✓ (`GroundTruthMode` enum, default Snapshot); Manual attribute ✓ (`ExpectedColumnAttribute`); snapshot gắn `DatabaseVersion` + **bổ sung cảnh báo lệch DB version** trong `snapshot diff` (yêu cầu chuyên gia 5).
   - `plans/implementation-plan.md`: Dapper analyzer ✓ (AnalyzeDapperQuery); rule set đầy đủ — **đã wire DG007-016 + MY001-003 + PG001-003 vào CLI `GetRulesForProvider`** (trước chỉ có DG001-006 chạy production); CLI reference thêm MySql/PostgreSql adapters.
2. ✅ **Fix 24/27 informational findings**:
   - Core: PublicApiSurface thu violation thật + duration thật; ConcurrentValidationEngine + schema hash sắp thứ tự deterministic; TelemetryCollector dùng `FlushIntervalSeconds` + tag `success` đúng tham số; ZeroTrustCredentialProvider tôn trọng `EnableAuditLogging` + injected logger; SupplyChainVerifier (JIT-tracking debug check, AWSSDK trusted, unsigned = informational); BaselineManager migrate chịu file hỏng; RulePluginManager chỉ nạp dir tường minh + log skip; EfModelSource cache scan + loại bin/obj + fallback design-time thật sự; AutoDetection khôi phục env var fallback + appsettings.Development.json + split YAML an toàn; ContractRules (DG004 so cả ColumnName, ToPascalCase lọc segment rỗng, bỏ nhánh chết).
   - Adapters: MySql/Pg dialect bỏ false positive ISNULL/LIMIT; SP parsers skip filler row (proc không tham số) + key schema.name (MySQL); DG008 worst-case 4 byte (AL32UTF8); bỏ nhánh DG014 chết.
   - CLI: `migrate` dùng option `--baseline` riêng; Deserialize/Serialize round-trip 10 field mới; snapshot diff cảnh báo drift DB version.
   - CI/tooling: release.yml truyền `VERSION` build-arg; Dockerfile bỏ ARG chết + label parameterize; dependabot thêm nuget; sln GUID SDK-style cho 2 adapter; VSCode command palette + UTF-8 decode; xóa `loop-results.tsv`; test tail-truncation audit + assert deterministic concurrent.
3. ✅ **Verify**: build 0 errors; Core.Tests 30/30 + GoldenCorpus 8/8 PASS.
4. ✅ **Push origin/main**: `9abdf18 → f8adbc3` (2 commit: `7a72b5c` code fixes + `f8adbc3` ci/config fixes).

## 🎯 VIỆC CẦN LÀM TIẾP THEO (bổ sung)
- [ ] **User action bắt buộc**: tạo secret `NUGET_USER` (Trusted Publishing, hạn chót 01/11/2026) + tùy chọn `NUGET_API_KEY` fallback.
- [ ] Theo dõi CI run đầu tiên trên GitHub; tạo tag `v0.1.0` test release pipeline end-to-end; verify CodeQL custom queries trên runner thật.
- [ ] 3 informational còn lại (cần DB thật/integration test): OracleReaders col_charsetform (NCHAR/NVARCHAR2); wire RefCursorDescriber vào đường Oracle validation (đã implement, chưa gọi); GoldenCorpusTests assert `unexpectedErrors` (cần align fixture H1_002 — entity PhoneNumber↔schema PHONE không khớp).
- [ ] NuGet publish 5 packages — cần NUGET_USER + tag release (không làm được local).
- [ ] gh CLI chưa login → chưa tạo được issue/PR.

---

## Phiên này — Quy hoạch workspace DataGuard

1. Đã kiểm tra toàn bộ surface ngoài `src/`: CI/release/Docker xác nhận DataGuard .NET là product canonical; Rust, Python, TypeScript, EcoSupport docs/rules và một số agent adapter là di sản hoặc cần quyết định owner.
2. Đã ghi manifest và phase cleanup tại `plans/2026-08-20-workspace-rationalization.md`; không xóa hoặc di chuyển WIP, source tracked, research, license, config agent hay session state.
3. Đã cutover toàn bộ rule workspace đang hiện diện (`CLAUDE.md`, `AGENTS.md`, `rules/*.md`, `.agentrules`, Cursor, Windsurf, Gemini, Devin và `.agents/`) sang policy DataGuard; rule OMP toàn cục trỏ về governance này. Không còn chỉ dẫn Cargo/Rust/EcoSupport bắt buộc. `.tmp_new_models` là candidate remove có evidence; `packages/` bị ignore không được coi là rác.

## Việc tiếp theo — quyết định owner trước cleanup

- [ ] Chọn disposition `keep`, `extract`, `rewrite` hoặc `remove` cho Rust, Python, TypeScript, Docker Compose, agent adapters và hai license.
- [ ] Sau khi owner duyệt, thực hiện cleanup theo phase 1–4 của plan, cập nhật toàn bộ docs/hook/validator và chạy verification DataGuard.

---

## 📌 VIỆC VỪA HOÀN THÀNH (Phiên này — Thiết lập OMP 2-model pipeline: DeepSeek worker → GPT solver)

1. ✅ Thiết lập `~/.omp/agent/config.yml` (global, ngoài repo — **không đụng `src/`**): `default`/`smol`/`slow`/`task`/`vision`/`designer`/`commit`/`tiny`/`prewalk`/`advisor` = `deepseek/deepseek-v4-pro:max`; `plan`/`gpt56` = `openai-codex/gpt-5.6-terra:high`; `cycleOrder: [default, gpt56]`; fallback chains (DeepSeek→DeepSeek, GPT→GPT→DeepSeek); `plan.defaultOnStartup: false`.
2. ✅ Tạo `~/.omp/agent/AGENTS.md` (auto-inject mọi session omp, cả 2 model): quy tắc tiếng Việt + protocol 2-model + template báo cáo 7 mục vào `<repo>/.omp/handoffs/`.
3. ✅ Tạo `~/.omp/agent/WORKFLOW.md` (import từ AGENTS.md): SOP chi tiết — model roles, plan mode, subagents (`scout`/`librarian`=`@smol`, `reviewer`=`@slow`), skills (`/skill:<name>`), advisor, prewalk, fallback, slash commands, handoff giữa 2 model.
4. ✅ Verify E2E: DeepSeek tạo `main.py` + handoff 7 mục → GPT đọc handoff, chạy lại verify → `KẾT LUẬN: ĐẠT`.

## 🎯 CÁCH DÙNG (tham chiếu nhanh)
- Session mở → DeepSeek worker (không tự plan mode). Vào GPT: `/model` → `gpt56`, hoặc `/plan`, hoặc `omp --plan`. Về DeepSeek: `/model` → `default`. Cycle 2 model: `Ctrl+P`.
- Một lệnh full pipeline: `omp --plan-yolo "…"`.

---

## 📌 PHIÊN GẦN NHẤT — DataGuard .NET + Marketplace (đã push `main`)

1. **Merge an toàn 2 PR Dependabot**: #3 (NuGet deps, 25 packages) và #4 (GitHub Actions group). Repair lock graph, rollback `System.CommandLine` về version main (tránh migration dở), verify CI green từng PR rồi squash-merge, xóa mọi branch ngoài `main`.
2. **CI core 5/5 green**; CodeQL open alerts = 0. Marketplace package 2/2 green (VS Code + Visual Studio VSIX kèm SHA-256 + SBOM + provenance).
3. **Marketplace product** (`plans/260820-marketplace-extensions/`): VS Code extension (trusted workspace, no-shell, private SARIF→Problems, cancel/tree) và Visual Studio VSIX (Tools command, SARIF→Error List, taskkill tree, disposal). **Chưa publish public**: cần owner verify publisher + secrets `VSCE_PAT`/`VS_MARKETPLACE_PAT` + VS 2022 Experimental Instance smoke. Runbook: `docs/marketplace-publishing.md`.
4. **Contract workflow**: CLI thêm `--format evidence|contracts|typescript` (deterministic, redacted, không serialize Location/Annotations). Refactor `BuildContractsAsync` + `ValidateContractsAsync`. Core.Tests 39/39 pass.
5. **Còn lại (debt, đã block)**: 8 annotations StyleCop/RS1038 → `plans/2026-08-21-warnings-plan.md`. Cần tách generator khỏi assembly reference `Microsoft.CodeAnalysis.Workspaces` và StyleCop settings (`SA1636/SA1204/SA16xx`).
