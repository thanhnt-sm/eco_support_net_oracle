# Enterprise Handoff — DataGuard (2026-08-21)

> **Mục đích**: Tài liệu bàn giao canonical cho đội triển khai (dev), đội kiểm thử (test/QC) và đội vận hành, chuẩn bị đưa DataGuard đến chuẩn bán hàng cho môi trường **doanh nghiệp lớn và ngân hàng** (tải trọng tuân thủ cao, offline-first, supply-chain nghiêm ngặt).
>
> **Cơ sở**: red-team trực tiếp trên code tại commit `ed5dbe1` (main, đã push) + toàn bộ tài liệu plans/docs liên quan. Mọi claim trong file này đều có bằng chứng `lệnh + output` hoặc `file:line` ghi kèm; claim không có bằng chứng được đánh dấu `[chưa xác minh]`.

---

## 0. Trạng thái thực tế đã xác minh (đo lại hôm nay)

| Hạng mục | Giá trị đã verify | Bằng chứng |
|---|---|---|
| Build | 0 errors, **8 warnings SA1000** (toàn bộ trong `tests/DataGuard.Core.Tests/RulesEngineTests.cs` — test code, không phải shipping code) | `dotnet build DataGuard.sln --no-incremental` → `0 Error(s)`, 8 warning SA1000 |
| Tests | **80/80 pass** (Core 50, GoldenCorpus 25, Analyzers 5) | `dotnet test DataGuard.sln` → Passed: 50+25+5, Failed: 0 |
| Git | `main` = `ed5dbe1`, sync với `origin/main`, working tree sạch | `git log --oneline -5`, `git status -sb` |
| NuGet packages | 9 + CodeFixes (Contracts, Core, 4 adapters, Analyzers, CodeFixes, Cli) | `ls src/` |
| Editor extensions | VS Code (TS) + Visual Studio 2022 (VSIX) — **chưa publish** | `plans/260820-marketplace-extensions/plan.md` |
| CodeQL open alerts | 0 (theo session register phiên trước) | `[chưa tái xác minh — cần CI run]` |

**Sửa sai số so với tài liệu cũ**: bản handoff trước ghi "Tests: 69 (Core 39)" và "Warnings 0". Thực tế hôm nay: **80 tests, 8 warnings SA1000 trong test project**. Không nghiêm trọng nhưng tài liệu bàn giao phải phản ánh đúng.

### Các blocker của red-team 2026-08-21 cũ đã được fix (verify trực tiếp)

| Blocker cũ | Trạng thái | Bằng chứng |
|---|---|---|
| B3: RepositoryUrl fake | ✅ Đã sửa | README trỏ `thanhnt-sm/eco_support_net_oracle` |
| B4: License mâu thuẫn 3 chiều | ✅ Đã chốt MIT đơn nhất | `LICENSE` = MIT toàn văn; README badge MIT; csproj không còn NU5034 conflict |
| B5: README 100% sản phẩm cũ | ✅ Đã rewrite | `README.md:1-45` = landing DataGuard (narrative EF #245 + dbt contracts) |
| B7: `config show` in ConnectionString | ✅ Đã redact | `Program.cs:446` → `"***redacted***"` |
| P1.13: ZeroTrust fail-open | ✅ Đã fail-closed | `ZeroTrustCredentialProvider.cs:157-167` — plaintext config-file bị chặn mặc định, phải bật explicit |
| P1.17: PublicApiSurface dead code/stub | ✅ Đã implement thật | `PublicApiSurface.cs:87-118` — WithPlugins/WithTelemetry/WithBaselineFile đều có thân hàm thật |

---

## 1. Findings red-team MỚI (phát hiện hôm nay — chưa có trong tài liệu nào)

Đây là kết quả trả lời 6 câu hỏi mở của bản handoff cũ (mục 5). **Tất cả chưa từng được ghi nhận ở bất kỳ đâu.**

### F1 — CRITICAL: Rule DG002 gần như no-op (trả lời câu hỏi 1)

**File**: `src/DataGuard.Core/Rules/ContractRules.cs:100-196`

Luồng hiện tại: `InferClrType(param.DataType)` suy ra CLR type **từ chính DB type**, rồi `IsTypeCompatible(clrType, param.DataType)` check ngược lại **chính DB type đó** với chính nó.

```csharp
// ContractRules.cs:154-155
var clrType = InferClrType(param.DataType);        // suy từ chính DB type
var isCompatible = IsTypeCompatible(clrType, param.DataType, isOracle);  // check lại chính nó
```

Hệ quả:
1. **Self-referential check**: DB type `varchar` → CLR `string` → map["string"] chứa "varchar" → luôn pass. DB type lạ (không trong map) → `_ => "string"` fallback → map["string"] chứa "varchar/char/…" → `Contains` substring khớp hay không tùy chuỗi. Rule **không bao giờ so sánh CLR type thật của call site** với DB type — nó so DB type với chính nó.
2. **Substring matching**: `IsTypeCompatible` dùng `dbType.Contains(t)` (line 194) → "point" chứa "int", "chart" chứa "char", "NUMERIC" chứa "NUMBER" v.v. — false positive tiềm ẩn hai chiều.
3. **Không so với C# thật**: rule không đọc được `SqlParameter`/Dapper parameter CLR type từ source — chỉ thấy `ParameterDescriptor.DataType` (string DB type). Với ngưỡng doanh nghiệp, đây là **giá trị cốt lõi của rule bị triệt tiêu**: chỉ phát hiện được lỗi khi DataType descriptor trống/lạ, không phát hiện mismatch thật giữa code và DB.

**Việc cần làm**: rule phải nhận **CLR type từ call site** (Roslyn symbol hoặc attribute `ExpectedSpParameter`) làm input, không tự suy ra từ DB type. Nếu không có nguồn CLR type thì rule nên skip + warning "insufficient ground truth", không fabricate.

### F2 — HIGH: Rule DG003 flag mọi OUT param bất kể call site

**File**: `ContractRules.cs:211-235`

Mọi param có Direction OUT/INOUT/RETURN đều sinh violation `"verify call site uses out/ref"` — không hề đọc call site. Trên stored procedure thật (banking codebase có hàng nghìn SP với OUT param chuẩn), rule này **phun noise lên toàn bộ codebase hợp lệ**, gây uninstall ngay lần đầu chạy. Phải lấy được direction tại call site (Roslyn) rồi mới so.

### F3 — HIGH: SchemaHash hash VIOLATIONS, không hash SCHEMA (trả lời câu hỏi 5)

**Files**: `BaselineManager.cs:204-214`, `Program.cs:376-390`

- `ComputeSchemaHash` chỉ hash `RuleId:Message` của các violations đã sinh ra. Nếu DB schema thay đổi mà thay đổi đó **không sinh rule violation nào** (ví dụ thêm cột mới vào table không được rule nào check), hash không đổi → drift bị bỏ qua im lặng.
- `snapshot diff` chỉ so hash + cảnh báo version major.minor (`Program.cs:363-373` — phần này đúng thiết kế); nhưng **`--fail-on-drift` mặc định false** (`Program.cs:37` + `Program.cs:390`) → CI gate tùy opt-in. Ngân hàng cần mặc định fail-on-drift khi chạy trong CI (detect CI env hoặc đổi default theo policy profile).
- Nguồn hash đúng: phải hash **schema descriptor** (columns, types, nullability, length của mọi table/SP trong snapshot), không hash output của rules.

### F4 — MEDIUM: Telemetry HTTP egress tồn tại đường explicit export (default đã đúng)

**File**: `src/DataGuard.Core/Telemetry/TelemetryCollector.cs:156-181, 197-201`

Khi cấu hình `ExportEndpoint`, `FlushEvents` POST NDJSON tới endpoint HTTP bất kỳ (line 173-175), lỗi bị nuốt im lặng (catch rỗng line 177-180). Bản thân đây là **tính năng** (OTLP-compatible), và default đã đúng posture enterprise: `TelemetryConfig(Enabled: false, ExportEndpoint: null)` (line 197-201) — đã verify trực tiếp. Nhưng với môi trường ngân hàng vẫn cần:

1. **Test chốt hành vi**: test "zero HttpClient instantiation when disabled" để default không bị regression về sau.
2. Banking profile trong docs: `EnableTelemetry: false` + `ExportEndpoint` trống → zero network egress.
3. Export chạy `.GetAwaiter().GetResult()` trên timer callback (line 175) — sync-over-async, deadlock tiềm ẩn; nên dùng async fire-and-forget hoặc channel.
4. Câu hỏi 2 (security boundary) của bản cũ: telemetry là đường egress **duy nhất còn lại** trong Core; argv không mang connection string khi dùng config file, `config show` đã redact (`Program.cs:446`).

### F5 — MEDIUM: `snapshot diff` không phát hiện stale snapshot nếu version giống nhau

`Program.cs:365-373` chỉ cảnh báo khi **major.minor khác**. Nếu DB cùng version nhưng DDL đổi (rất phổ biến — deploy schema migration không nâng version DB), không có tín hiệu nào. Khuyến nghị: thêm `snapshot.ageDays` + cảnh báo khi snapshot cũ > N ngày (policy-configurable), và khuyến nghị chạy `snapshot refresh` theo lịch trong CI.

### F6 — MEDIUM: ZeroTrustCredentialProvider vẫn còn nguồn config-file (đã gate nhưng cần audit UX)

`ZeroTrustCredentialProvider.cs:157-174`: plaintext config-file credential bị chặn mặc định (tốt), nhưng thông điệp lỗi nhắc "set AllowConfigFileCredentials" — cần đảm bảo banking profile **không bao giờ** set flag này và docs ghi rõ flag này là dev-only.

### F7 — LOW: exit-code table chưa được tài liệu hóa như contract

`Program.cs` dùng nhiều `Environment.ExitCode` (1 = validation fail, 2 = config/error). Dev-đội cần bảng exit-code chính thức trong docs (0/1/2) để CI consumer và extension hai host dùng đúng语义; test phải assert exit codes.

### Tóm tắt câu trả lời 6 câu hỏi red-team cũ

| # | Câu hỏi | Verdict |
|---|---|---|
| 1 | Rule no-op tồn tại? | **CÓ — DG002 self-referential** (F1); DG003 noise (F2) |
| 2 | Vector leak từ CLI còn? | Chỉ còn telemetry egress explicit — default off đã verify (F4); config-file gate dev-only (F6); `config show` đã redact, argv không mang connection string |
| 3 | Khoảng trống SLSA L3? | snupkg publish + TruffleHog history + package lock (P2.1/2.2, P1.16 red-team cũ) — chưa làm |
| 4 | PublicApi SemVer/dead code? | Stub đã hết; còn `ValidateGraph` trả về counters rỗng (`PublicApiSurface.cs:342-354`) — misleading, nên xóa hoặc trả về đúng semantics |
| 5 | Snapshot drift/stale? | **CÓ lỗ hổng** — hash violations không hash schema (F3), không phát hiện stale cùng version (F5) |
| 6 | Claim ~ms chưa benchmark? | Không có benchmark nào trong repo `[đã grep — không tìm thấy BenchmarkDotNet]`; claim "~ms" phải bỏ khỏi tài liệu bán hàng cho tới khi có số đo |
---

## 2. Gap tới chuẩn enterprise/banking — phân loại theo đội

### 2.1 Đội Dev (blocker trước khi bán)

| Ưu tiên | Việc | Lý do / gắn finding |
|---|---|---|
| **P0** | Fix DG002: nhận CLR type từ call site; bỏ substring match (exact token match sau normalize) | F1 |
| **P0** | Fix DG003: chỉ flag khi call-site direction xác thực mismatch | F2 |
| **P0** | SchemaHash → hash schema descriptor (columns/types/nullability/length), backward-compat migration cho snapshot cũ | F3 |
| **P0** | Xác nhận `TelemetryConfig.Enabled` default false + banking profile docs "zero egress" | F4 |
| **P1** | `snapshot diff` fail-on-drift mặc định trong CI mode; `snapshot.ageDays` warning | F3, F5 |
| **P1** | Test coverage → 60%+: `BaselineManager`, `ZeroTrustCredentialProvider`, `Sources/` (EfModelSource, SqlServerParsers, OracleReaders), `AutoDetectionEngine`, `ConcurrentValidationEngine`, `TelemetryCollector` | handoff cũ |
| **P1** | Bỏ/đ_fix `ValidateGraph` misleading counters | F7 |
| **P1** | `version` in `InformationalVersion`; `snapshot show` exit 0 informational | handoff cũ |
| **P2** | Benchmark BenchmarkDotNet cho `IncrementalGenerator` + `ConcurrentValidationEngine` trước khi ghi "~ms" vào tài liệu bán hàng | F6 |

### 2.2 Đội Test/QC

| Ưu tiên | Việc |
|---|---|
| **P0** | Viết test asserting DG002 catch mismatch thật (CLR int ↔ DB varchar) và **không** flag compatible pair — test này fail hôm nay, chứng minh bug |
| **P0** | Test schema-hash đổi khi DDL đổi (add column) — fail hôm nay với hash violations |
| **P0** | Test exit codes: 0 pass / 1 drift fail / 1 validation fail / 2 config error |
| **P1** | Test redaction: `config show` không chứa connection string thật (đã pass informally — cần test chính thức) |
| **P1** | GoldenCorpus: assert exact-match diagnostics (không chỉ ≥1 diagnostic), assert `unexpectedErrors == 0` |
| **P1** | Testcontainers integration tests (Oracle/SQL Server thật) cho AllArgumentsReader, sp_describe_first_result_set, RefCursorDescriber — chạy CI service containers |
| **P1** | Analyzer test (CSharpAnalyzerTest) cho tất cả descriptor arity/messageFormat |
| **P2** | Performance regression test: validate 1000 contracts dưới ngưỡng X giây trên CI runner |

### 2.3 Đội vận hành / go-to-market (bán ngân hàng)

| Ưu tiên | Việc |
|---|---|
| **P0** | **Không claim compliance certification** (PCI DSS/SOC 2/GDPR) khi chưa có audit độc lập — đã đúng trong marketplace plan, giữ nguyên kỷ luật này |
| **P0** | Banking/enterprise profile docs: offline-first (snapshot default), zero telemetry, redaction, least-privilege DB role (read-only + EXECUTE), exit-code contract |
| **P1** | Publish 2 marketplace extensions (cần owner: publisher verify + `VSCE_PAT`/`VS_MARKETPLACE_PAT` + VS 2022 Experimental Instance smoke) — runbook `docs/marketplace-publishing.md` |
| **P1** | NuGet Trusted Publishing: secret `NUGET_USER` (hạn migrate 01/11/2026) + tag `v0.1.0` end-to-end |
| **P2** | Case study/eval pilot với 5-10 team mục tiêu trước khi build P2 portal/dashboard |

---

## 3. Definition of Done — Enterprise/Banking release (v1.0)

Tất cả điều kiện dưới đây phải **đủ bằng chứng kiểm chứng** (lệnh + output gắn link CI run):

### 3.1 Correctness
- [ ] DG002 phát hiện mismatch CLR↔DB thật, không false positive trên compatible pairs (test đỏ→xanh)
- [ ] DG003 không flag OUT param hợp lệ
- [ ] SchemaHash hash schema descriptor; DDL change (add/drop column, đổi type) → hash đổi → `snapshot diff` báo drift
- [ ] `snapshot diff` exit ≠ 0 khi drift trong CI mode (hoặc policy default)
- [ ] MySQL/PostgreSQL `validate` chạy thật (không no-op) trên DB thật qua Testcontainers

### 3.2 Security posture (banking)
- [ ] Telemetry default off; test "zero HttpClient khi disabled" pass
- [ ] `config show` redacted (test chính thức)
- [ ] ZeroTrust: config-file plaintext bị chặn trong production profile (test)
- [ ] Không connection string trong argv/log/audit/SARIF (test redaction + review)
- [ ] SECURITY.md đúng product; incident response contact

### 3.3 Supply chain
- [ ] NuGet publish qua Trusted Publishing; snupkg publish đầy đủ
- [ ] SBOM + provenance + checksum cho 9 packages + 2 VSIX (đã có — cần verify release run thật)
- [ ] TruffleHog scan history; package lock commit
- [ ] CodeQL + vuln gate trên **tag release** (không chỉ branch)

### 3.4 Quality gates
- [ ] Coverage ≥ 60% toàn solution (coverlet report trong CI)
- [ ] 80+ tests pass; golden corpus exact-match
- [ ] 0 warnings shipping code (8 SA1000 hiện tại chỉ trong test project — fix nốt)
- [ ] Public API docs (XML doc) cho Core surface

### 3.5 Delivery
- [ ] NuGet packages 9+1 public với metadata chuẩn (RepositoryUrl, license MIT, tags, readme)
- [ ] 2 marketplace extensions public sau owner credentials
- [ ] Docs: user guide, banking profile, exit-code table, migration guide v0.x→v1.0
- [ ] CHANGELOG + versioning MinVer từ git tag

---

## 4. Câu hỏi mở cho chủ repo (blocker decision)

1. **Scope DG002 rewrite**: lấy CLR type bằng Roslyn analyzer (IDE path) hay bằng `ExpectedSpParameter` attribute (Manual mode)? Khuyến nghị cả hai, attribute trước (đơn giản, test được offline).
2. **Breaking change snapshot format**: hash schema mới làm snapshot cũ incompatible — chấp nhận migration tool `dataguard migrate` bắt buộc?
3. **Fail-on-drift default**: đổi default theo policy profile (banking = fail) hay theo detect CI env? Khuyến nghị policy profile, explicit hơn.
4. **Telemetry default**: xác nhận `TelemetryConfig.Enabled` default false (cần owner xác nhận thiết kế).
5. **Priorities story**: grant narrative Anthropic còn là mục tiêu không, hay product commercial enterprise là mục tiêu chính? Ảnh hưởng thứ tự backlog.

---

## 5. Link tài liệu tham chiếu

- SSOT: `plans/ACTIVE_SESSION_REGISTER.md`
- Red-team tổng 2026-08-21: `plans/2026-08-21-redteam-review.md`
- Red-team marketplace: `plans/260820-marketplace-extensions/reports/marketplace-redteam.md`
- ADR-001/002: `plans/adr/`
- Warning debt: `plans/2026-08-21-warnings-plan.md`
- Runbook publish: `docs/marketplace-publishing.md`
- Gap cũ (nhiều mục đã fix — đối chiếu mục 0): `docs/RISKS_GAPS.md`, `docs/FIX_PLAN.md`

---

## 6. Metadata

| Metadata | Giá trị |
|---|---|
| Worker model | DeepSeek V4 Pro (worker) — red-team trực tiếp, không qua subagent (subagent fail 402) |
| Commit cơ sở | `ed5dbe1` (main, đã push) |
| Ngày | 2026-08-21 |
| Verify lệnh | `dotnet build DataGuard.sln --no-incremental` (0 err, 8 SA1000 test-only); `dotnet test DataGuard.sln` (80/80) |
