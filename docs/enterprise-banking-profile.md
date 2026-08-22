# DataGuard — Enterprise / Banking Profile

> **Mục đích**: cấu hình và vận hành DataGuard trong môi trường doanh nghiệp lớn / ngân hàng với tải trọng tuân thủ cao, offline-first, và supply-chain nghiêm ngặt.
> Mọi cài đặt dưới đây phản ánh hành vi thực tế đã verify trong code (commit `054476e`, 2026-08-22).

## 1. Posture tóm tắt (mặc định an toàn)

| Trục | Default | Bằng chứng |
|---|---|---|
| Ground truth | `Snapshot` (offline, không cần DB/credentials trong CI) | `Configuration.cs:8` — `GroundTruthMode.Snapshot` |
| Telemetry | `Enabled=false`, `ExportEndpoint=null` — zero network egress | `TelemetryCollector.cs` — `TelemetryConfig(Enabled: false, ExportEndpoint: null)` |
| Credentials | env var / secret manager (Key Vault, AWS, Vault); plaintext config-file bị chặn | `ZeroTrustCredentialProvider.cs` — `AllowPlaintextConfigFallback=false` |
| Redaction | `config show` không in connection string | `Program.cs:491-497` — `"***redacted***"` |
| Telemetry egress | allowlist HTTPS + localhost/127.0.0.1; circuit breaker sau 3 lỗi liên tiếp | SEC-006 (`TelemetryCollector.cs`) |

## 2. Least-privilege DB role runbook

DataGuard chỉ **đọc** catalog/schema — không cần quyền ghi. Tạo role DB riêng với quyền tối thiểu theo provider.

### SQL Server

```sql
CREATE LOGIN dataguard_ro WITH PASSWORD = '<strong-password>';
CREATE USER dataguard_ro FOR LOGIN dataguard_ro;

-- Đọc catalog (schema, procedure signature, result-set shape)
GRANT VIEW DEFINITION TO dataguard_ro;

-- EXECUTE các stored procedure cần validate (sp_describe_first_result_set chạy trong context)
GRANT EXECUTE TO dataguard_ro;
```

Lưu ý: `sp_describe_first_result_set` cần quyền `EXECUTE` trên procedure để mô tả result set. Không cấp `db_owner`/`db_ddladmin`/`CONTROL`.

### Oracle

```sql
CREATE USER dataguard_ro IDENTIFIED BY "<strong-password>";
GRANT CREATE SESSION TO dataguard_ro;

-- Đọc data dictionary: ALL_ARGUMENTS, ALL_TAB_COLUMNS, ALL_PROCEDURES, NLS_SESSION_PARAMETERS
GRANT SELECT ON SYS.ALL_ARGUMENTS TO dataguard_ro;
GRANT SELECT ON SYS.ALL_TAB_COLUMNS TO dataguard_ro;
GRANT SELECT ON SYS.ALL_PROCEDURES TO dataguard_ro;
GRANT SELECT ON SYS.NLS_SESSION_PARAMETERS TO dataguard_ro;
```

Không cấp `SELECT ANY TABLE` — chỉ cần các view dictionary `ALL_*`.

### MySQL / PostgreSQL

```sql
-- MySQL
CREATE USER 'dataguard_ro'@'%' IDENTIFIED BY '<strong-password>';
GRANT SELECT ON information_schema.* TO 'dataguard_ro'@'%';

-- PostgreSQL
CREATE ROLE dataguard_ro LOGIN PASSWORD '<strong-password>';
GRANT USAGE ON SCHEMA information_schema TO dataguard_ro;
GRANT SELECT ON information_schema.routines, information_schema.parameters,
              information_schema.columns TO dataguard_ro;
```

## 3. Cấu hình khuyến nghị (`.dataguard.yml`)

```yaml
ground_truth_mode: Snapshot        # offline-first; không cần DB credentials trong CI
enable_telemetry: false            # zero egress
allow_plaintext_config_fallback: false   # dev-only flag — KHÔNG BAO GIỜ bật ở production
fail_on_drift: true                # CI gate: drift → exit 1
```

Connection string **không** đặt trong file config — dùng env var:

```bash
export DATAGUARD_CONNECTION_STRING="Server=...;Database=...;Trusted_Connection=True"
dataguard validate --config .dataguard.yml
```

## 4. Exit-code contract (cho CI consumer + extensions)

| Code | Ý nghĩa |
|---|---|
| `0` | Success — validation passed, no drift, hoặc informational output |
| `1` | Validation failures (error-severity) hoặc drift detected với `--fail-on-drift` |
| `2` | Configuration / usage error — invalid `--format`, machine-readable format thiếu `--output`, arguments không hợp lệ |

CI note: `snapshot diff` trả `0` khi drift nếu không có `--fail-on-drift`; trong CI env (`CI`/`GITHUB_ACTIONS`) in reminder bật flag.

## 5. Tuân thủ — điều KHÔNG được claim

- **Không claim compliance certification** (PCI DSS / SOC 2 / GDPR / ISO) khi chưa có audit độc lập.
- "~ms" performance claim phải có benchmark đo thật trước khi đưa vào tài liệu bán hàng.
- Banking profile không thay thế security review nội bộ — chỉ là cấu hình an toàn mặc định.
