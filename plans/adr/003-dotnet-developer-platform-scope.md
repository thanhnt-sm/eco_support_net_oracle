# ADR-003: .NET Developer Platform Scope — Assessment Surface

**Date**: 2026-08-23
**Status**: Accepted
**Context docs**: `docs/product-discovery/{baseline,source-inventory,dependency-map,code-capabilities,dotnet-legacy-evidence,problem-market-evidence,opportunity-backlog,capability-matrix}.md`

## Context

DataGuard hiện là contract validator Entity↔SP/Raw SQL với delivery surface: CLI (`System.CommandLine`), programmatic API (`ValidationPipeline`), Roslyn analyzer (netstandard2.0), VS Code extension, VS package. Yêu cầu: mở rộng thành bộ công cụ hỗ trợ lập trình viên C#/.NET cho codebase legacy (.NET Framework/SDK cũ) mà không phát minh surface mới.

## Decisions

### D1 — Delivery surface: CLI command mới + API extension, không tạo surface song song

Assessment được expose qua:
- CLI: subcommand `assess` trong `src/DataGuard.Cli/Program.cs`, theo pattern command/options hiện có.
- API: method trên `ValidationPipeline`/factory trong `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`.
Không tạo frontend, daemon hay persistence layer mới.

### D2 — Version support boundary

Assessment targets: .NET Framework 4.6.2–4.8.1, SDK-style projects, netstandard2.0 libraries. Support claims chỉ từ curated table committed trong repo (source URL + retrieval date + range + rule id). Unknown → `Unknown`; không suy đoán từ family name.

### D3 — Data handling: local-first; network opt-in và timeout-bound

Mặc định 100% local. Remote advisory/vulnerability lookup chỉ bật bằng config/flag hiện có, có timeout; failure trả partial result kèm provider/timestamp/error. Không credential lưu cache ngoài vị trí cache hiện có của product.

### D4 — Uncertain findings policy

Mọi finding mang evidence + confidence; không bao giờ claim remediation certainty. Read-only: assessment không sửa solution/project/package/config/source; auto-fix nằm ngoài scope release này.

## Consequences

- Composition root duy nhất cho wiring: CLI Program.cs + PublicApiSurface.cs.
- Mọi capability phải thỏa refusal/error behavior trong `capability-matrix.md` trước khi vào implementation.
- Fixture matrix 4 project styles là acceptance bắt buộc cho core report contract.
