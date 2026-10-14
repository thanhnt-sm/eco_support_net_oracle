<div align="center">
  <p>
    <a href="README.md">English</a> | <b>Tiếng Việt</b>
  </p>
</div>

# DataGuard — Kiểm tra hợp đồng (Contract) giữa Entity ↔ Stored Procedure / Raw SQL

[![License: GPL-3.0-only + Commercial](https://img.shields.io/badge/license-GPL--3.0--only%20%2B%20Commercial-blue.svg)](LICENSE)
[![codecov](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle)
[![OpenSSF Scorecard](https://api.securityscorecards.dev/projects/github.com/thanhnt-sm/eco_support_net_oracle/badge)](https://scorecard.dev/viewer/?uri=github.com/thanhnt-sm/eco_support_net_oracle)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/15184/badge)](https://www.bestpractices.dev/projects/15184)

**DataGuard** phát hiện lệch lạc (drift) giữa entity .NET và SQL mà chúng phụ thuộc — tham số stored procedure, hình dạng result set, nullability, ngữ nghĩa độ dài (CHAR/BYTE), lệch dialect — ngay tại thời điểm thiết kế và trong CI.

> **Vì sao tồn tại:** EF Core đã ghi nhận khoảng trống kiểm tra contract cho stored procedure từ [Microsoft EF issue #245 (2014)](https://github.com/dotnet/efcore/issues/245) và từ chối xây dựng. DataGuard đưa mẫu *model contracts* mà **dbt** đã chứng minh cho data engineering (preflight kiểm tra cột/tham số lúc compile, từ Core v1.5, 2023) vào thế giới stored-procedure/.NET.

## Bắt đầu nhanh

```bash
# Tải CLI dataguard cho OS của bạn từ GitHub Releases (dataguard-<version>-<rid>.zip) và kiểm tra
# SHA-256 với file .sha256 đi kèm (DataGuard.Cli chưa được publish lên nuget.org):
# https://github.com/thanhnt-sm/eco_support_net_oracle/releases
cd YourProject
dataguard init            # tạo .dataguard.yml (snapshot refresh tạo .dataguard-snapshot.json)
dataguard validate        # chạy rule contract với ground truth
dataguard snapshot diff   # phát hiện schema drift so với snapshot đã commit
```

## Ba chế độ ground truth

| Chế độ | Nguồn | Dùng khi |
|--------|-------|----------|
| **Full** | Kết nối DB trực tiếp | CI có credential được DBA duyệt |
| **Snapshot** *(mặc định)* | File `snapshot.json` commit trong repo | Zero credential CI; validate offline |
| **Manual** | Attribute `[ExpectedColumn]` / `[ExpectedSpParameter]` | Chỉ attribute, không cần DB |

Tầng IDE (`DataGuard.Analyzers`) đánh dấu lời gọi SQL chưa validate bằng incremental generator siêu nhẹ; tầng CI (`dataguard validate`) chạy toàn bộ diff engine với ground truth từ database.

## Quy tắc

Rule adapter nào chạy tùy `--provider` (`src/DataGuard.Cli/ProviderRuleCatalog.cs`; DG010 cũng chạy cho SQL Server, MySQL và PostgreSQL); bỏ qua rule bằng `--skip-rules DG017,MY005`.

<!-- rule-table:start -->
<!-- Sinh bởi `scripts/gen_rule_table.py` từ `ProviderRuleCatalog.RuleTitles` và source của rule; không sửa tay (`--check` chạy trong `scripts/verify_docs_sync.sh`). Tên/mô tả rule giữ nguyên tiếng Anh theo source. -->

### Engine lõi (mọi provider)

| ID | Quy tắc | Mô tả |
|----|------|------|
| DG002 | Parameter Type Match | Parameter CLR types must match database types |
| DG003 | Parameter Direction (In/Out/Return) | Parameter direction must match call site (in/out/ref) |
| DG004 | Result Set Column Shape | Result set columns must match entity properties |
| DG005 | Nullable Compatibility | Database column nullability should match the mapped entity property nullability |
| DG006 | Naming Convention Compliance | Database column names should follow naming convention vs C# properties |
| DG015 | Phantom Table Reference | Raw SQL references a table that does not exist in the database schema |
| DG016 | Phantom Column Reference | Raw SQL references a column that does not exist in the referenced table |
| DG017 | Avoid SELECT * | Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation |
| DG018 | Live Query Shape Mismatch | Validates live database result set columns and types against C# object properties |
| DG019 | Raw SQL Parse Error | Raw SQL must parse successfully before validation |
| DG020 | Undetermined Query Shape | Reported by `LiveSqlShapeValidationRule` (`UndeterminedShapeRuleId`) |
| DG101 | Parameter Count Match | Stored procedure calls must resolve to a catalog procedure and supply exactly its required parameters |

### Adapter Oracle

| ID | Quy tắc | Mô tả |
|----|------|------|
| DG007 | Entity Length Exceeds Column | Entity property MaxLength exceeds Oracle column MaxLength |
| DG008 | Multi-Byte Length Overflow Risk | Entity property may exceed Oracle column byte capacity in BYTE semantics |
| DG009 | Inferred Size Fallback Risk | EF Core infers NVARCHAR2(2000) which may cause ORA-12899 |
| DG010 | Oracle Syntax in Non-Oracle Context | Oracle-specific syntax detected in non-Oracle context |
| DG011 | Non-Oracle Function in Oracle Context | SQL Server/MySQL function used in Oracle context |
| DG012 | Provider Option Mismatch | Database context doesn't match configured provider |
| DG013 | SQL Server Syntax Leak | SQL Server EXEC syntax or bracket-quoted identifiers used in Oracle context |
| DG014 | Unmapped Type Usage | Raw SQL uses type not mapped by Oracle EF Core provider |

### Adapter MySQL

| ID | Quy tắc | Mô tả |
|----|------|------|
| MY001 | MySQL Syntax in Non-MySQL Context | MySQL-specific syntax detected in non-MySQL context |
| MY002 | Non-MySQL Syntax in MySQL Context | Non-MySQL syntax (SQL Server/Oracle/PostgreSQL) detected in MySQL context |
| MY003 | MySQL VARCHAR Byte Limit | MySQL column exceeds 65535-byte row limit or entity MaxLength exceeds TEXT type maximum |
| MY004 | Entity Length Exceeds MySQL Column Length | Entity property MaxLength exceeds MySQL column CHARACTER_MAXIMUM_LENGTH |
| MY005 | Row Size Overflow Risk | VARCHAR/CHAR widths implied by the entity exceed MySQL's 65535-byte row limit (charset-aware) |
| MY006 | TEXT/BLOB Type Overflow Risk | Entity MaxLength exceeds MySQL TEXT/BLOB family type maximum |
| MY007 | Inferred Size Fallback Risk | String property without MaxLength (Pomelo maps it to longtext) is stored in a bounded VARCHAR/CHAR column |

### Adapter PostgreSQL

| ID | Quy tắc | Mô tả |
|----|------|------|
| PG001 | PostgreSQL Syntax in Non-PostgreSQL Context | PostgreSQL-specific syntax detected in non-PostgreSQL context |
| PG002 | Non-PostgreSQL Syntax in PostgreSQL Context | Non-PostgreSQL syntax (SQL Server/Oracle/MySQL) detected in PostgreSQL context |
| PG003 | Entity Length Exceeds PostgreSQL Column Length | Entity property MaxLength exceeds PostgreSQL column length |
| PG004 | PostgreSQL Provider Option Mismatch | Database context doesn't match configured PostgreSQL provider |
| PG005 | PostgreSQL Raw SQL Unmapped Type Usage | Raw SQL uses type not mapped by Npgsql EF Core provider |

### Chỉ analyzer (Roslyn, IDE/build)

| ID | Quy tắc | Mô tả |
|----|------|------|
| DG001 | Track Unvalidated SQL Calls | Marks SQL calls that haven't been validated against database schema. Run full validation in CI. |
| DG097 | Stored procedure command text form | A text command that only names a procedure must start with EXEC/EXECUTE/CALL; a CommandType.StoredProcedure command must name the procedure without that prefix. |
| DG098 | Raw SQL query missing FROM clause | Raw SQL SELECT query is missing a FROM clause. |
| DG099 | Potential SQL injection pattern | Raw SQL contains a pattern that may indicate SQL injection, or is built by concatenation/interpolation into a raw SQL API. |

### Assessment (`dataguard assess`)

| ID | Quy tắc | Mô tả |
|----|------|------|
| DG1004 | Inventory | Legacy non-SDK project format detected. |
| DG1101 | Inventory | Target framework '…' has no curated support-table entry; support status Unknown. |
| DG1102 | Inventory | Target framework '…' is out of support (…). |
| DG1103 | Inventory | Target framework '…' reaches end of support on …. |
| DG1201 | Inventory | Package references exist but no packages.lock.json was found next to the project. |
| DG1202 | Dependency health | Project declares '…' but the committed lock file has no matching target framework section. |
| DG1203 | Dependency health | packages.lock.json is not valid JSON. |
| DG1204 | Dependency health | packages.lock.json exceeds the … byte safety limit. |
| DG1217 | Remote advisory (OSV, opt-in) | OSV advisory … affects package … …. |
| DG1301 | Build/CI | Projects target … but no global.json pins the workspace SDK. |
| DG1302 | Build/CI | Pinned SDK … does not correspond to any project TFM major (…). |
| DG1303 | Build/CI | global.json is not valid JSON. |
| DG1401 | Configuration and secrets | Config value at '…:…' matches a secret-like key name ('…'); value is redacted. |
| DG1402 | Configuration and secrets | Config references a machine-specific absolute path at line …. |

<!-- rule-table:end -->

## Tài liệu

- [Tổng quan giải pháp](docs/SOLUTION.md) · [Sản phẩm](docs/PRODUCT.md) · [Cách dùng](docs/USAGE.md) · [Kiến trúc](docs/architecture/architecture.md) · [Bảo mật](SECURITY.md)

## Độ phủ Kiểm thử & Chỉ số Chất lượng

Dữ liệu độ phủ dòng lệnh (line), nhánh (branch) và phân tích kiểm thử (Test Analytics) được công bố liên tục lên [Codecov](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle) qua OIDC (`codecov/codecov-action@v7.1.1`) theo cấu hình tại [`.github/codecov.yml`](.github/codecov.yml).

### Cờ Bộ Kiểm thử (Test Suite Flags)

| Flag | Phạm vi bộ kiểm thử | Độ phủ |
|---|---|---|
| `dotnet-unit` | Kiểm thử đơn vị & hợp đồng .NET 9 đa nền tảng (`DataGuard.CrossPlatform.slnf`) | [![dotnet-unit](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&flag=dotnet-unit)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/flags) |
| `live-db` | Kiểm thử tích hợp CSDL thực qua Testcontainers (Oracle, SQL Server, PostgreSQL, MySQL) | [![live-db](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&flag=live-db)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/flags) |
| `vscode-ext` | Kiểm thử đơn vị TypeScript & bảo mật giao diện của VS Code Extension (Node 22) | [![vscode-ext](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&flag=vscode-ext)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/flags) |

### Thành phần Kiến trúc (Components)

| Component ID | Phân hệ | Đường dẫn theo dõi | Độ phủ |
|---|---|---|---|
| `core_engine` | Core Engine & Contracts | `src/DataGuard.Core/**`, `src/DataGuard.Contracts/**`, `src/DataGuard.SqlClassification/**` | [![core_engine](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&component_id=core_engine)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/components) |
| `db_adapters` | Database Adapters | `src/DataGuard.{Oracle,SqlServer,PostgreSql,MySql}.Adapter/**` | [![db_adapters](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&component_id=db_adapters)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/components) |
| `roslyn_analyzers` | Roslyn Analyzers & CodeFixes | `src/DataGuard.Analyzers/**`, `src/DataGuard.CodeFixes/**` | [![roslyn_analyzers](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&component_id=roslyn_analyzers)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/components) |
| `cli_and_host` | CLI, Host, Build & LSP | `src/DataGuard.{Cli,Host,Build,LanguageServer}/**` | [![cli_and_host](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&component_id=cli_and_host)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/components) |
| `observability` | Enterprise Observability | `src/DataGuard.Observability*/**` | [![observability](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&component_id=observability)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/components) |
| `ide_extensions` | VS Code & Visual Studio Extensions | `src/DataGuard.VSCode/**`, `src/DataGuard.VisualStudio/**` | [![ide_extensions](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graph/badge.svg?branch=main&component_id=ide_extensions)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle/components) |

### Biểu đồ Kiến trúc Độ phủ

| Biểu đồ Phân cấp Sunburst | Lưới Độ phủ Tệp (Grid) |
|---|---|
| [![Codecov Sunburst](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graphs/sunburst.svg?branch=main)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle) | [![Codecov Grid](https://codecov.io/gh/thanhnt-sm/eco_support_net_oracle/graphs/grid.svg?branch=main)](https://app.codecov.io/gh/thanhnt-sm/eco_support_net_oracle) |

## Giấy phép kép & FAQ

Từ **v0.4.0**, DataGuard được phát hành theo hai giấy phép:

1. **GNU GPL phiên bản 3 (chỉ v3)** (`GPL-3.0-only`, xem [`LICENSE`](LICENSE)), kèm một quyền bổ sung (additional
   permission) cho driver cơ sở dữ liệu và IDE host mà DataGuard chạy cùng ([`docs/legal/ADDITIONAL-PERMISSIONS.md`](docs/legal/ADDITIONAL-PERMISSIONS.md)).
2. **Giấy phép thương mại** cho doanh nghiệp muốn nhúng DataGuard vào sản phẩm mã đóng mà họ phân phối. Liên hệ
   `<contact email placeholder>`. Điều khoản được thỏa thuận riêng với từng bên được cấp phép; không có bảng giá hay biểu mẫu công khai.

> **Tình trạng pháp lý:** văn bản quyền bổ sung và điều khoản thương mại **chưa được luật sư xem xét**. Nội dung ở đây
> không phải tư vấn pháp lý. Nếu vấn đề giấy phép quan trọng với bạn, hãy hỏi luật sư của bạn.

**GPL-3.0 có cấm doanh nghiệp dùng DataGuard thương mại không?** Không. GPL-3.0 cho phép dùng thương mại, sao chép và
sửa đổi. Chạy `dataguard validate` trong CI hoặc dùng analyzer khi build sản phẩm của bạn tự nó không đòi hỏi giấy phép thương mại.

**Copyleft áp dụng khi nào?** Khi bạn *phân phối* một tác phẩm dựa trên DataGuard, ví dụ đóng mã hoặc binary của
DataGuard vào sản phẩm. Khi đó bạn phải cung cấp tác phẩm đó theo GPL-3.0, hoặc mua giấy phép thương mại. Dùng một công cụ
nội bộ không phải là phân phối.

**Có thể làm repository không cho sao chép được không?** Không. Repository công khai và điều khoản GitHub cho phép fork.
Các bản đến hết v0.3.0 đã phát hành theo giấy phép MIT và **giữ MIT vĩnh viễn**; thay đổi này không hồi tố.

**Phần nào không thuộc GPL?**

- `DataGuard.Contracts` được cấp phép theo giấy phép MIT có chủ đích. Gói này nằm trong ứng dụng của bạn và được DataGuard đọc
  bằng reflection nên không được kéo mã của bạn vào GPL.
- `Oracle.ManagedDataAccess.Core` và `Microsoft.Data.SqlClient.SNI.runtime` đi kèm CLI, file zip theo RID, container image
  và extension IDE theo điều khoản riêng của nhà cung cấp. Văn bản giấy phép nằm ở
  [`docs/legal/THIRD-PARTY-NOTICES.md`](docs/legal/THIRD-PARTY-NOTICES.md), đi kèm mọi bản tải DataGuard.

**Gói analyzer để lại gì trong output của tôi?** `DataGuard.Analyzers` là development dependency chỉ chạy lúc build: assembly
analyzer nạp trong compiler từ `analyzers/dotnet/cs` và gói không có `lib/`, nên `DataGuard.Analyzers.dll` và
`DataGuard.SqlClassification.dll` không xuất hiện trong output build của bạn (kiểm bằng `scripts/verify-analyzer-packaging.sh`).
Phát biểu này chỉ áp dụng cho gói Analyzers. `DataGuard.Contracts` (MIT) được chép vào output khi bạn dùng attribute của nó;
các gói khác như `DataGuard.Build` hay `DataGuard.Core` đặt DLL của chúng vào đó và theo giấy phép riêng.

**Mã do AI hỗ trợ có được bảo hộ không?** Một số commit có đồng tác giả là trợ lý AI. Bảo hộ bản quyền cho tác phẩm có AI hỗ trợ
còn hạn chế và chưa ngã ngũ; đây là rủi ro cần biết, không phải khẳng định theo hướng nào.

**Văn bản đầy đủ:** [`LICENSE`](LICENSE) (GPL-3.0),
[`docs/legal/ADDITIONAL-PERMISSIONS.md`](docs/legal/ADDITIONAL-PERMISSIONS.md),
[`docs/legal/THIRD-PARTY-NOTICES.md`](docs/legal/THIRD-PARTY-NOTICES.md) và văn bản MIT của v0.1.0-v0.3.0
([`docs/legal/MIT-v0.1.0-v0.3.0.txt`](docs/legal/MIT-v0.1.0-v0.3.0.txt)).
