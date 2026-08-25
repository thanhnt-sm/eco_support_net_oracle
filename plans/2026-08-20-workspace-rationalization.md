# Plan: Quy hoạch workspace DataGuard và loại bỏ di sản EcoSupport

**Ngày**: 2026-08-20  
**Trạng thái**: Decision-complete; đã hoàn tất cutover policy/rule sang DataGuard; chưa thực hiện thao tác xóa hoặc di chuyển không đảo ngược.
**Phạm vi**: Mọi thành phần ngoài `src/`, bao gồm tài liệu, cấu hình, automation, mã di sản, state cục bộ và quy tắc agent.

---

## 1. Kết luận

`src/` là production source duy nhất đang được CI/release/Docker dùng cho **DataGuard .NET 9**. Hai workflow chỉ `setup-dotnet`, `restore/build/test DataGuard.sln`; Dockerfile publish `src/DataGuard.Cli`; `DataGuard.sln` liệt kê các project dưới `src/` và test C# dưới `tests/`.

Workspace vẫn mang một lớp di sản EcoSupport gồm Rust, Python, TypeScript, tài liệu, rules và toolchain cũ. Phần này không được xóa trong plan này: một số file đang có WIP không thuộc phiên hiện tại, một số là dữ liệu nghiên cứu hoặc state của tool khác. Quy hoạch chọn **cutover sạch sau quyết định của chủ sở hữu**, không tạo thêm thư mục `archive/` để giữ rác lâu dài.

### Bằng chứng chính

| Bằng chứng | Hệ quả |
|---|---|
| `.github/workflows/ci.yml` và `release.yml` build/test/pack `DataGuard.sln` bằng .NET 9 | C# DataGuard là đường phát hành hiện hành. |
| `Dockerfile` publish `src/DataGuard.Cli/DataGuard.Cli.csproj` | Runtime container là DataGuard CLI, không phải MCP/Node. |
| `README.md`, `package.json`, `Cargo.toml`, `pyproject.toml` vẫn mô tả EcoSupport | Documentation và manifest product đang lệch nhau. |
| `pyproject.toml` khai báo `src/eco_support`, nhưng đường dẫn này không tồn tại | Python package không thể là deliverable hiện hành. |
| `package.json` khai báo `packages/*`, trong khi `.gitignore` đang ignore `packages/` | TypeScript workspace không được version-control như production source; không được tự xóa. |
| `.tmp_new_models` không có tham chiếu trong surface vận hành được kiểm tra | Đây là ứng viên xóa có bằng chứng mạnh. |
| `.codegraph/.gitignore` ghi rõ database/log là local transient | Đây là cache có thể purge, không phải tài liệu hay source. |

---

## 2. Phân loại cấu trúc ngoài `src/`

### 2.1 Giữ: production và automation bắt buộc

| Nhóm | Đường dẫn | Lý do giữ |
|---|---|---|
| Build solution | `DataGuard.sln`, `Directory.Build.props` | Entry point .NET và policy build. |
| Test | `tests/DataGuard.Core.Tests/`, `tests/DataGuard.GoldenCorpus.Tests/` | Được solution và CI gọi. |
| CI/release | `.github/`, `.githooks/` | Đường phát hành, security scan, CodeQL, hook. |
| Container | `Dockerfile`, `.dockerignore` | Docker smoke-test và image release. |
| Scripts/tooling | `scripts/`, `tools/git-tools/` | Chỉ giữ các script còn được CI, hook hoặc runbook DataGuard gọi. |
| Workspace runtime | `.omp/` | Runtime/handoff OMP theo policy hiện tại; ignored theo chủ đích. |

### 2.2 Tài liệu và tri thức: giữ nhưng chuẩn hóa nội dung

| Nhóm | Đường dẫn | Hành động sau cutover |
|---|---|---|
| Tài liệu sản phẩm | `README*`, `CONTRIBUTING*`, `SECURITY*`, `docs/` | Viết lại theo DataGuard; không xóa chỉ vì đang cũ. |
| Kế hoạch | `plans/` | Chỉ giữ active plan, ADR và register đã cập nhật; đóng/archive logical các plan hoàn tất. |
| Nghiên cứu DataGuard | `research/muc_tieu/`, benchmark/data liên quan Oracle | Giữ, đặt tên rõ mục tiêu và liên kết từ docs/plan. |
| Chiến lược/hồ sơ | `brainstorm/`, `grants/` | Giữ như historical/business material, tách khỏi product docs. |
| Pháp lý | `LICENSE`, `LICENSE.md` | Không tự xóa: phải có quyết định owner về một license canonical trước. |

### 2.3 Không phải tài liệu: operational hoặc state cục bộ

| Nhóm | Đường dẫn | Chính sách |
|---|---|---|
| Rule/adapter agent | `AGENTS.md`, `rules/`, `.agentrules`, `.cursorrules`, `.windsurfrules`, `.geminirules`, `claude/`, `.agents/`, `devin_instructions.md` | Giữ một nguồn topology canonical là `rules/workspace_governance.md`; adapter chỉ còn khi tool tương ứng thật sự được dùng. |
| Agent state khác OMP | `.omo/config.toml`, `.omo/agents.toml`, `.omo/run-continuation/` | Không coi là config OMP. Giữ state local; không xóa thủ công session đang dùng. |
| Generated/local cache | `.codegraph/`, `.ruff_cache/`, `.mypy_cache/`, `.pytest_cache/`, `tests/__pycache__/` | Không commit; chỉ purge khi tool/daemon tương ứng đã dừng. Không dùng `git clean -fdx`. |
| Deployment cấu hình | `docker-compose.yml` | Không phải tài liệu. Hiện mâu thuẫn Dockerfile .NET với service MCP/Node; cần quyết định xóa hoặc viết lại theo một runtime thật. |

### 2.4 Ứng viên di sản: cần phê duyệt owner trước khi xóa

| Ứng viên | Bằng chứng | Disposition đề xuất |
|---|---|---|
| `crates/`, `Cargo.toml`, `Cargo.lock`, Rust tests | CI/release hiện không build Cargo; đây còn có WIP hiện diện trong working tree | Freeze; không đụng trước khi owner chọn tách sang repo khác hoặc xóa. |
| `pyproject.toml`, Python tests, `research/python_prototype/` | Python package target `src/eco_support` không tồn tại và CI không chạy Python | Giữ prototype research nếu còn giá trị; xóa manifest/tests production giả sau khi owner phê duyệt. |
| `package.json`, `pnpm-lock.yaml`, `tsconfig.base.json`, `vitest.config.ts`, `packages/` | Product EcoSupport cũ; `packages/` đang bị ignore bởi `.gitignore`; CI không build Node | Không xóa. Owner phải chọn khôi phục như project độc lập hoặc bỏ toàn bộ stack Node cùng lúc. |
| `.tmp_new_models` | Nội dung là catalog OpenCode Zen cũ; không có ref trong config, script, CI, OMP policy đã kiểm tra | Xóa trong cleanup commit được phê duyệt. |
| `.omo` tracked config và agent adapters cũ | Có thể thuộc tool khác, không có evidence là runtime DataGuard | Xác minh loader/tool owner trước; chỉ sau đó hợp nhất hoặc xóa. |
| Docs/rules EcoSupport cũ | Không phải code rác, nhưng mô tả product sai | Rewrite hoặc xóa có kiểm soát cùng các link/index; không bỏ file lẻ. |

---

## 3. Cấu trúc đích

```text
/
├── src/                         # Production DataGuard C# source
├── tests/                       # DataGuard C# tests
├── docs/                        # DataGuard product, developer, operation docs
├── research/                    # Non-production Oracle research, data, benchmarks
├── plans/                       # Active plans, ADRs, completed-plan record
├── scripts/                     # Verified developer/CI utilities
├── tools/                       # Versioned supporting tools
├── .github/  .githooks/         # CI, release, repository automation
├── .omp/                        # Ignored OMP workspace runtime and handoff
└── root manifests/configs       # Only DataGuard build/release/legal files
```

Không tạo `archive/`, `legacy/`, hay root scratch mới. Khi một stack di sản cần giữ lại, tách nó sang repository/branch do owner chọn; khi không cần, xóa trọn vẹn trong một commit có manifest.

---

## 4. Các phase thực hiện sau khi owner duyệt cleanup

### Phase 0 — Freeze và manifest quyết định

1. Ghi snapshot `git status --short`; bảo vệ mọi WIP, đặc biệt `Cargo.lock`, `crates/eco-agents/*`, docs agent-config và preflight script đang đổi.
2. Tạo bảng quyết định signed-by-owner cho Rust, Python, TypeScript, Docker Compose, agent adapters và license: `keep`, `extract`, `rewrite`, hoặc `remove`.
3. Không dựa vào một search không có match để xóa tracked file. Kiểm tra manifest, CI/release, import/reference và owner intent.

**Success criteria**: Không có thay đổi user-owned bị mất; mọi candidate có disposition rõ ràng.

### Phase 1 — Cutover documentation và control plane sang DataGuard

> Đã hoàn tất trong phiên quy hoạch: `CLAUDE.md`, `AGENTS.md`, bốn rule trong `rules/`, các adapter workspace (`.agentrules`, Cursor, Windsurf, Gemini, Devin, `.agents/`) và rule OMP đều trỏ về topology DataGuard. Các mục còn lại của phase là nội dung product, hook và validator sau quyết định owner.


1. Viết lại root README/contributing/security, `docs/architecture/`, `docs/operations/`, `docs/testing/`, registry và session register để mô tả DataGuard C# thay vì EcoSupport.
2. Chuyển `verify_docs_sync.sh` từ kiểm tra tồn tại danh sách EcoSupport sang các DataGuard docs thực sự và các link/command có thể kiểm chứng.
3. Chuyển `.githooks/pre-commit`/`pre-push` từ Cargo/Ruff mặc định sang formatter, build và test .NET; chỉ giữ toolchain khác khi stack tương ứng được owner giữ.
4. Đồng bộ `scripts/anti_garbage_guard.sh` với allowlist DataGuard (`DataGuard.sln`, `Directory.Build.props`, Docker files, `.github/`, `src/`, `tests/`, docs/plans/research/scripts/tools) và bỏ whitelist lịch sử không còn dùng.

**Success criteria**: Repo entrypoints, hook, docs validator và rule không còn mô tả production Rust/Node/Python nếu stack đó đã bị loại.

### Phase 2 — Tách hoặc xóa di sản theo manifest

1. Nếu owner chọn giữ stack: extract bằng lịch sử Git sang repository riêng, xác nhận build/test độc lập, sau đó xóa tham chiếu chéo khỏi DataGuard.
2. Nếu owner chọn remove: xóa trọn stack cùng manifest, test, configs, docs links, script/hook commands và lock file liên quan trong cùng cleanup commit.
3. Xóa `.tmp_new_models` khi manifest được duyệt.
4. Với `docker-compose.yml`, hoặc viết service đúng cho DataGuard CLI hoặc xóa file; không giữ compose MCP không có runtime server.
5. Chọn duy nhất một license canonical với tư vấn/approval owner; cập nhật manifest, README badge, package metadata và xóa license còn lại chỉ trong phase này.

**Success criteria**: Không còn product identity, package manager hay CI surface mồ côi.

### Phase 3 — Purge state cục bộ an toàn

1. Dừng CodeGraph daemon trước khi xoá `codegraph.db`, WAL/SHM và log trong `.codegraph/`; giữ `.codegraph/.gitignore`.
2. Xóa cache Python (`.ruff_cache/`, `.mypy_cache/`, `.pytest_cache/`, `__pycache__/`) bằng tool owner hoặc lệnh purge rõ scope.
3. Không xóa `.omp/` handoff hoặc `.omo/run-continuation/` khi còn session/tool dùng chúng. Để tool owner rotate state hoặc nhận explicit directive.
4. Xác nhận `git status --ignored --short` chỉ còn state được policy cho phép.

**Success criteria**: Working tree không có generated debris không cần thiết và không làm mất session/runtime state.

### Phase 4 — Verification và handoff

1. `dotnet restore DataGuard.sln`, `dotnet build DataGuard.sln --configuration Release`, `dotnet test DataGuard.sln --configuration Release`.
2. Chạy actionlint/YAML validation cho `.github/workflows/` và Docker smoke test khi daemon Docker sẵn sàng.
3. Chạy bản `scripts/anti_garbage_guard.sh` và `scripts/verify_docs_sync.sh` đã đổi; chúng phải validate DataGuard topology, không chỉ check file tồn tại.
4. Xác nhận không còn reference tới stack đã chọn remove trong manifest/CI/hook/docs; kiểm tra license/link một lần cuối.
5. Cập nhật `plans/ACTIVE_SESSION_REGISTER.md` và `.omp/handoffs/CURRENT.md` với evidence thực, không chép transcript hay secret.

**Success criteria**: CI local kiểm chứng DataGuard, repo topology và agent policy đồng nhất, cleanup có thể review bằng một diff rõ ràng.

---

## 5. Quy tắc quyết định

- Không xóa tracked source, plan, research, license, agent config hoặc state chỉ vì hiện chưa có callsite.
- Không thay thế lịch sử bằng thư mục `archive/` trong cùng production repository.
- `packages/` bị ignore là một defect ownership/version-control, không phải bằng chứng nó được phép xóa.
- Cache có thể purge; config/session state cần tool owner hoặc user directive.
- Mọi cleanup commit phải có manifest from → disposition, cập nhật toàn bộ caller/link/hook/validator và chạy verification DataGuard.

## 6. Phụ thuộc Marketplace

`src/DataGuard.VSCode/` là production source của VS Code extension, không thuộc TypeScript/EcoSupport di sản ở root. `src/DataGuard.VisualStudio/` sẽ là production source Windows-only khi được thêm. Mọi cleanup Node/TypeScript manifest, CI hay docs phải preserve hai module và workflow phát hành của chúng. Chi tiết/gates: [Marketplace product plan](260820-marketplace-extensions/plan.md).

## Execution log — Phase 2 (2026-08-24)

Thực hiện: 2026-08-24 (orchestration phiên, thực thi trực tiếp do subagent spawn infra không khả dụng trong session — theo AGENTS.md rule 3).

| From | Disposition |
|------|-------------|
| `crates/` (5 crates eco-agents/eco-cli/eco-core/eco-mcp/eco-radar), `Cargo.toml`, `Cargo.lock`, `target/` | REMOVE (D1) |
| `packages/{cli,core,mcp}`, `package.json`, `tsconfig.base.json`, `vitest.config.ts`, `pnpm-lock.yaml`, `node_modules/` | BACKUP rồi REMOVE (D2) |
| `pyproject.toml`, `tests/{__init__.py,test_agents.py,test_client.py,test_mcp_servers.py,test_radar.py}`, `tests/test_rust_*.rs` ×4, caches Python (`.venv`, `.mypy_cache`, `.pytest_cache`, `.ruff_cache`, `tests/__pycache__`) | REMOVE (D3) |
| `.tmp_new_models` | REMOVE (D4) |

- Backup path (ngoài repo): `/Volumes/Data/101.AI/GitHub/_legacy_ecosupport_backup/20260824T112630Z`
- Backup stats: **94 files, 346717 bytes**; đối chiếu count + tổng bytes khớp tuyệt đối nguồn, 5/5 SHA-256 checksum khớp.
- `research/python_prototype/` GIỮ nguyên (D3 chỉ xóa Python chết + test mồ côi).
- `scripts/anti_garbage_guard.sh` + `scripts/preflight_agent_check.sh` đã sửa (xóa allowlist/pattern di sản) và commit trong `b7072fc` (commit auto-sync xảy ra trước phiên này); guardrail chống auto-sync bổ sung ở `795820e` (`rules/git_workflow.md`, `.githooks/commit-msg`, harden `dg-git`, wire `core.hooksPath`).
- Reference còn lại trong `docs/` (mô tả sản phẩm chuyên biệt), `rules/`, `claude/skills/eco-support/`, `.gitignore` nằm NGOÀI scope todo 9 — thuộc plan docs-sync riêng.

## Execution log — Phase 2 reference sweep bổ sung (2026-08-25)

Đối chiếu plan với hiện trạng sau commit `0f7a1c2` phát hiện reference di sản còn sót. Root cause: orchestrator phiên trước buộc thực thi trực tiếp do subagent spawn infra hỏng (`ProviderModelNotFoundError: opencode/deepseek-v4-flash-free` — đã fix model override ở `~/.config/opencode/opencode.jsonc`), nên bỏ sót một số surface. Xử lý bổ sung:

| From | Disposition |
|------|-------------|
| `scripts/{preflight_agent_check,verify_docs_sync,git_sync,git_conflict_resolver}.sh` header "EcoSupport" | REWRITE → "DataGuard" |
| `claude/skills/eco-support/` (SKILL.md + plugin.json) | REMOVE |
| `.env.example` (Anthropic/FastMCP/ecosystem radar) | REWRITE → DataGuard env vars (`DATAGUARD_CONNECTION_STRING`, `DATAGUARD_PROVIDER`, `DATAGUARD_CLI_PATH`, `VAULT_TOKEN`) |
| `.gitignore` (section Rust/Python/pnpm/Cargo/vitest trùng lặp) | REWRITE → topology DataGuard (giữ .NET + Node VSCode + secrets) |

- Còn lại ngoài scope (plan docs-sync riêng): `docs/` và `plans/` mô tả sản phẩm EcoSupport cũ cần rewrite DataGuard; `research/python_prototype/` giữ nguyên theo D3.
