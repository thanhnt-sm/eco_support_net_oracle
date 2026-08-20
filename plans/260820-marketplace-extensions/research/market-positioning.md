---
status: completed
date: 2026-08-20
scope: DataGuard product positioning and enterprise adoption
---

# Research: DataGuard market positioning

## Kết luận

DataGuard không nên cạnh tranh ở surface đã đông: SQL editor, autocomplete, SQL formatter, generic static lint, hoặc database GUI. Marketplace đã có đại diện SQL Schema Guard, SQL Script Compare, T-SQL Analyzer, ApexSQL Diff/Enforce và SlowQL. Vị trí khác biệt có thể kiểm chứng là **contract lifecycle intelligence**: phát hiện thay đổi schema/procedure/result contract tác động trực tiếp tới EF/Dapper/DTO/consumer, phân loại compatibility, và sinh evidence an toàn cho CI/banking.

## Market map

| Nhóm | Ví dụ Marketplace | Nhu cầu đã được đáp ứng | DataGuard không duplicate |
|---|---|---|---|
| SQL lint/static quality | T-SQL Analyzer, SQL Enlight, SlowQL | style, anti-pattern, offline query finding | editor/linter SQL tổng quát |
| Schema/script diff | SQL Schema Guard, SQL Script Compare, ApexSQL Diff | DDL/script/schema difference | GUI/schema compare toàn diện |
| Deployment controls | ApexSQL Enforce, SQL toolchain | policy deploy/DBA workflow | deployment engine/database GUI |
| DataGuard opportunity | chưa thấy entry có evidence tương đương trong result khảo sát | app-code ↔ stored procedure/result schema compatibility, provider-aware drift, SARIF/evidence | phải chứng minh bằng corpus/CI outcomes, không slogan |

## Persona-value mapping

| Persona | Job-to-be-done | DataGuard promise có thể đo |
|---|---|---|
| Backend .NET | phát hiện EF/Dapper/SP contract vỡ trước integration test/production | precise location + expected/actual type/length/nullability/parameter mismatch; fail CI only theo policy |
| Full-stack | biết DB/DTO thay đổi nào phá API/client | versioned evidence + compatibility classification + generated types chỉ từ contract validated |
| Frontend | nhận thay đổi có action, không cần DB access | consumed artifact/type; breaking marker; không cài DB driver/credential |
| DBA/platform | tránh surprise drift/deploy data loss | snapshot/diff evidence, provider-specific semantics, bounded/least-privilege reads |
| Bank/security | prove tool không làm lộ data/secret và release không bị tamper | offline profile, redaction, no telemetry, SBOM/provenance/checksum, signed artifact, explicit policy |

## Marketplace discovery requirements

VS Code official manifest docs yêu cầu `name`, `version`, `publisher`, `engines.vscode`; search/discovery dùng `displayName`, description, allowed categories và tối đa 30 keywords. Icon tối thiểu 128x128 (256x256 Retina); repository/homepage/bugs/license hiển thị resource links.

**Decision**: VS Code metadata phải dùng categories `Linters`, `Testing`; keywords có intent thật: `database`, `sql`, `stored-procedure`, `dapper`, `entity-framework`, `schema-drift`, `contract-testing`, `sarif`, `devsecops`. Không SEO-stuff hoặc claim compliance certification chưa có audit.

## Enterprise adoption controls

NIST SBOM/supply-chain guidance và Microsoft DevOps Security guidance củng cố các requirements: inventory artifact/dependency, provenance, integrity, policy gate, logging không secret, least privilege và reviewable evidence. Với on-prem/banking, “no network egress/no telemetry/offline snapshot” là feature adoption chứ không phải toggle marketing.

**License decision**: giữ MIT hiện tại cho extension source/package, công khai scope. Không thêm pricing/paywall/claim SOC2/PCI/GDPR compliance khi chưa có legal/audit evidence.

## Evidence-backed priority backlog

| Priority | Outcome | Why now | Explicit non-goal |
|---|---|---|---|
| P0 | correct CLI SARIF/output/evidence contract, redaction, exit codes, policy profile | prerequisite cho mọi host và CI | new SQL parser/DB GUI |
| P0 | VS Code trusted single-flight command + diagnostics; Visual Studio equivalent | visible workflow value, preserves host safety | auto-run on typing/live production scan |
| P0 | deterministic two-VSIX packaging, SBOM/checksum/provenance, publisher gate | enterprise acquisition blocker | public publish without owner publisher/secret |
| P1 | breaking-change classification + TypeScript export from validated contract | bridges full-stack pain point | infer client types from arbitrary live result |
| P1 | provider-specific drift/migration evidence and policy profiles | DBA/bank adoption | database deployment engine |
| P2 | team evidence portal/dashboard only if artifact consumption proves insufficient | not core before users validate need | generic project management/observability suite |

## Sources

1. [VS Code extension manifest and Marketplace presentation](https://code.visualstudio.com/api/references/extension-manifest)
2. [VS Code publishing and publisher requirements](https://code.visualstudio.com/api/working-with-extensions/publishing-extension)
3. [Microsoft SQLPackage drift/deploy reports](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-deploy-drift-report?view=sql-server-ver17)
4. [NIST SBOM guidance](https://www.nist.gov/itl/executive-order-14028-improving-nations-cybersecurity/software-supply-chain-security-guidance-20)
5. Marketplace competitor samples: [T-SQL Analyzer](https://marketplace.visualstudio.com/items?itemName=ErikEJ.TSqlAnalyzer), [SQL Schema Guard](https://marketplace.visualstudio.com/items?itemName=prestonabraham.sql-schema-guard), [SlowQL](https://marketplace.visualstudio.com/items?itemName=Makroumi.slowql-vscode).

## Unresolved

- Validate demand with 5–10 target .NET/DBA teams before P2 portal/dashboard work.
- Publisher identity and Marketplace permission remain owner prerequisites.
