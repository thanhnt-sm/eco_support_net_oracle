# AGENTS.md - Multi-Agent Architecture & Governance

This document establishes the official operational blueprint, agent swarm responsibilities, and constitutional guardrails for autonomous agents working within the **EcoSupport** system.

---

## 🏛️ System Architecture Overview

EcoSupport operates as a collaborative swarm of specialized AI subagents, orchestrated through the **Model Context Protocol (MCP)** and powered by **Claude 3.7 Sonnet** with dynamic Extended Thinking capabilities.

```
                  ┌───────────────────────────────┐
                  │    EcoSupport Orchestrator    │
                  │   (Claude 3.7 Sonnet Host)    │
                  └──────────────┬────────────────┘
                                 │ FastMCP 2.0
         ┌───────────────────────┼───────────────────────┐
         ▼                       ▼                       ▼
┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐
│   Radar Agent    │    │   Triage Agent   │    │ Patch Synthesizer│
│ (Niche Discovery)│    │ (Deep Diagnosis) │    │(Thinking Patches)│
└────────┬─────────┘    └────────┬─────────┘    └────────┬─────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 ▼
                     ┌───────────────────────┐
                     │   MCP Bridge Builder  │
                     │  & Security Auditor   │
                     └───────────────────────┘
```

---

## 🤖 Subagent Specifications & Personas

### 1. `RadarAgent` (Niche Ecosystem Scanner)
* **Objective**: Continuously discover, index, and monitor niche open-source repositories and MCP servers that have high ecosystem impact but low maintainer bandwidth.
* **Core Tools**: `scan_niche_ecosystem`, `calculate_criticality_index`, `fetch_dependency_graph`.
* **Guardrail**: Does not modify code or write issues; purely observational and indexing.

### 2. `TriageAgent` (Deep Issue Diagnostician)
* **Objective**: Ingest unaddressed bug reports and feature requests from scanned repositories, reproduce failure modes, identify root causes, and craft empathetic, high-signal triage responses.
* **Core Tools**: `diagnose_repo_bottleneck`, `fetch_issue_thread`, `trace_stack_symbols`.
* **Thinking Budget**: 4,096 - 8,192 tokens.
* **Guardrail**: Never posts unverified assumptions. Must ground all diagnostic steps in actual code AST and test traces.

### 3. `PatchSynthesizerAgent` (Extended Thinking Code Engineer)
* **Objective**: Generate minimal, type-safe, backward-compatible PRs for complex bugs in niche libraries.
* **Core Tools**: `generate_repro_test`, `synthesize_code_patch`, `validate_ast_safety`.
* **Thinking Budget**: 8,192 - 16,384 tokens (High reasoning mode).
* **Guardrail**: Must provide accompanying unit tests and maintain backwards compatibility for existing dependents.

### 4. `MCPBridgeAgent` (Model Context Protocol Synthesizer)
* **Objective**: Transform standard, niche Python/TypeScript libraries that lack AI connectivity into fully compliant, secure FastMCP servers.
* **Core Tools**: `inspect_library_signatures`, `generate_fastmcp_server`, `audit_mcp_security`.
* **Guardrail**: Enforces strict input validation, zero privilege escalation, and schema compliance.

---

## ⚡ Pre-Action Invariant Hooks & In-Flight Enforcement (BẮT BUỘC TUÂN THỦ TỨC THÌ)

Để không bao giờ xảy ra tình trạng "việc đã rồi mới báo lỗi", TẤT CẢ các agent hoạt động trong workspace này PHẢI kích hoạt cơ chế tự kiểm tra chặn trước (**Pre-Action Interception**) và tuân thủ các nguyên tắc sau NGAY TRONG LÚC THỰC THI:

### 1. 🛑 Pre-Write Tool Hook (Chặn tạo file rác TRƯỚC KHI gọi tool)
* **Quy tắc**: Trước khi gọi bất kỳ tool tạo file nào (`write_to_file`, `create_file`...), AI PHẢI kiểm tra đường dẫn đích với Whitelist sau:
  - **Thư mục hợp lệ**: `crates/`, `docs/`, `rules/`, `plans/`, `scripts/`, `grants/`, `brainstorm/`, `research/`, `tests/`, `.github/`, `.agents/`.
  - **File nháp/tạm**: BẮT BUỘC ghi vào `scratch/` (gitignored).
  - **File ở root**: CHỈ ĐƯỢC PHÉP ghi vào các file đã định danh cố định (`Cargo.toml`, `Cargo.lock`, `CLAUDE.md`, `AGENTS.md`, `CONTRIBUTING.md`, `README.md`, `SECURITY.md`, `LICENSE.md`, `robots.txt`, `.gitignore`, `.gitattributes`, `.cursorrules`, `.windsurfrules`, `.geminirules`, `.agentrules`, `devin_instructions.md`, `pyproject.toml`, `.env.example`).
* **Hành vi bắt buộc**: Nếu đường dẫn nằm ngoài danh sách trên, **DỪNG LẠI NGAY LẬP TỨC**, chuyển hướng đường dẫn sang đúng thư mục quy định trước khi gọi tool.

### 2. 🔄 In-Flight Co-Update Hook (Sửa Code là BẮT BUỘC sửa Doc CÙNG LÚC)
* **Quy tắc**: Khi sửa đổi bất kỳ code nào trong `crates/`, AI **KHÔNG ĐƯỢC PHÉP** để dồn đến cuối phiên mới sửa doc.
* **Hành vi bắt buộc trong CÙNG MỘT PHIÊN LÀM VIỆC (In-Flight)**:
  1. Cập nhật code Rust trong `crates/`.
  2. Cập nhật ngay lập tức tài liệu tiếng Anh tương ứng trong `docs/` VÀ bản dịch tiếng Việt (`.vi.md`).
  3. Cập nhật ngay lập tức [`docs/sitemap_and_component_registry.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/docs/sitemap_and_component_registry.md) và [`docs/sitemap_and_component_registry.vi.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/docs/sitemap_and_component_registry.vi.md) nếu có symbol/file mới.

### 3. 📋 Real-Time Session Register Hook (Tự động duy trì sổ giao ban)
* **Bắt đầu**: Đọc [`plans/ACTIVE_SESSION_REGISTER.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/plans/ACTIVE_SESSION_REGISTER.md) đầu tiên để hiểu bối cảnh.
* **Kết thúc mỗi tác vụ**: Tự động ghi lại các hạng mục vừa hoàn thành và cập nhật danh sách việc tiếp theo vào [`plans/ACTIVE_SESSION_REGISTER.md`](file:///Volumes/Data/101.AI/GitHub/eco_support/plans/ACTIVE_SESSION_REGISTER.md) trước khi hoàn tất câu trả lời cho người dùng.

---

## 🛡️ Constitutional Guardrails & Safety Protocols

1. **Non-Intrusive Engagement**:
   - EcoSupport agents must never spam maintainers.
   - All AI-generated outputs (triage summaries, patches) must be explicitly watermarked with the reasoning trace and maintainer-first opt-in options.

2. **Supply-Chain Integrity**:
   - Any patch generated by `PatchSynthesizerAgent` is statically analyzed for security anti-patterns (no arbitrary `eval()`, no dynamic imports of unpinned packages, no network egress in unit tests).

3. **Telemetry & Privacy**:
   - Zero private repository scraping without explicit OAuth grant.
   - API keys and author identities are sanitized before processing.

