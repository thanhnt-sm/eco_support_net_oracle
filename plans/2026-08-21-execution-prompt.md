# Execution Prompt — DataGuard Enterprise v1.0 Hardening

> **Cách dùng**: đưa toàn bộ file này làm prompt cho session worker (DeepSeek) hoặc leader đội triển khai. Prompt tự chứa (self-contained): mọi context, bằng chứng, thiết kế quyết định, acceptance criteria đều ở trong này — người nhận không cần đọc lịch sử hội thoại. Nguồn sự thật: `plans/2026-08-21-review-handoff.md` (commit `c1c3ac8`).

---

## PROMPT (copy từ đây)

Bạn là kỹ sư .NET senior phụ trách hardening DataGuard — contract validation engine cho môi trường doanh nghiệp/ngân hàng — tại repo `/Volumes/Data/101.AI/GitHub/eco_support_net_oracle` (branch `main`, commit gốc `c1c3ac8`).

### Bối cảnh sản phẩm

DataGuard (.NET 9, solution `DataGuard.sln`) phát hiện drift giữa entity .NET ↔ stored procedure / raw SQL: parameter mismatch, result-set shape, nullability, length semantics, dialect. Kiến trúc: `DataGuard.Contracts` (netstandard2.0, attributes) → `DataGuard.Core` (net9.0, rules engine) ← 4 DB adapters (SqlServer/Oracle/MySql/PostgreSql) → `DataGuard.Cli` (dotnet tool) + `DataGuard.Analyzers`/`DataGuard.CodeFixes` (Roslyn) + 2 editor extensions (VS Code TS, Visual Studio VSIX). Ground truth 3 mode: Full (live DB) / Snapshot (default, offline) / Manual (attributes).

Red-team 2026-08-21 đã xác định các lỗ hổng dưới đây. **Nhiệm vụ của bạn: fix theo đúng thứ tự phase, mỗi việc có test chứng minh, không được bỏ qua phase nào.**

### Quy tắc bất biến

1. **Evidence-first**: mọi thay đổi phải kèm lệnh verify + output mong đợi. Không đoán.
2. **Test đỏ→xanh với bug fix**: viết test chứng minh bug TRƯỚC khi fix (test fail trên code hiện tại), rồi fix cho test pass.
3. **Không scope creep**: chỉ làm các việc liệt kê. Gặp việc phát sinh → ghi vào `plans/2026-08-21-review-handoff.md` mục "Rủi ro mở", không tự làm.
4. **Không thay đổi public API breaking** khi chưa được chỉ định ở dưới. Trước khi sửa symbol public → chạy `lsp references` tìm mọi callsite.
5. **Mỗi phase kết thúc**: `dotnet build DataGuard.sln` 0 errors (không thêm warning mới — baseline hiện 8 SA1000 trong `tests/DataGuard.Core.Tests/RulesEngineTests.cs`) + `dotnet test DataGuard.sln` toàn pass + commit conventional (`fix(core): ...`, `test: ...`).
6. Trả lời tiếng Việt trong báo cáo; code/identifier giữ nguyên.

---

### PHASE 0 — Baseline reproduction (bắt buộc, ~10 phút)

Chạy và ghi lại output làm mốc so sánh:

```bash
dotnet build DataGuard.sln --no-incremental --nologo   # kỳ vọng: 0 errors, 8 warnings SA1000
dotnet test DataGuard.sln --nologo                      # kỳ vọng: 80/80 pass (Core 50, GoldenCorpus 25, Analyzers 5)
```

Nếu sai khác kỳ vọng → DỪNG, báo cáo, không sửa gì.

---

### PHASE 1 — P0 correctness fixes (blocker bán hàng)

#### 1.1 Fix DG002 `ParameterTypeMatchRule` (CRITICAL — self-referential no-op)

**Bug** (`src/DataGuard.Core/Rules/ContractRules.cs:100-196`): `InferClrType(param.DataType)` suy CLR type từ chính DB type rồi `IsTypeCompatible(clrType, param.DataType)` check ngược với chính DB type đó — rule không bao giờ so với CLR type thật. Ngoài ra `IsTypeCompatible` dùng `dbType.Contains(t)` substring match (line 194): "point" chứa "int", "chart" chứa "char".

**Fix theo thiết kế sau** (quyết định: nguồn CLR type = attribute trước, Roslyn sau):

1. Rule chỉ chạy khi có nguồn CLR type thật. Nguồn 1: `ParameterDescriptor` mở rộng thêm `ClrType` (string, nullable) — được set từ `ExpectedSpParameter` attribute (Manual mode) và từ SP parser khi có type mapping. Nguồn 2 (phase sau, KHÔNG làm ở phase này): Roslyn analyzer truyền call-site type.
2. Nếu `ClrType` null/empty → **skip rule cho param đó** + không violation (không fabricate). Có thể thêm 1 informational diagnostic sau (backlog, không làm).
3. Thay `Contains` bằng exact token match sau normalize: tách `dbType` thành tokens (loại `(...)`, whitespace, dấu phẩy), so sánh `string.Equals(token, t, StringComparison.OrdinalIgnoreCase)` với từng phần tử map. "NUMBER(1)" cho bool: match exact chuỗi "NUMBER(1)" (giữ entry riêng như hiện có).
4. Giữ 2 map `SqlServerTypeMap`/`OracleTypeMap`; bỏ hàm `InferClrType` khỏi path check (có thể giữ nếu còn callsite khác — kiểm tra `lsp references` trước khi xóa).

**Test bắt buộc (viết TRƯỚC, phải fail trên code hiện tại)**:
- `DG002_CatchesRealMismatch`: param `ClrType="int"`, DB type `varchar` → 1 violation.
- `DG002_PassesCompatiblePair`: `ClrType="int"`, DB `int`/`NUMBER` → 0 violation.
- `DG002_SkipsWhenNoClrType`: `ClrType=null` → 0 violation.
- `DG002_NoSubstringFalsePositive`: `ClrType="int"`, DB type `"POINT"` → không khớp "int" theo substring (behavior đúng: violation vì POINT không compatible int).

#### 1.2 Fix DG003 `ParameterDirectionRule` (HIGH — noise)

**Bug** (`ContractRules.cs:211-235`): mọi param OUT/INOUT/RETURN đều sinh violation "verify call site uses out/ref" — không đọc call site nào cả.

**Fix**: tương tự 1.1 — thêm `CallSiteDirection` (nullable) vào `ParameterDescriptor`. Khi null → skip (0 violation). Khi có → chỉ flag khi `param.Direction` yêu cầu out/ref mà `CallSiteDirection` là input-only. Rule giữ RuleId DG003.

**Test**: OUT param + CallSiteDirection=Output → pass; OUT param + CallSiteDirection=Input → violation; CallSiteDirection null → skip.

#### 1.3 Fix SchemaHash — hash schema descriptor, không hash violations (HIGH)

**Bug** (`src/DataGuard.Core/Baseline/BaselineManager.cs:204-214` + `src/DataGuard.Cli/Program.cs:376-390`): `ComputeSchemaHash` hash `RuleId:Message` của violations → DDL đổi mà không sinh violation thì drift im lặng. `--fail-on-drift` default false.

**Fix**:
1. Thêm `ComputeSchemaHash(IReadOnlyList<SnapshotTable> schema)` overload (hoặc type tương đương snapshot đã lưu — xem `SnapshotTable` usage tại `Program.cs:249-258`): serialize canonical (sort theo table/column name, field cố định: name, type, nullable, length, precision, scale) → SHA256 full, hex. Giữ prefix `..16` nếu cần so sánh format cũ — KHÔNG, dùng full hash cho mới.
2. `snapshot refresh` lưu cả schema + schemaHash mới; `snapshot diff` (Program.cs:375-390) tính hash từ schema hiện tại (live hoặc snapshot được refresh) và so với `baseline.SchemaHash`.
3. **Backward-compat**: snapshot cũ không có schema → in warning "snapshot format v1 — run `dataguard snapshot refresh` to upgrade" và fallback so hash violations cũ (behavior hiện tại), exit code theo fallback result. KHÔNG crash trên snapshot cũ.
4. `--fail-on-drift` default GIỮ false (đổi default là breaking cho người dùng hiện tại); thay vào đó thêm cảnh báo in rõ khi chạy trong CI (detect `CI` hoặc `GITHUB_ACTIONS` env): "drift detected — pass --fail-on-drift to fail CI".

**Test bắt buộc**:
- `SchemaHash_ChangesWhenColumnAdded`: schema A + thêm cột → hash khác (fail trên code hiện tại vì hash violations không đổi).
- `SchemaHash_StableAcrossViolationOrdering`: cùng schema, violations reorder → hash giống.
- `SnapshotDiff_FailOnDriftExitCode`: drift + `--fail-on-drift` → exit 1; không flag → exit 0. Test qua CLI invocation (nếu test project có harness; không có thì unit test handler logic + integration test thủ công ghi output).

#### 1.4 Telemetry zero-egress test (MEDIUM — chốt posture)

Default đã đúng: `TelemetryConfig(Enabled: false, ...)` (`src/DataGuard.Core/Telemetry/TelemetryCollector.cs:197-201`). Việc cần làm:
1. Test `Telemetry_NoHttpClientWhenDisabled`: collector với `Enabled: false`, record events, trigger flush, assert không có request ra ngoài (cách xác thực: chỉ cần assert `FlushEvents` không tạo `HttpClient` — refactor nhẹ `ExportEvents` thành injectable `Func<...>` hoặc kiểm tra `_config.Enabled` gate trước mọi export path; chọn cách refactor nhỏ nhất).
2. KHÔNG sửa sync-over-async ở line 175 ở phase này — ghi backlog.

---

### PHASE 2 — P1 (sau khi Phase 1 xanh)

Theo thứ tự:

1. **`version` command** in `InformationalVersion` ( assemblies phụ đang 0.0.0.0 ) — `src/DataGuard.Cli/Program.cs` tìm handler `version`.
2. **`snapshot show` exit 0** khi chưa có snapshot (informational, không phải error).
3. **`ValidateGraph` fix** (`src/DataGuard.Core/PublicApi/PublicApiSurface.cs:342-354`): đang trả về counters rỗng misleading. Đổi return type hoặc semantics: trả về `RuleDependencyGraph` validation thật (errors/warnings đúng). Kiểm tra `lsp references` trước — nếu zero caller public, cân nhắc xóa hẳn (breaking nhưng pre-1.0).
4. **Coverage 60%**: thêm unit test cho `BaselineManager`, `ZeroTrustCredentialProvider` (mock sources), `Sources/EfModelSource`, `SqlServerParsers`, `OracleReaders`, `AutoDetectionEngine`, `ConcurrentValidationEngine`, `TelemetryCollector`. Dùng coverlet (`dotnet test --collect:"XPlat Code Coverage"`), ghi số trước/sau vào báo cáo.
5. **GoldenCorpus exact-match**: assert số diagnostic + RuleId chính xác (không chỉ ≥1); assert `unexpectedErrors == 0`.
6. **Exit-code table docs**: thêm mục "Exit codes" vào `README.md`: 0 = pass, 1 = validation fail / drift fail, 2 = config/error. Test assert các exit code chính.

KHÔNG làm ở phase này (backlog, cần DB thật hoặc owner decision): Testcontainers integration, benchmark, marketplace publish, snupkg/TruffleHog/package-lock, docs 1-ngôn-ngữ.

---

### PHASE 3 — Verification tổng + bàn giao

1. Chạy lại toàn bộ:
```bash
dotnet build DataGuard.sln --no-incremental --nologo   # 0 errors, ≤8 warnings SA1000 (không tăng)
dotnet test DataGuard.sln --nologo                      # 100% pass, số test ≥ 80 + các test mới
dotnet test DataGuard.sln --collect:"XPlat Code Coverage" --nologo  # ghi coverage %
```
2. Smoke CLI end-to-end trên sample config (tạo scratch ngoài repo): `dataguard validate --offline`, `snapshot diff` với snapshot cũ format v1 (backward-compat path), `config show` (không in connection string).
3. Cập nhật `plans/2026-08-21-review-handoff.md`: đánh dấu mục 2.1/2.2 đã xong + bằng chứng.
4. Ghi handoff `.omp/handoffs/CURRENT.md` theo template 7 mục (nếu quy trình OMP yêu cầu).
5. Commit từng việc riêng (không giant commit); push `main`.

### Acceptance criteria tổng (Definition of Done cho phiên này)

- [ ] DG002: 4 test case ở 1.1 pass; rule so CLR type thật; exact token match
- [ ] DG003: 3 test case ở 1.2 pass; không còn flag vô điều kiện
- [ ] SchemaHash: 3 test ở 1.3 pass; backward-compat snapshot v1 không crash
- [ ] Telemetry: test zero-egress pass
- [ ] P1 items 1-6 hoàn thành hoặc có lý do ghi rõ (owner-blocked)
- [ ] Build 0 errors, không warning mới; toàn test pass; coverage ≥ 45% sau phase này (60% là mục tiêu v1.0, có thể cần thêm phiên)
- [ ] Mỗi claim trong báo cáo kèm lệnh + output thật

### Giới hạn

- KHÔNG sửa `src/DataGuard.VSCode/` hay `src/DataGuard.VisualStudio/` (publish blocked bởi owner secrets — ngoài scope).
- KHÔNG đổi license/metadata package.
- KHÔNG force-push, KHÔNG đổi tag.
- Nếu thiếu thông tin để quyết (ví dụ format `SnapshotTable` không có field cần hash): chọn phương án bảo thủ nhất, ghi rõ quyết định + lý do trong báo cáo, tiếp tục phần khác.

---

## HẾT PROMPT (copy đến đây)

### Ghi chú cho người điều phối (không đưa vào prompt)

- 5 câu hỏi owner trong handoff (`plans/2026-08-21-review-handoff.md` mục 4) chưa có trả lời — prompt đã chọn default an toàn: attribute-based CLR source, fail-on-drift giữ default false + CI cảnh báo, ValidateGraph sửa semantics. Nếu owner quyết khác → cập nhật prompt trước khi chạy.
- Subagent scout đang fail 402 (provider balance) — phiên thực thi nên tự grep/read, không ủy thác scout cho tới khi provider ổn định.
