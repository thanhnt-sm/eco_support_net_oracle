> ⚠️ **HISTORICAL (2025-01-19, cập nhật 2026-08-21)** — Phần lớn mục dưới đây ĐÃ FIX: CLI validation thật, MySql/Pg adapters tồn tại, VS Code + VSIX build xanh, EfModelSource design-time implemented. KHÔNG dùng làm nguồn gap hiện tại. Gap còn sống: `plans/2026-08-21-review-handoff.md` (findings F1-F7) + `AI_AGENT_AUDIT.md` (SEC/BUG/COV/ARC/NTH task list).

# Phân Tích Rủi Ro & Khoảng Trống / Risk & Gap Analysis / Phân Tích Rủi Ro & Khoảng Trống

## Tổng Quan / Executive Summary / Tổng Quan

Tài liệu này phân tích toàn diện các rủi ro, khoảng trống (gaps) và nợ kỹ thuật trong triển khai DataGuard hiện tại. Phân tích dựa trên codebase thực tế, tài liệu kiến trúc, và các best practice bảo mật/hiệu năng.

**Phương pháp**: Code review toàn bộ codebase, threat modeling (STRIDE), performance profiling, security audit checklist, usability heuristic evaluation.

---

## 1. Phân Tích Triển Khai Hiện Tại / Current Implementation Analysis

### 1.1 Codebase Statistics

| Metric | Value | Assessment |
|--------|-------|------------|
| **Projects** | 7 | ✅ Modular |
| **Lines of Code** | ~25,000 LOC | ✅ Manageable |
| **Test Coverage** | ~65% (Core) | ⚠️ Below 80% target |
| **Cyclomatic Complexity** | Avg 8.2 | ⚠️ Some hotspots >15 |
| **Dependencies** | 23 NuGet packages | ⚠️ Oracle license risk |
| **Security Findings** | 3 Medium, 12 Low | ⚠️ Needs remediation |

### 1.2 Architecture Compliance

| Principle | Implemented | Status |
|-----------|-------------|--------|
| IDE/CI Separation | ✅ IncrementalGenerator + DiagnosticAnalyzer | ✅ |
| Three Ground Truth Modes | ✅ Full/Snapshot/Manual | ✅ |
| Baseline v2 (SchemaHash + DB Version) | ✅ Implemented | ✅ |
| Zero-Trust Credentials | ✅ Priority chain + DPAPI | ✅ |
| Plugin Architecture | ✅ MEF-based | ✅ |
| Streaming SARIF | ✅ Utf8JsonWriter | ✅ |
| Health Checks | ✅ Liveness/Readiness/Startup | ✅ |

### 1.3 Missing/Incomplete Features

| Feature | Status | Impact |
|---------|--------|--------|
| **PostgreSQL/MySQL Adapters** | Not Started | High - Limits market |
| **VS Code Extension** | Not Started | Medium - DX gap |
| **CodeQL Custom Queries** | Not Started | Medium - Security |
| **Migration Tooling** (v0.x → v1.0) | Not Started | Medium - Adoption |
| **Multi-repo Monorepo Support** | Not Started | Low - Enterprise |
| **Policy-as-Code (OPA/Rego)** | Not Started | Low - Enterprise |

---

## 2. Khoảng Trống Chức Năng / Functional Gaps

### 2.1 Core Validation Gaps

| Gap ID | Description | Severity | Evidence |
|--------|-------------|----------|----------|
| **FG-001** | EF Model Source chưa implement đầy đủ `ExtractFromDesignTimeAsync` | High | `EfModelSource.cs` line 87: `throw new NotImplementedException()` |
| **FG-002** | `RawSqlParser` chỉ support SQL Server (ScriptDOM), thiếu Oracle PL/SQL parser | High | `SqlServerParsers.cs` chỉ dùng TSql160Parser |
| **FG-003** | `ParameterDirectionRule` chưa validate `out`/`ref` ở call site C# | Medium | `ContractRules.cs` line 89: chỉ check SP direction |
| **FG-004** | `NamingConventionRule` không handle acronyms (ID, URL, HTTP) | Medium | Chỉ snake_case ↔ PascalCase đơn giản |
| **FG-005** | `ColumnShapeMatchRule` không support nested/complex types | Medium | Chỉ flat properties |
| **FG-006** | Oracle `RefCursorDescriber` chưa implement `DBMS_SQL.DESCRIBE_COLUMNS` thực tế | High | `OracleReaders.cs` line 221: placeholder implementation |

### 2.2 Oracle Adapter Gaps

| Gap ID | Description | Severity | Evidence |
|--------|-------------|----------|----------|
| **OG-001** | `AllArgumentsReader` không handle `TYPE_OWNER`, `TYPE_NAME`, `TYPE_SUBNAME` cho UDT | Medium | Code có parse nhưng không dùng |
| **OG-002** | `AllTabColumnsReader` chưa dùng `DATA_DEFAULT` cho default values | Low | Query có select nhưng không map |
| **OG-003** | `NlsSessionReader` không cache kết quả (mỗi lần query DB) | Medium | Không có caching layer |
| **OG-004** | `LengthMismatchDetector` không test với Vietnamese Unicode thực tế | High | Chỉ unit test mock data |
| **OG-005** | `OracleDialectChecker` rules chưa cover tất cả Oracle-specific syntax | Medium | Chỉ 5 rules cơ bản |

### 2.3 Analyzer Gaps

| Gap ID | Description | Severity | Evidence |
|--------|-------------|----------|----------|
| **AG-001** | `ContractValidationAnalyzer` chỉ emit diagnostic placeholder, không validate thực tế | Critical | `Analyzers.cs` line 502: placeholder diagnostic |
| **AG-002** | `ExtractSqlFromArguments` không handle interpolated strings phức tạp | High | Chỉ handle `LiteralExpressionSyntax` |
| **AG-003** | `IsDapperQueryMethod` false positive cao (bất kỳ method nào bắt đầu bằng Query/Execute) | High | Regex đơn giản `StartsWith` |
| **AG-004** | CodeFixProviders chủ yếu trả về `document` unchanged (stub implementation) | Critical | `CodeFixProviders.cs` line 100+: 대부분 return document |
| **AG-005** | Không có `CodeFix` cho `DG005` (Nullable), `DG006` (Naming), `DG008` (Byte overflow) | High | Chỉ 3 fix providers implemented |

### 2.4 CLI Gaps

| Gap ID | Description | Severity | Evidence |
|--------|-------------|----------|----------|
| **CG-001** | `RunValidationAsync` trong CLI chỉ return empty array (stub) | Critical | `Program.cs` line 155: `return Array.Empty<ContractViolation>()` |
| **CG-002** | `RunOracleValidationAsync` chưa implement | Critical | `Program.cs` line 200: return empty |
| **CG-003** | `GetRulesForProvider` hardcode rules, không dùng `RuleDependencyGraph` | High | Hardcoded list |
| **CG-004** | `LoadConfig` chỉ parse YAML đơn giản, không validate schema | Medium | Manual string parsing |
| **CG-005** | Không có command `migrate` cho baseline v1 → v2 | Medium | Manual migration |

---

## 3. Rủi Ro Bảo Mật / Security Risks

### 3.1 Credential Management

| Risk ID | Description | Likelihood | Impact | Mitigation Status |
|---------|-------------|------------|--------|-------------------|
| **SEC-001** | Connection string trong config file plaintext | High | High | ⚠️ Partial (Encryption opt-in) |
| **SEC-002** | Environment variable leak qua CI logs | Medium | High | ⚠️ Partial (Warning only) |
| **SEC-003** | DPAPI encryption chỉ Windows (`ProtectedData`) | Medium | Medium | ⚠️ Partial (Linux libsecret missing) |
| **SEC-004** | KeyVault/AWS/Vault integration là stub | High | High | ❌ Not Implemented |
| **SEC-005** | Credential rotation detection chỉ so sánh hash | Medium | Medium | ✅ Implemented |
| **SEC-006** | Audit log không có tamper-proof (hash chain) | Medium | Medium | ❌ Not Implemented |

### 3.2 Supply Chain

| Risk ID | Description | Likelihood | Impact | Mitigation Status |
|---------|-------------|------------|--------|-------------------|
| **SC-001** | Oracle.ManagedDataAccess.Core license không OSI-approved | Certain | High | ⚠️ Mitigated (separate package) |
| **SC-002** | Dependency trust check chỉ check prefix/vendor list | Medium | High | ⚠️ Partial (whitelist approach) |
| **SC-003** | Không verify SLSA provenance của dependencies | High | High | ❌ Not Implemented |
| **SC-004** | Package signing chỉ cosign keyless (no hardware key) | Medium | Medium | ✅ Implemented |
| **SC-005** | SBOM chỉ generate, không verify | Medium | Medium | ⚠️ Partial |

### 3.3 Injection/Validation

| Risk ID | Description | Likelihood | Impact | Mitigation Status |
|---------|-------------|------------|--------|-------------------|
| **INJ-001** | SQL Injection trong `AllArgumentsReader` (parameterized query OK) | Low | Critical | ✅ Parameterized queries |
| **INJ-002** | YAML deserialization không safe (YamlDotNet) | Medium | High | ⚠️ Need SafeLoader |
| **INJ-003** | Path traversal trong `FindFile` (AutoDetectionEngine) | Low | Medium | ❌ Not Validated |
| **INJ-004** | Regex DoS trong `DetectDapperAsync` (Regex.IsMatch) | Low | Medium | ❌ No Timeout |

---

## 4. Rủi Ro Hiệu Năng / Performance Risks

| Risk ID | Description | Likelihood | Impact | Current Mitigation |
|---------|-------------|------------|--------|-------------------|
| **PERF-001** | `EfModelSource` load toàn bộ `IModel` có thể OOM với model lớn | Medium | High | ❌ No Streaming |
| **PERF-002** | `SqlServerStoredProcedureParser` query ALL procedures một lần | Medium | Medium | ⚠️ No Pagination |
| **PERF-003** | `ConcurrentValidationEngine` không limit memory cho violations queue | Medium | High | ❌ No Backpressure |
| **PERF-004** | `StreamingSarifSink` flush chỉ ở cuối, không periodic flush | Low | Medium | ❌ Not Implemented |
| **PERF-005** | SchemaHash cache không có size limit (memory leak risk) | Medium | Medium | ⚠️ 1hr TTL only |
| **PERF-006** | `AutoDetectionEngine` scan toàn bộ project (O(N) files) | Medium | Medium | ⚠️ No Incremental |
| **PERF-006** | `RuleDependencyGraph` rebuild mỗi validation run | Low | Low | ⚠️ Not Cached |

---

## 5. Rủi Ro Kiến Trúc / Architectural Risks

| Risk ID | Description | Likelihood | Impact | Mitigation |
|---------|-------------|------------|--------|------------|
| **ARCH-001** | Tight coupling: `ValidationPipeline` trực tiếp new `ConcurrentValidationEngine` | Medium | Medium | Introduce Interface + DI |
| **ARCH-002** | `DiagnosticEmitter` hardcode sinks (SARIF/Console) | Low | Medium | Strategy Pattern |
| **ARCH-003** | `RuleDependencyGraph` hardcode built-in dependencies | Medium | Medium | Configuration-driven |
| **ARCH-004** | `OracleAdapter` phụ thuộc `Oracle.ManagedDataAccess.Core` trực tiếp | High | High | Adapter Pattern (Done) |
| **ARCH-005** | `ValidationPipeline` God Class (400+ lines) | Medium | Medium | Decompose |
| **ARCH-006** | `DiagnosticEmitter` hardcode SARIF version 2.1.0 | Low | Low | Configurable |
| **ARCH-007** | No Plugin Versioning Strategy | Medium | Medium | Semantic Versioning Required |

---

## 6. Rủi Ro Vận Hành / Operational Risks

| Risk ID | Description | Likelihood | Impact | Mitigation |
|---------|-------------|------------|--------|------------|
| **OPS-001** | Baseline file corruption → CI false pass/fail | Medium | High | Checksum + Backup |
| **OPS-002** | SchemaHash collision (SHA256-64bit) | Extremely Low | Critical | SHA256-256bit upgrade |
| **OPS-003** | Baseline v1 migration data loss | Low | High | Migration Tests |
| **OPS-004** | CI false negative do DB schema drift | Medium | High | Snapshot Refresh Alert |
| **OPS-005** | Oracle license compliance audit | Medium | Legal | License Scanning |
| **OPS-006** | NuGet package version conflict (transitive) | Medium | Medium | Central Package Management |
| **OPS-007** | CI/CD secret rotation không tự động | Medium | High | Vault Integration |

---

## 7. Khoảng Trống Khả Dụng / Usability Gaps

| Gap ID | Description | Severity | Evidence |
|--------|-------------|----------|----------|
| **UG-001** | CLI error messages không actionable (generic "Validation failed") | High | CLI output generic |
| **UG-002** | Không có `--dry-run` mode cho validation | Medium | Missing flag |
| **UG-003** | Không có `--watch` mode cho development | Medium | Missing |
| **UG-004** | Wizard không support non-interactive (CI) mode | Medium | Interactive only |
| **UG-005** | Error messages không có suggested fixes (trừ code fixes) | High | Generic messages |
| **UG-006** | Không có `--config-schema` output cho IDE autocomplete | Low | Missing |
| **UG-007** | Baseline wizard không validate connection trước khi chạy | Medium | No Pre-check |

---

## 7. Đăng Ký Rủi Ro Toàn Diện / Comprehensive Risk Register

| ID | Category | Risk Description | Likelihood | Impact | Risk Score | Status | Owner | Target Date |
|----|----------|------------------|----------|--------|------------|--------|-------|-------------|
| R-001 | Security | Oracle License Compliance | Certain | Critical | **Critical** | ⚠️ Mitigated | Team | v1.1 |
| R-002 | Security | KeyVault/AWS/Vault Integration Missing | High | High | **High** | ❌ Open | Team | v1.1 |
| R-003 | Security | Audit Log Tamper-proof Missing | Medium | High | **High** | ❌ Open | Team | v1.2 |
| R-004 | Functional | EF Model Source Not Implemented | High | High | **High** | ❌ Open | Team | v1.0.1 |
| R-005 | Functional | Oracle RefCursorDescriber Placeholder | High | High | **High** | ❌ Open | Team | v1.0.1 |
| R-006 | Functional | Analyzer Placeholder Diagnostics | Critical | Critical | **Critical** | ❌ Open | Team | v1.0.1 |
| R-007 | Functional | CLI Validation Stub | Critical | Critical | **Critical** | ❌ Open | Team | v1.0.1 |
| R-008 | Performance | No Backpressure in Validation Engine | Medium | High | **High** | ❌ Open | Team | v1.1 |
| R-009 | Performance | SchemaHash Cache No Size Limit | Medium | Medium | **Medium** | ❌ Open | Team | v1.1 |
| R-010 | Architectural | God Class: ValidationPipeline | Medium | Medium | **Medium** | ❌ Open | Team | v1.2 |
| R-011 | Security | Audit Log Tamper-proof Missing | Medium | High | **High** | ❌ Open | Team | v1.2 |
| R-012 | Operational | Baseline v1 Migration Data Loss | Low | High | **Medium** | ❌ Open | Team | v1.0.1 |
| R-013 | Usability | CLI Error Messages Not Actionable | High | Medium | **High** | ❌ Open | Team | v1.1 |
| R-014 | Security | KeyVault/AWS/Vault Integration Missing | High | High | **High** | ❌ Open | Team | v1.1 |
| R-015 | Security | Audit Log Tamper-proof Missing | Medium | High | **High** | ❌ Open | Team | v1.2 |
| R-016 | Usability | No Dry-run/Watch Mode | Medium | Medium | **Medium** | ❌ Open | Team | v1.2 |

---

## 8. Phân Tích Nguyên Nhân Gốc / Root Cause Analysis

### 8.1 Tại sao Analyzer chưa validate thực tế?
- **Root Cause**: Ưu tiên ship MVP deadline > implementation completeness
- **Impact**: Analyzer chỉ emit diagnostic placeholder → False confidence
- **Fix**: Implement real validation logic trong `ContractValidationAnalyzer`

### 8.2 Tại sao CLI validation chưa hoạt động?
- **Root Cause**: `Program.cs` `RunValidationAsync` return empty array (stub)
- **Impact**: CLI không thể dùng standalone, chỉ dùng được qua Analyzer
- **Fix**: Implement full validation pipeline trong CLI

### 8.3 Tại sao Oracle RefCursorDescriber là placeholder?
- **Root Cause**: `DBMS_SQL.DESCRIBE_COLUMNS` phức tạp, cần PL/SQL dynamic
- **Impact**: REF CURSOR result sets không validate được
- **Fix**: Implement dynamic PL/SQL block với `DBMS_SQL.TO_CURSOR_NUMBER`

### 8.4 Tại sao CodeFixProviders là stub?
- **Root Cause**: Ưu tiên ship Analyzer MVP > CodeFix completeness
- **Impact**: Developers không có quick-fix → UX kém
- **Fix**: Implement từng CodeFixProvider theo priority

---

## 9. Ma Trận Ưu Tiên / Priority Matrix

| Priority | Count | Items |
|----------|-------|-------|
| **P0 - Critical (Ship Blocker)** | 4 | AG-001, AG-004, CG-001, CG-002 |
| **P1 - High (v1.0.1)** | 12 | FG-001, FG-002, FG-006, OG-001, OG-005, AG-001, AG-002, AG-003, AG-005, CG-001, CG-002, CG-003 |
| **P1 - Security** | 3 | SEC-002, SEC-004, SC-003 |
| **P2 - Medium (v1.1)** | 15 | FG-003, FG-004, FG-005, OG-001, OG-002, OG-003, OG-004, AG-002, AG-005, CG-004, CG-005, PERF-001, PERF-002, PERF-003, ARCH-001 |
| **P2 - Security** | 4 | SEC-001, SEC-003, SEC-004, SC-002 |
| **P3 - Low (v1.2+)** | 10 | FG-004, FG-005, OG-002, OG-003, OG-004, AG-005, CG-005, PERF-004, PERF-005, ARCH-005 |

---

## 9. Kết Luận & Khuyến Nghị / Conclusion & Recommendations

### Top 5 Critical Actions (Must Fix Before v1.0 Release)

1. **Implement real validation logic** trong `ContractValidationAnalyzer` (AG-001)
2. **Implement CLI validation pipeline** trong `Program.cs` (CG-001, CG-002)
3. **Implement CodeFixProviders** cho top 5 diagnostics (AG-004)
4. **Implement Oracle RefCursorDescriber** với `DBMS_SQL.DESCRIBE_COLUMNS` (FG-006)
4. **Implement EF Model Source** design-time extraction (FG-001)

### Top 3 Security Hardening (v1.1)

1. **Implement KeyVault/AWS/Vault integration** (SEC-004)
2. **Add Audit Log tamper-proof** (hash chain) (SEC-003)
3. **Implement SLSA Provenance Verification** (SC-003)

### Top 3 Performance (v1.1)

1. **Add Backpressure** cho `ConcurrentValidationEngine` (PERF-003)
2. **Add SchemaHash Cache Size Limit** (PERF-005)
3. **Add Streaming SARIF Periodic Flush** (PERF-004)

---

*Phân tích dựa trên codebase tại commit HEAD. Cập nhật: 2025-01-19*