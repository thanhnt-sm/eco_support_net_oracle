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
           → Đổi toàn bộ URL repo `thannt/eco_support` → `thannt/eco_support_net_oracle`
             (Cargo.toml, README.md, README.vi.md, CONTRIBUTING.md, CONTRIBUTING.vi.md,
              grants/written_explanation.md) + `git remote add origin ...eco_support_net_oracle.git`
             + thêm research/muc_tieu/5.md
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

---

## 🎯 VIỆC CẦN LÀM TIẾP THEO (Theo thứ tự ưu tiên)

### Ưu tiên 1 — Tạo repo trên GitHub & Push (Chờ User)
- [ ] Repo `thannt/eco_support_net_oracle` **chưa tồn tại** trên GitHub. Cần User tạo repo thủ công hoặc `gh auth login` để AI tạo giúp.
- [ ] Sau khi repo tồn tại: `git push -u origin main`.

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
