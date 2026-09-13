# Audit `plans/` và `research/` — Luna source audit

Ngày audit: 2026-09-12
Baseline: `93bf7288324dd746669ad09c5e2a592adc772748` (working tree sạch trước khi audit)
Phạm vi: các file hiện hữu trong `plans/` và `research/`, loại trừ `plans/260912-1936-luna-src-audit/`.
Phương pháp: đọc toàn văn file text/source; không sửa production source, không chạy build/test, không trích xuất secrets/session payload.

## Ledger đầy đủ

### 25 tracked files trong `plans/`

Đã đọc đủ (full text) tất cả các file sau:

1. `plans/2026-08-18-agentize/plan.md`
2. `plans/2026-08-18-agentize/release-checklist.md`
3. `plans/2026-08-20-ci-cd-upgrade.md`
4. `plans/2026-08-20-workspace-rationalization.md`
5. `plans/2026-08-21-execution-prompt.md`
6. `plans/2026-08-21-golden-standard-roadmap.md`
7. `plans/2026-08-21-redteam-review.md`
8. `plans/2026-08-21-review-handoff.md`
9. `plans/2026-08-21-warnings-plan.md`
10. `plans/2026-08-25-fix-issues-gaps.md`
11. `plans/260820-marketplace-extensions/plan.md`
12. `plans/260820-marketplace-extensions/reports/marketplace-redteam.md`
13. `plans/260820-marketplace-extensions/research/market-positioning.md`
14. `plans/260820-marketplace-extensions/research/marketplace-publishing.md`
15. `plans/ACTIVE_SESSION_REGISTER.md`
16. `plans/ARCHIVE-ecosupport-history.md`
17. `plans/HANDOVER-deepseek-v4-pro.md`
18. `plans/adr/001-v4-architecture.md`
19. `plans/adr/002-core-dependency-scope.md`
20. `plans/adr/003-dotnet-developer-platform-scope.md`
21. `plans/implementation-plan.md`
22. `plans/master-plan.md`
23. `plans/reports/agentize-agentization-map.md`
24. `plans/reports/agentize-decisions.md`
25. `plans/reports/research-260823-0647-cross-agent-claude-skills-reuse.md`

Phân loại: `ACTIVE_SESSION_REGISTER.md` là SSOT vận hành; ADR-002/003 là quyết định mới hơn và được ưu tiên khi mâu thuẫn với ADR-001; `2026-08-21-review-handoff.md` là backlog/DoD canonical; workspace-rationalization là cleanup manifest. `master-plan.md` và `implementation-plan.md` tự đánh dấu SUPERSEDED. Agentize files, archive và các bản EcoSupport là historical, cần giữ/đánh nhãn, không tự xóa. Marketplace plan là in-progress nhưng publish bị owner-blocked.

### 50 tracked files trong `research/`

Đã đọc đủ full text/source:

1. `research/benchmarks/triage_benchmark.py`
2. `research/data/niche_seed_registry.json`
3. `research/muc_tieu/1.md`
4. `research/muc_tieu/2.md`
5. `research/muc_tieu/2.txt`
6. `research/muc_tieu/3.md`
7. `research/muc_tieu/4.md`
8. `research/muc_tieu/5.md`
9. `research/niche_ecosystem_survey/criticality_model.py`
10. `research/niche_ecosystem_survey/survey_report_2026.md`
11. `research/python_prototype/__init__.py`
12. `research/python_prototype/agents/__init__.py`
13. `research/python_prototype/agents/doc_bridge_agent.py`
14. `research/python_prototype/agents/patch_synthesizer.py`
15. `research/python_prototype/agents/triage_agent.py`
16. `research/python_prototype/cli/__init__.py`
17. `research/python_prototype/cli/main.py`
18. `research/python_prototype/core/__init__.py`
19. `research/python_prototype/core/client.py`
20. `research/python_prototype/core/config.py`
21. `research/python_prototype/core/exceptions.py`
22. `research/python_prototype/core/telemetry.py`
23. `research/python_prototype/mcp/__init__.py`
24. `research/python_prototype/mcp/server.py`
25. `research/python_prototype/mcp/tools/ecosystem_tools.py`
26. `research/python_prototype/mcp/tools/security_auditor.py`
27. `research/python_prototype/radar/__init__.py`
28. `research/python_prototype/radar/health_analyzer.py`
29. `research/python_prototype/radar/models.py`
30. `research/python_prototype/radar/niche_scanner.py`

Đã inventory metadata (path/size/line count), không đọc/diễn giải bytecode (generated, binary) của 20 file sau:

31. `research/python_prototype/__pycache__/__init__.cpython-312.pyc`
32. `research/python_prototype/agents/__pycache__/__init__.cpython-312.pyc`
33. `research/python_prototype/agents/__pycache__/doc_bridge_agent.cpython-312.pyc`
34. `research/python_prototype/agents/__pycache__/patch_synthesizer.cpython-312.pyc`
35. `research/python_prototype/agents/__pycache__/triage_agent.cpython-312.pyc`
36. `research/python_prototype/cli/__pycache__/__init__.cpython-312.pyc`
37. `research/python_prototype/cli/__pycache__/main.cpython-312.pyc`
38. `research/python_prototype/core/__pycache__/__init__.cpython-312.pyc`
39. `research/python_prototype/core/__pycache__/client.cpython-312.pyc`
40. `research/python_prototype/core/__pycache__/config.cpython-312.pyc`
41. `research/python_prototype/core/__pycache__/exceptions.cpython-312.pyc`
42. `research/python_prototype/core/__pycache__/telemetry.cpython-312.pyc`
43. `research/python_prototype/mcp/__pycache__/__init__.cpython-312.pyc`
44. `research/python_prototype/mcp/__pycache__/server.cpython-312.pyc`
45. `research/python_prototype/mcp/tools/__pycache__/ecosystem_tools.cpython-312.pyc`
46. `research/python_prototype/mcp/tools/__pycache__/security_auditor.cpython-312.pyc`
47. `research/python_prototype/radar/__pycache__/__init__.cpython-312.pyc`
48. `research/python_prototype/radar/__pycache__/health_analyzer.cpython-312.pyc`
49. `research/python_prototype/radar/__pycache__/models.cpython-312.pyc`
50. `research/python_prototype/radar/__pycache__/niche_scanner.cpython-312.pyc`

Research `muc_tieu/1–5` là đề xuất kiến trúc DataGuard đời đầu; `python_prototype`, benchmark và niche survey là EcoSupport research độc lập, không phải evidence runtime của DataGuard. Không được import chúng vào `src/` theo `plans/ACTIVE_SESSION_REGISTER.md:67-70` và governance.

## Requirement map và evidence source

| Cam kết | Evidence source hiện tại | Mức tin cậy |
|---|---|---|
| DataGuard .NET 9 là product canonical; CLI/Core/adapters/analyzers/extensions | `plans/ACTIVE_SESSION_REGISTER.md:9-35` | Cao cho định hướng; trạng thái số liệu trong file là snapshot cũ |
| Full/Snapshot/Manual, Snapshot mặc định | `src/DataGuard.Core/Models/Configuration.cs:8-52`; `src/DataGuard.Cli/Program.cs:80-100,938-960` | Cao |
| Manual contract attributes | `src/DataGuard.Contracts/ContractAttributes.cs:45-53`; `src/DataGuard.Core/Sources/ManualContractSource.cs:11-36` | Cao |
| Baseline và persisted schema snapshot | `src/DataGuard.Core/Baseline/BaselineManager.cs:22-70,204-375`; `src/DataGuard.Cli/Program.cs:268-307` | Cao, nhưng cần test runtime mới |
| SARIF CLI/host output | `src/DataGuard.Core/Reporting/SarifTypes.cs:15-113`; `src/DataGuard.Cli/Program.cs:770-779`; `src/DataGuard.VSCode/src/extension.ts:13-38,256-300` | Cao |
| Rule wiring provider | `src/DataGuard.Cli/Program.cs:1024,1111-1188`; `tests/DataGuard.GoldenCorpus.Tests/RuleCoverageTests.cs:85-194` | Trung bình-cao; wiring không thay thế DB integration evidence |
| Oracle readers/ref-cursor/length semantics | `src/DataGuard.Oracle.Adapter/OracleReaders.cs:10-20,339-380,666-796`; `LengthMismatch.cs:9-18,136-180` | Trung bình; DB thật chưa xác minh trong audit |
| Assessment CLI/API, local-first/evidence contract | `src/DataGuard.Cli/Program.cs:679-779`; `src/DataGuard.Core/Assessment/AssessmentEngine.cs:10-81`; `AssessmentContracts.cs:3-130` | Cao về code surface |
| ADR-002 layering | `plans/adr/002-core-dependency-scope.md`; source Contracts/Analyzers/Core | Cao |

## Findings (không nâng proposal thành bug)

### F1 — Ưu tiên P2, confidence cao: tài liệu canonical chứa snapshot verification cũ

`ACTIVE_SESSION_REGISTER.md:39-70` ghi build/test/coverage/CI của các commit cũ (`6502992`, `f8adbc3`); `review-handoff.md:13-18` còn ghi 80/80 và 8 warnings. Baseline audit hiện tại là `93bf728`; chưa chạy build/test theo phạm vi được giao. Vì vậy các số liệu này chỉ là historical evidence, không được báo cáo như trạng thái live.

**Khuyến nghị:** cập nhật register sau lần verification có output thật; tách rõ `verified-at-commit` và `current-unverified`.

### F2 — Ưu tiên P2, confidence cao: ADR-001 có nội dung đã bị supersede và link không tồn tại

ADR-001 vẫn ghi Core “zero vendor deps” tại `plans/adr/001-v4-architecture.md:60`, trong khi ADR-002 nêu rõ điều ngược lại và đánh dấu supersede. ADR-001 còn tham chiếu `adr/002-module-boundaries.md`, `adr/003-security-model.md`, `adr/004-test-strategy.md` tại dòng 205–208; các file này không nằm trong ledger.

**Khuyến nghị:** thêm supersession banner vào ADR-001, sửa link sang ADR-002/003 và các tài liệu thực tế. Khi mâu thuẫn, ưu tiên ADR-002/003, không phải ADR-001.

### F3 — Phân loại lịch sử, không phải defect hiện hành: proposal/research EcoSupport

`plans/2026-08-18-agentize/*`, `plans/reports/agentize-*`, `research/python_prototype/**`, `research/niche_ecosystem_survey/**` mô tả Rust/Python/TypeScript, MCP, Anthropic key và telemetry. Chúng là historical/independent research theo `plans/2026-08-20-workspace-rationalization.md:42-69`, không phải capability DataGuard hiện hành.

**Khuyến nghị:** giữ nguyên lịch sử và các nhãn hiện có; chỉ đề xuất bổ sung nhãn nơi thực sự thiếu. Sự tồn tại của research độc lập không phải lỗi sản phẩm; không xóa chỉ vì không có callsite.

### F4 — Ưu tiên P2, confidence cao về code: schema hash có hai semantics cần đối chiếu contract

`BaselineManager.cs:204+` có hash từ violations và `:221+` có hash từ schema snapshot; CLI chọn schema tại `Program.cs:415` nhưng fallback violation hash tại `:441-443`. CLI vẫn in “pass `--fail-on-drift`” tại `Program.cs:467`. Đây là điểm cần policy/test, không kết luận source bug chỉ từ tài liệu.

**Khuyến nghị:** tài liệu hóa rõ schema hash canonical, legacy fallback, và policy CI/banking cho fail-on-drift; thêm test DDL change nếu chưa có evidence.

### F5 — Giới hạn xác minh: DB integration và marketplace publish chưa có evidence live

Research/plan đề xuất Testcontainers Oracle/SQL Server và marketplace artifacts, nhưng package reference hoặc kế hoạch không chứng minh runtime integration/public deployment. Marketplace docs nêu owner prerequisites và fail-loud behavior tại `plans/260820-marketplace-extensions/research/marketplace-publishing.md`.

**Khuyến nghị:** chỉ claim đã tích hợp/publish sau CI run hoặc artifact smoke evidence; owner cần `NUGET_USER`, `VSCE_PAT`, `VS_MARKETPLACE_PAT`/publisher verification.

### F6 — Ưu tiên P3, confidence cao: generated `.pyc` đang tracked

Có 20 `.pyc` trong `research/python_prototype/**/__pycache__`. Governance cấm coi generated state là documentation/source; nhưng absence search không đủ quyền xóa.

**Khuyến nghị:** lập manifest `path → remove/keep` và owner approval trước cleanup; không thực hiện trong audit.

### F7 — Ưu tiên P2, confidence trung bình: claim hiệu năng chưa có benchmark evidence

Research `muc_tieu/4.md:22` và ADR-001:24 dùng “dưới 1 giây/~ms”; không thấy benchmark DataGuard tương ứng trong `research/` (benchmark hiện hữu là EcoSupport triage). Đây là claim chưa chứng minh, không phải kết luận hiệu năng sai.

**Khuyến nghị:** bỏ claim khỏi marketing cho tới khi có BenchmarkDotNet/CI measurement và ngưỡng định nghĩa.

## Unresolved / cần owner hoặc verification

- Worker này không chạy build/test; xem [verification hiện tại](verification.md). Audit không đo coverage mới.
- Chưa verify GitHub CI, CodeQL, release workflow hay Marketplace deployment thật.
- Chưa có evidence DB integration runtime cho Oracle/SQL Server/ref cursor trong phạm vi này.
- Chưa quyết định owner về mức rewrite narrative historical trong `plans/` và `research/`.
- Chưa có manifest/approval cleanup 20 `.pyc`; không tự xóa.
- Cần thống nhất schema-hash/fail-on-drift policy và cập nhật docs sau khi test.

## Handoff

Đã đọc hoàn chỉnh: 25/25 plan files và 30/30 text/source research files; 20/20 binary `.pyc` đã inventory metadata, không đọc bytecode. Báo cáo này là thay đổi duy nhất trong audit scope; không có production source edit và không chạy test.
