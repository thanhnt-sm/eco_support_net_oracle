---
title: "Extension AI/LLM & Provider Audit and Architecture Brainstorm"
date: 2026-10-14
type: brainstorm-audit
status: completed
skills_used:
  - ck:brainstorm
  - ck:scout
  - ck:research
  - ck:ask
  - ck:council
  - ck:docs
  - ck:project-organization
typesafe_verification:
  judge: "uses_ai_or_llm=option_0 (no_100_percent_deterministic, confidence=1.0), provider_requirement=option_0 (no_ai_provider_only_db_provider, confidence=1.0)"
  bicameral_advisor: "AUTO_APPROVE / proceed (confidence=0.98)"
  ck_advisor: "proceed (confidence=1.00)"
---

# Extension AI/LLM & Provider Audit Report

## 1. Executive Summary (Direct Answer)

1. **Có dùng AI / LLM không?** **KHÔNG (0% AI / LLM).**
   - Toàn bộ workspace `eco_support_net_oracle` (**DataGuard v0.4.0**) và cả 2 IDE extensions đang được build (`DataGuard.VSCode` và `DataGuard.VisualStudio`) hoạt động **100% deterministic (xác định tĩnh)** bằng Roslyn syntax/semantic analyzers, bộ phân tích cú pháp SQL (SQL lexer/AST, `Microsoft.SqlServer.TransactSql.ScriptDom`), và đối chiếu schema metadata.
   - Không có bất kỳ SDK AI/LLM nào (`OpenAI`, `Anthropic`, `Claude`, `Gemini`, `Ollama`, `SemanticKernel`, `Microsoft.Extensions.AI`, `LangChain`, hay `MCP SDK`) trong bất kỳ file `.csproj` hay `package.json` nào của toàn bộ 17 projects dưới `src/`.
   - Extension **không gửi code/SQL lên bất kỳ AI server nào**, không gọi LLM API ở runtime hay build time.

2. **Có cần Provider nào không?**
   - **AI / LLM Provider:** **KHÔNG CẦN** (không cần OpenAI API key, Anthropic API key, Gemini key, Copilot hay Ollama).
   - **Database Provider (`dataguard.provider` / `--provider`):** Trong mã nguồn và cấu hình của DataGuard, từ khóa **`provider`** hoàn toàn mang nghĩa là **Hệ quản trị cơ sở dữ liệu (Database Engine Dialect)**:
     - Giá trị hỗ trợ: `sqlserver` (mặc định), `postgresql`, `mysql`, `oracle`.
     - **Khi nào KHÔNG cần DB Provider / Connection?** Khi gõ code trực tiếp trong IDE (In-editor diagnostics): Roslyn Analyzer (`DataGuard.Analyzers`) và Language Server (`DataGuard.LanguageServer`) phân tích cú pháp C#/SQL ngay trên bộ nhớ (in-memory) để bắt các lỗi như `DG017` (`SELECT *`), `DG004`, `DG101`, `DGSQL001` mà **không cần DB connection hay file snapshot**.
     - **Khi nào CẦN DB Provider (`sqlserver` | `postgresql` | `mysql` | `oracle`)?** Khi chạy kiểm tra hợp đồng schema (`validate`, `snapshot`, `baseline`, `scan`, `verify-shape`) để bắt lỗi lệch bảng/cột/kiểu dữ liệu (`DG001`–`DG020`). Ngay cả khi đó, ở chế độ mặc định của IDE (`--ide-safe`), extension ưu tiên đọc **Offline Schema Snapshot** (`.dataguard-snapshot.json` hoặc EF Core `*ModelSnapshot.cs`) chứ không tự ý mở kết nối mạng/DB, trừ khi người dùng chủ động bấm xác nhận chạy lệnh Live DB (`snapshot`, `baseline`, `verify-shape`) với biến môi trường chứa chuỗi kết nối DB (`connectionEnv`, mặc định `DATAGUARD_CONNECTION`).

---

## 2. Evidence Matrix Across All Workspace Projects

| Component / Project | Target / Artifact | Mechanism | AI / LLM SDKs | "Provider" Meaning & Requirement |
|---|---|---|---|---|
| **VS Code Extension** (`src/DataGuard.VSCode`) | `dataguard-vscode-*.vsix` (`node` + TypeScript) | Spawns local `dataguard` CLI (`--ide-safe`) + starts `DataGuard.LanguageServer.dll` over stdio (`vscode-languageclient ^10.1.2`) | **None** (`package.json:255-264`) | `dataguard.provider` enum: `["sqlserver", "postgresql", "mysql", "oracle"]` (`package.json:207-218`). DB dialect only. |
| **Language Server** (`src/DataGuard.LanguageServer` + `src/DataGuard.SqlClassification`) | `server/DataGuard.LanguageServer.dll` (`net9.0`) | Deterministic lexical/AST scanner (`SqlClassifier.Classify`, `SqlTokenizer`, `CSharpStringLiteralScanner`) over JSON-RPC stdio | **None** (`DataGuard.LanguageServer.csproj:1-11`) | **None required.** Operates 100% in-memory without DB, network, or provider access (`SqlClassifier.cs:8`). |
| **Visual Studio Extension** (`src/DataGuard.VisualStudio`) | `DataGuard.VisualStudio.vsix` (`net472` VS 2022) | Bundles `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, and self-contained `win-x64` `cli\dataguard.exe` (`--ide-safe --format sarif`) | **None** (`DataGuard.VisualStudio.csproj:20-29`) | Uses `.dataguard.yml` `provider` (`oracle`/`sqlserver`/`mysql`/`postgresql`) via local CLI in `--ide-safe` snapshot mode (`CliArgumentBuilder.cs:88-103`). |
| **Roslyn Analyzers & CodeFixes** (`src/DataGuard.Analyzers`, `src/DataGuard.CodeFixes`) | `netstandard2.0` Roslyn Analyzer DLLs | Compiler-in-the-loop syntax/semantic analysis (`ContractValidationAnalyzer.cs`) & AST code fixes (`CodeFixProviders.cs`) | **None** (`DataGuard.Analyzers.csproj:23-34`) | **None required.** Runs inside `csc.exe` / IDE Roslyn host with zero I/O or network calls. |
| **CLI & Core Engine** (`src/DataGuard.Cli`, `src/DataGuard.Core`) | `dataguard` CLI (`net9.0`) | 13 deterministic subcommands (`validate`, `assess`, `scan`, `verify-shape`, `snapshot`, `baseline`, etc.) + `ConcurrentValidationEngine` | **None** (`DataGuard.Core.csproj:21-37`) | Requires DB dialect provider (`oracle`, `sqlserver`, `postgresql`, `mysql`) + offline snapshot or opt-in DB connection string. Optional secret stores (DPAPI, KeyVault, AWS Secrets Manager, HashiCorp Vault) for DB passwords. |
| **DB Adapters** (`src/DataGuard.{Oracle,SqlServer,PostgreSql,MySql}.Adapter`) | `.NET 9.0` class libraries | `Oracle.ManagedDataAccess.Core`, `Microsoft.Data.SqlClient` + `ScriptDom`, `Npgsql`, `MySqlConnector` | **None** | Live DB catalog readers & `SchemaOnly` query shape inspectors when user runs live DB commands. |

---

## 3. Why "AI", "MCP", or "API Key" Appear in Specific Files (Disambiguation)

Qua rà soát toàn bộ repo bằng `ck:scout`, có 3 chỗ xuất hiện từ khóa liên quan đến AI/API Key nhưng **không phải** là extension dùng AI:

1. **DataGuard là công cụ *bắt lỗi* SQL do AI sinh ra (Anti-AI-Hallucination):**
   - Trong `src/DataGuard.Analyzers/DiagnosticDescriptors.cs:219,228`, `src/DataGuard.Core/Rules/PhantomTableRule.cs:8-9` (`DG015`), và `src/DataGuard.Core/Rules/PhantomColumnRule.cs:8-15` (`DG016`), mô tả rule ghi rõ: phát hiện bảng/cột "ma" (phantom table/column) — *"a common AI-hallucination failure mode"* khi lập trình viên dùng Copilot/ChatGPT/Claude viết câu lệnh SQL. Tức là DataGuard dùng thuật toán tĩnh để **kiểm chứng code do AI viết**, chứ DataGuard không gọi AI.
2. **Regex che giấu thông tin nhạy cảm (Secret Redaction & Secret Scanning):**
   - Các file `src/DataGuard.VSCode/src/security.ts:4,7`, `src/DataGuard.VSCode/src/ui/redaction.ts:8,17`, `src/DataGuard.Core/Assessment/Internal/SecretsPack.cs:12,15`, và `src/DataGuard.Core/Reporting/DiagnosticEmitter.cs:290-294` có chứa từ khóa `api_key`, `apikey`, `Authorization: Bearer`. Đây là bộ lọc bảo mật để tự động che (`[REDACTED]` / `***`) mật khẩu hoặc API key nếu vô tình xuất hiện trong chuỗi kết nối/log trước khi hiển thị lên UI.
3. **Tài liệu lịch sử đã lưu trữ (`ARCHIVED` Legacy Docs):**
   - Các file `docs/mcp.md:1`, `docs/cli.md:1`, và `docs/overview/vibe_coder_guide.md:1` có nhắc tới `ANTHROPIC_API_KEY` và `eco-support mcp-serve`. Tuy nhiên, ngay dòng số 1 của cả 3 file đều có cảnh báo:
     > `> ⚠️ ARCHIVED — This document describes the previous EcoSupport Rust/Python product. It does not apply to DataGuard (.NET).`
   - Theo `plans/ARCHIVE-ecosupport-history.md`, dự án đã pivot hoàn toàn sang `.NET` DataGuard từ tháng 8/2026 và xóa toàn bộ code Rust/Node cũ.

---

## 4. Architectural Brainstorm & Options (YAGNI / KISS / DRY)

Dựa trên đánh giá từ **TypeSafe Judge**, **TypeSafe Bicameral Advisor**, và **ck:advisor**, nếu bạn đang cân nhắc định hướng cho các bản build Extension tiếp theo, có 3 phương án kiến trúc:

### Option A: Giữ nguyên kiến trúc 100% Deterministic & Offline-First (Recommended — Score: 9.8/10)
- **Cơ chế:** Không tích hợp bất kỳ LLM/AI provider nào vào extension. Tập trung hoàn thiện khả năng phân tích tĩnh và đối chiếu Ground-Truth Schema (như kế hoạch đang triển khai tại `plans/261014-0750-ide-extension-db-phase/`: tự động đọc EF Core `*ModelSnapshot.cs` trong `--ide-safe`, đóng gói đầy đủ Roslyn 4.12.0 ABI cho Visual Studio VSIX, và hỗ trợ lệnh Live DB tường minh).
- **Ưu điểm:**
  - Tốc độ phản hồi tức thì (<150ms trong editor), chi phí token = $0.
  - Không cần cấu hình API key, chạy hoàn toàn offline/air-gapped (đáp ứng chuẩn bảo mật ngân hàng/doanh nghiệp).
  - Độ chính xác 100% dựa trên AST và DB Catalog thật, không bao giờ bị AI hallucinate kết quả kiểm tra.
- **Nhược điểm:** Các gợi ý sửa lỗi tự động (CodeFix) chỉ áp dụng cho các mẫu AST xác định (như thêm `[SqlContract]` hoặc thay `SELECT *` khi đã biết danh sách cột từ snapshot).

### Option B: Bổ sung chế độ AI-Consumer qua MCP Server mỏng (`dataguard mcp`) (Score: 8.5/10)
- **Cơ chế:** Extension và CLI vẫn **không cần AI provider**, nhưng bổ sung lệnh `dataguard mcp` (stdio JSON-RPC) để các AI Agent bên ngoài mà lập trình viên sẵn có (Claude Code, Cursor, GitHub Copilot, Windsurf) có thể gọi DataGuard làm công cụ kiểm tra hợp đồng SQL (`validate`, `scan`, `verify-shape`) trước khi AI trả code cho người dùng.
- **Ưu điểm:** Biến DataGuard thành "hàng rào kiểm định" (deterministic guardrail) cho AI coding agents mà vẫn giữ nguyên nguyên tắc Zero-AI-Dependency bên trong DataGuard.
- **Nhược điểm:** Cần duy trì thêm một lớp giao thức MCP stdio mỏng trên `DataGuard.Cli`.

### Option C: Tích hợp Optional BYOK / Local LLM Provider vào Extension để gợi ý viết lại SQL phức tạp (Score: 2.0/10 — Không khuyến nghị)
- **Cơ chế:** Cho phép người dùng nhập API key (OpenAI/Anthropic/Gemini) hoặc trỏ vào local Ollama trong phần Settings của Extension để nhờ LLM giải thích lỗi hoặc viết lại câu SQL vi phạm.
- **Ưu điểm:** Có thể gợi ý sửa các câu truy vấn phức tạp chưa có trong mẫu AST.
- **Nhược điểm (Vi phạm YAGNI/KISS):** Làm phình to extension, phát sinh rủi ro lộ lọt schema DB nội bộ ra ngoài mạng (data exfiltration), phá vỡ cam kết `--ide-safe` (Zero Network / Zero Telemetry), và trùng lặp chức năng với Copilot/Cursor/Claude vốn đã có sẵn trong IDE của người dùng.

---

## 5. Unresolved Questions & Next Steps

1. Bạn hỏi câu này để **xác nhận điều kiện vận hành/cài đặt bản VSIX hiện tại** (tức chỉ cần cài `.vsix` + cấu hình Database Provider nếu muốn kiểm tra schema), hay bạn đang **muốn thiết kế thêm tính năng liên quan đến AI/MCP** cho extension?
2. Đối với Database Provider (`sqlserver`, `postgresql`, `mysql`, `oracle`), bạn có muốn tiếp tục triển khai ngay kế hoạch `plans/261014-0750-ide-extension-db-phase/` (tự động nhận diện EF Core `ModelSnapshot.cs` không cần cấu hình DB connection và thêm các nút lệnh DB cho Visual Studio VSIX) không?
