> **⚠️ ARCHIVED** — This document describes the previous EcoSupport Rust/Python product. It does not apply to DataGuard (.NET). See [README](../../README.md) for current documentation.

[English](sitemap_and_component_registry.md) | [Tiếng Việt](sitemap_and_component_registry.vi.md)

# Bản Đồ Workspace & Danh Mục Thành Phần Sống (Sitemap & Registry)

**Mã tài liệu**: `REGISTRY-SITEMAP-2026.1`  
**Mục đích**: Danh mục toàn diện, mô tả chi tiết từng thành phần và bản đồ điều hướng cho tất cả các file trong workspace EcoSupport.

---

## 🗺️ Bản Đồ Thư Mục Gốc & Danh Mục File

| Đường dẫn / File | Mục Đích & Trách Nhiệm | Tầng Kiến Trúc | Ngôn Ngữ Chính |
| :--- | :--- | :---: | :---: |
| **`Cargo.toml`** | File cấu hình Cargo workspace quản lý 5 crates, cấu hình tối ưu hóa biên dịch (LTO, strip), và các dependency dùng chung. | Root Config | TOML |
| **`CLAUDE.md`** | Hướng dẫn vận hành chính cho Claude Code, Claude Desktop, và các vòng lặp agent Anthropic API. | Quản trị | Markdown |
| **`AGENTS.md`** | Đặc tả kiến trúc Multi-Agent swarm và các rào chắn hiến pháp an toàn. | Quản trị | Markdown |
| **`.cursorrules`** | Quy tắc AI và chuẩn mã nguồn cho Cursor IDE. | Quản trị | Markdown |
| **`.windsurfrules`** | Quy tắc Cascade cho Windsurf IDE. | Quản trị | Markdown |
| **`.geminirules`** | Quy tắc cho trợ lý Google Gemini Code Assist. | Quản trị | Markdown |
| **`LICENSE.md`** | Giấy phép PolyForm Noncommercial 1.0.0 kèm điều khoản cấm train AI độc hại. | Pháp lý / IP | Markdown |
| **`README.md`** / **`README.vi.md`** | Tài liệu tổng quan dự án, kiến trúc hệ thống và hướng dẫn bắt đầu nhanh (Song ngữ EN/VI). | Tài liệu | Markdown |
| **`CONTRIBUTING.md`** / **`CONTRIBUTING.vi.md`** | Hướng dẫn đóng góp & nguyên tắc ưu tiên maintainer (Song ngữ EN/VI). | Tài liệu | Markdown |
| **`SECURITY.md`** / **`SECURITY.vi.md`** | Chính sách công bố lỗ hổng bảo mật và cam kết SLA phản hồi (Song ngữ EN/VI). | An ninh | Markdown |
| **`robots.txt`** | Chỉ thị chặn các web crawler thu thập dữ liệu AI bất hợp pháp. | An ninh | Text |
| **`.gitattributes`** | Chuẩn hóa kết thúc dòng (EOL) và quy tắc export-ignore bảo vệ bí mật. | Git / SCM | SCM |
| **`.gitignore`** | Bộ quy tắc loại trừ cho build artifact Rust, thư mục ảo Python và scratchpad. | Git / SCM | SCM |
| **`rules/universal_ai_constitution.md`** | Hiến pháp phổ quát ràng buộc tất cả các mô hình AI khi làm việc trong workspace. | Quản trị | Markdown |
| **`rules/workspace_governance.md`** | Bộ quy tắc bất biến về phân chia thư mục và ranh giới kiến trúc. | Quản trị | Markdown |
| **`rules/doc_sync_enforcement.md`** | Chuẩn đồng bộ tài liệu liên tục & bắt buộc duy trì song ngữ EN/VI. | Quản trị | Markdown |
| **`.agentrules`** | Quy tắc vận hành chung cho Devin.ai, OpenCode, Oh-My-Pi, và các AI agent khác. | Quản trị | Markdown |
| **`devin_instructions.md`** | Quy trình làm việc chi tiết từng bước dành riêng cho Devin.ai. | Quản trị | Markdown |
| **`plans/ACTIVE_SESSION_REGISTER.md`** | **Nguồn Chân Lý Duy Nhất (SSoT)** ghi nhận trạng thái qua các phiên làm việc của AI. | Quản trị | Markdown |
| **`rules/small_model_operational_protocol.md`** | Quy trình khép kín kết hợp compiler (Compiler-in-the-Loop) giúp AI nhỏ tự sửa lỗi. | Quản trị | Markdown |
| **`crates/eco-core/`** | Cấu hình cốt lõi, client Claude 3.7 API, quản lý token, telemetry. | Production Core | Rust |
| **`crates/eco-radar/`** | Cỗ máy tính toán chỉ số nguy cấp ECI, mô hình toán học, scanner đa registry. | Production Radar | Rust |
| **`crates/eco-mcp/`** | Server FastMCP 2.0 / `rmcp`, tool handler, và công cụ quét an ninh tĩnh. | Production MCP | Rust |
| **`crates/eco-agents/`** | Swarm subagent tự động: Khám nghiệm (Triage), Sinh bản vá (Patch), Cầu nối (Bridge). | Production Agents | Rust |
| **`crates/eco-cli/`** | Binary CLI dòng lệnh siêu tốc (3.7MB) hiển thị Terminal trực quan. | Production CLI | Rust |
| **`crates/eco-cli/tests/`** | Bộ integration test toàn diện xác minh tính đúng đắn của toàn bộ 5 crates. | QA / Testing | Rust |
| **`docs/overview/vibe_coder_guide.md`** / **`.vi.md`** | Sổ tay trực quan cho Vibe Coder (sơ đồ tư duy, phép ẩn dụ, cheatsheet). | Tài liệu | Markdown |
| **`docs/architecture/system_architecture.md`** / **`.vi.md`** | Bản thiết kế kiến trúc hệ thống chính thức với 6 sơ đồ Mermaid. | Tài liệu | Markdown |
| **`docs/architecture/tech_stack_evaluation.md`** / **`.vi.md`** | Đánh giá công nghệ & benchmark ngôn ngữ (Rust vs Zig vs Go vs C++ vs Mojo). | Tài liệu | Markdown |
| **`docs/architecture/agent-config.md`** / **`.vi.md`** | Cấu hình agent model miễn phí opencode zen, quy tắc routing, tích hợp Rust. | Tài liệu | Markdown |
| **`docs/operations/playbook_and_runbook.md`** / **`.vi.md`** | Cẩm nang SRE, ma trận khắc phục sự cố, diễn tập khôi phục thảm họa. | Tài liệu | Markdown |
| **`docs/testing/qa_test_strategy.md`** / **`.vi.md`** | Triết lý kiểm thử QA, kim tự tháp test, lệnh thực thi tự động. | Tài liệu | Markdown |
| **`docs/developers/contributor_deep_dive.md`** / **`.vi.md`** | Cẩm nang chuyên sâu cho Developer, API crate, cách tạo FastMCP tool mới. | Tài liệu | Markdown |
| **`docs/sitemap_and_component_registry.md`** / **`.vi.md`** | Danh mục bản đồ workspace và thành phần sống (Song ngữ EN/VI). | Tài liệu | Markdown |
| **`brainstorm/expert_council_redteam.md`** | Hội đồng 5 chuyên gia phản biện Red Team chiến lược dự án. | Chiến lược | Markdown |
| **`brainstorm/product_vision_and_niche_strategy.md`** | Định nghĩa sản phẩm, lợi thế độc quyền và lộ trình thắng giải Anthropic. | Chiến lược | Markdown |
| **`grants/written_explanation.md`** | Bài giải trình 412 từ chính thức cho track Claude Ecosystem Impact. | Hồ sơ Grant | Markdown |
| **`grants/ecosystem_impact_matrix.md`** | Ma trận định lượng các lĩnh vực ngách và ROI điện toán. | Hồ sơ Grant | Markdown |
| **`grants/grant_pitch.md`** | Tuyên bố thuyết trình điều hành cho hội đồng chấm giải Anthropic. | Hồ sơ Grant | Markdown |
| **`grants/SUBMISSION_CHECKLIST.md`** | Danh mục kiểm tra tiền nộp hồ sơ xin tài trợ Anthropic Grant. | Hồ sơ Grant | Markdown |
| **`research/niche_ecosystem_survey/`** | Báo cáo khảo sát hiểm họa mã nguồn mở 2026 và mô hình toán độc lập. | Nghiên cứu | Markdown/Python |
| **`research/benchmarks/`** | Bộ script benchmark thực nghiệm đánh giá Claude 3.7 Extended Thinking. | Nghiên cứu | Python |
| **`research/data/`** | Tập dữ liệu hạt giống JSON của các thư viện mã nguồn mở có rủi ro cao. | Nghiên cứu | JSON |
| **`scripts/git_sync.sh`** | 1-click tự động hóa format, kiểm tra lỗi, commit và đẩy code an toàn lên Git. | Công cụ | Bash |
| **`scripts/git_conflict_resolver.sh`** | Công cụ tự động chẩn đoán và hướng dẫn gỡ xung đột Git 3 chiều. | Công cụ | Bash |
| **`scripts/verify_docs_sync.sh`** | Script tự động xác minh toàn bộ tài liệu và bản dịch song ngữ đầy đủ. | Công cụ | Bash |
| **`scripts/anti_garbage_guard.sh`** | **Anti-Garbage Guard** — chặn commit chứa file rác ngoài phân vùng cho phép. | Công cụ | Bash |
| **`scripts/preflight_agent_check.sh`** | **Pre-Flight Invariant Checker** — công cụ kiểm tra sức khỏe và độ sạch root tức thì cho AI agent. | Công cụ | Bash |
| **`scripts/demo_scan.sh`** | **Live CLI Demo Script** — script demo quét ECI đa danh mục phục vụ quay video và nộp hồ sơ. | Công cụ | Bash |
| **`.githooks/pre-commit`** | Git hook chạy `cargo fmt` và `ruff` trước khi commit. | Công cụ | Bash |
| **`.githooks/pre-push`** | Git hook chạy `cargo check` trước khi đẩy lên remote. | Công cụ | Bash |

