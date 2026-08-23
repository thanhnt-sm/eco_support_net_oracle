# Opportunity backlog

Mỗi opportunity phải map đồng thời: (a) seam thật trong `code-capabilities.md`, (b) evidence trong `problem-market-evidence.md` hoặc `dotnet-legacy-evidence.md`. Item thiếu một trong hai bị loại.

## Retained

| id | Persona | Target range | Problem | Evidence | Proposed observable outcome | Existing seam | Dependencies | Security/privacy impact | Acceptance criterion |
|---|---|---|---|---|---|---|---|---|---|
| OPP-1 | Backend .NET maintainer | .NET Framework 4.6.2–4.8.1; SDK-style netstandard2.0/net9.0 | Không biết chính xác target framework/package format/SDK pinning của từng project; family name không đủ suy support status (4.6.1 retired, 4.6.2 đến 2027-01-13). | `dotnet-legacy-evidence.md` §1, §2; `code-capabilities.md` Delivery surface | Báo cáo inventory per-project: TFM(s), package format, lock file, SDK pin, legacySupportStatus từ bảng versioned; unknown → `Unknown`. | CLI command registration (`Program.cs`); config model (`DataGuardConfiguration`); structured report (`SarifLog`, `ContractViolation`) | none beyond existing Core | read-only local files; không network | Chạy trên fixture 4 kiểu project → report schema đầy đủ; project hỏng không abort siblings |
| OPP-2 | Full-stack maintainer giữ API/DB contract | .NET Framework + EF/SP solutions | Contract drift giữa entity và SP/raw SQL gây lỗi runtime cho client; cần evidence thay đổi contract trước khi deploy. | `problem-market-evidence.md` hàng Redgate/SqlPackage; `code-capabilities.md` Inputs and rules | Report drift/breaking-change findings với rule ID, severity, evidence span; SARIF output ổn định. | Built-in rules (`ContractRules.cs`), `DiagnosticEmitter`, baseline (`BaselineManager`) | OPP-1 inventory facts | đọc source/schema local; không gửi connection string ra log | Positive/negative/missing-data fixtures cho từng rule pack; partial failure giữ finding độc lập |
| OPP-3 | CI maintainer của solution mixed-style | SDK pinning + PackageReference/lock file | Build reproducibility drift: SDK/lock/matrix lệch khỏi yêu cầu project. | `dotnet-legacy-evidence.md` §3–§4; `problem-market-evidence.md` hàng CI | Finding so sánh committed SDK pinning, restore-lock behavior, CI matrix với yêu cầu project; gợi ý action deterministic, không sửa file. | Config seams (`LoadConfig`), reporting sinks, test boundary xUnit | OPP-1 | chỉ đọc repo local | Fixture chain/blocker/mixed → ordered steps, byte-for-byte unchanged files |
| OPP-4 | Enterprise/banking maintainer có nghĩa vụ SSDF evidence | Legacy config/appSettings machine.config paths | Secret plaintext và cấu hình máy-specific khó phát hiện có hệ thống; cần objective evidence redacted. | `problem-market-evidence.md` hàng NIST SSDF | Deterministic key/name+value detection; finding redact value, hiển thị file/key; không bao giờ persist secret. | Reporting sinks; audit logger seam (`FileAuditLogger`) | OPP-1 | giá trị secret phải bị redact trong mọi output/log/artifact | Hostile-path/malformed/oversized/network-failure tests; raw fixture secret absent from stdout/stderr/log |

## Rejected

| Candidate | Lý do loại |
|---|---|
| Auto-remediation / auto-fix code | Plan cấm ở release đầu; không có seam user-confirmation. |
| Database deployment engine | Ngoài bề mặt product; SqlPackage đã tồn tại, DataGuard là assessment. |
| Generic dashboard/telemetry SaaS | Không có seam; vi phạm local-first boundary. |
| Compliance certification claim | NIST SSDF chỉ hỗ trợ evidence; certification ngoài phạm vi phần mềm. |

## Priority order

OPP-1 → OPP-2 → OPP-3 → OPP-4, theo rubric plan: inventory/compatibility trước, dependency/build diagnosis tiếp, rồi deterministic diagnostics, cuối cùng optional opt-in intelligence.
