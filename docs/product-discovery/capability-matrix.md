# Capability matrix

Chỉ giữ capability có seam thật (`code-capabilities.md`) và evidence (`opportunity-backlog.md`). Mỗi capability khóa: input contract, output schema, target range, offline/network behavior, cancellation/timeout, stable error code.

| Group | Capability | Input contract | Output schema/message | Target range | Offline/Network | Cancellation/Timeout | Error codes | Rubric scores (impact, legacy, leverage, sensitivity, FP cost, maintenance, testability) | Priority |
|---|---|---|---|---|---|---|---|---|---|
| Environment inventory | Project/solution inventory: TFM(s), package format, lock file, SDK pin, build configuration/platform | `AssessmentRequest { workspaceRoot, projectFilters? }` qua CLI option/API `ValidationPipeline` extension | `AssessmentReport` JSON: `schemaVersion`, `toolVersion`, `target`, `generatedAt`, `findings[]`, `errors[]`, `summary` | .NET Framework 4.6.2–4.8.1; SDK-style; netstandard2.0 libs | 100% local | cooperative token; per-project timeout từ config hiện có | `DG1001 path-outside-workspace`, `DG1002 unreadable-file`, `DG1003 invalid-metadata` | 5,5,5,2,2,3,5 | P0 |
| Legacy compatibility | Compare inventory facts vs curated support table committed as resource | Inventory facts + embedded table (source URL + retrieval date + range + rule id) | Finding với exact tuple, confidence; absent metadata → informational incomplete-data | như trên, bảng versioned trong repo | local table only | same | `DG1101 no-rule-match`, `DG1102 conflicting-rules` | 4,5,4,2,2,3,4 | P0 |
| Dependency health | Direct/transitive package inventory từ existing tooling/parser; missing lock/restore reproducibility; package-target incompatibility local check | Project files + lock files | Findings với evidence file/key; remote advisory chỉ khi opt-in flag/config có sẵn | PackageReference + packages.config (read-only) | local default; network opt-in, timeout-bound, partial result + provider/timestamp/error | same | `DG1201 lock-missing`, `DG1202 target-incompatible`, `DG1299 provider-unavailable` | 4,4,4,3,3,3,4 | P1 |
| Build/CI diagnosis | SDK pinning vs project requirements; restore-lock behavior; CI matrix drift | Repo files: `global.json`, csproj props, workflow files | Drift findings với exact file/property/job evidence + deterministic suggested action | CI YAML conventions hiện có của repo target | local | same | `DG1301 sdk-pin-drift`, `DG1302 matrix-gap` | 4,4,4,2,3,3,4 | P1 |
| Code correctness diagnostics | Chỉ qua Roslyn analyzer seam đã có (`DataGuard.Analyzers`); legacy-safe high-confidence rules; không regex-scan C# | Compilation + semantic model | Diagnostic ID, severity default, message, span, `helpLinkUri`, version applicability | Roslyn versions tương thích netstandard2.0 analyzer | local | compiler-driven | analyzer ID riêng (`DG9xxx`) | 3,4,5,2,4,4,4 | P2 |
| Configuration and secrets | Deterministic key/name+value detection từ config sources có sẵn; redact value; machine-specific paths | Config files được product đọc sẵn | Finding redacted với file/key evidence | app.config/web.config/.dataguard.yml | local | same | `DG1401 secret-like-value`, `DG1402 machine-path` | 4,4,3,5,3,3,4 | P1 |
| Test and performance guidance | Enumerate test targets/frameworks/coverage command; xác định thiếu deterministic test execution hoặc runner settings không tương thích | Project/test files | Evidence-based report; KHÔNG estimate performance thiếu measurement | xUnit conventions hiện có | local | same | `DG1501 no-deterministic-tests`, `DG1502 runner-mismatch` | 3,4,4,2,2,3,4 | P2 |
| Upgrade planning | Ordered steps từ inventory + compatibility table; topological order từ project references; SCC → manual blocker | Inventory + findings | `UpgradePlan` với `UpgradeStep[]`: source/target/blocking IDs/prereqs/validation command/rollback/confidence | curated table only | local | same | `DG1601 circular-blocked`, `DG1602 no-safe-target` | 4,4,4,2,3,3,4 | P2 |

## Loại khỏi scope release này

- Remote vulnerability/license lookup mặc định: chỉ opt-in, không ship enabled.
- SARIF export mới ngoài writer đã có: dùng `SarifTypes.cs` hiện hữu; writer schema-validated mới phải thêm trước.
- Auto-fix/codefix cho legacy rules: `DataGuard.CodeFixes` tồn tại nhưng auto-remediation bị plan cấm release đầu.

## Refusal/error behavior bắt buộc mọi capability

Unknown metadata → `Unknown`, không suy đoán support. I/O failure từng project → error entry, các sibling vẫn assess. Network opt-in fail → partial result kèm error, không silent omit. Secret value không bao giờ xuất hiện trong output/log/artifact.
