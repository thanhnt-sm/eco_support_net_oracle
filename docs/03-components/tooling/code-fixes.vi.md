# Code Fix Providers

DataGuard cung cấp năm Roslyn code fix provider đề xuất sửa nhanh trong IDE cho các vi phạm contract phổ biến. Tất cả provider nhắm đến `netstandard2.0` và sử dụng `Microsoft.CodeAnalysis.CSharp` để thao tác cú pháp.

## Kiến trúc

```mermaid
graph TB
    subgraph "Diagnostics"
        DG001[DG001: Unvalidated SQL Call]
        DG002[DG002: Parameter Mismatch]
        DG006[DG006: Naming Convention]
        DG007[DG007: Length Exceeds Column]
        DG010-DG013[DG010-DG013: Dialect Issues]
        DG012[DG012: Provider Option Mismatch]
    end

    subgraph "Code Fix Providers"
        DCFP[DataGuardCodeFixProvider]
        MAFP[AddMaxLengthAttributeFixProvider]
        SCFP[SkipContractCheckFixProvider]
        NCFP[NamingConventionFixProvider]
        UOFP[UseOracleProviderFixProvider]
    end

    subgraph "Fix Actions"
        F1[Thêm [SkipContractCheck]]
        F2[Thêm comment CI-only]
        F3[Cập nhật SQL parameters]
        F4[Tự đổi tên property]
        F5[Thêm [Column] attribute]
        F6[Thêm [MaxLength]]
        F7[Đề xuất CLOB/NCLOB]
        F8[Thêm ghi chú phương ngữ]
        F9[Thêm .UseOracle()]
    end

    DG001 --> DCFP
    DG002 --> DCFP
    DG006 --> DCFP
    DG007 --> DCFP
    DG010-DG013 --> DCFP

    DCFP --> F1
    DCFP --> F2
    DCFP --> F3
    DCFP --> F4
    DCFP --> F5
    DCFP --> F6
    DCFP --> F7
    DCFP --> F8
    DCFP --> F9

    DG007 --> MAFP
    DG001 --> SCFP
    DG006 --> NCFP
    DG012 --> UOFP
```

## File nguồn

| File | Dòng | Mục đích |
|------|------|----------|
| `CodeFixProviders.cs` | 544 | Tất cả năm code fix provider |

## DataGuardCodeFixProvider

Code fix provider chính xử lý phần lớn các diagnostic ID.

### Diagnostic được hỗ trợ

| Diagnostic | Hành động sửa |
|------------|---------------|
| DG001 | Thêm `[SkipContractCheck]`, Thêm comment CI-only |
| DG002 | Cập nhật SQL khớp tham số mong đợi |
| DG006 | Tự sửa quy ước đặt tên, Thêm attribute `[Column]` |
| DG007 | Thêm attribute `[MaxLength]`, Đề xuất CLOB/NCLOB |
| DG010 | Thêm ghi chú chuyển đổi phương ngữ |
| DG011 | Thêm ghi chú chuyển đổi phương ngữ |
| DG013 | Thêm ghi chú chuyển đổi phương ngữ |

### Flow đăng ký fix

```mermaid
flowchart TD
    A[Nhận Diagnostic] --> B{Diagnostic ID?}
    B -->|DG001| C[RegisterUnvalidatedSqlCallFixes]
    B -->|DG002| D[RegisterParameterMismatchFixes]
    B -->|DG006| E[RegisterNamingConventionFixes]
    B -->|DG010/DG011/DG013| F[RegisterDialectFixes]
    B -->|DG007| G[RegisterLengthFixes]
    B -->|DG012| H[RegisterProviderOptionFixes]

    C --> C1[Thêm [SkipContractCheck]]
    C --> C2[Thêm comment CI-only]
    D --> D1[Cập nhật SQL parameters]
    E --> E1[Tự đổi tên theo quy ước]
    E --> E2[Thêm attribute [Column]]
    F --> F1[Thêm ghi chú chuyển đổi phương ngữ]
    G --> G1[Thêm [MaxLength]]
    G --> G2[Đề xuất CLOB/NCLOB]
    H --> H1[Thêm .UseOracle()]
```

### Triển khai fix

#### Thêm [SkipContractCheck]

Thêm `SkipContractCheckAttribute` vào method hoặc class bao quanh:

```csharp
[global::DataGuard.Contracts.SkipContractCheck(Reason = "Dynamic SQL - manual review required")]
public IQueryable<Customer> Search(string query) { ... }
```

**Triển khai:** Sử dụng `DocumentEditor.AddAttribute()` trên ancestor `MemberDeclarationSyntax`.

#### Thêm comment CI-only

Thêm comment `// DataGuard: Validate in CI only` phía trên câu lệnh gọi SQL:

```csharp
// DataGuard: Validate in CI only
var results = context.Customers.FromSqlRaw("SELECT * FROM Customers");
```

**Triển khai:** Sử dụng `DocumentEditor.ReplaceNode()` để thêm trivia vào `StatementSyntax`.

#### Cập nhật SQL khớp tham số

Đề xuất cập nhật chuỗi SQL để khớp tham số stored procedure mong đợi. Đây là fix placeholder thêm comment với danh sách tham số mong đợi.

#### Tự sửa quy ước đặt tên

Đổi tên property để khớp quy ước đặt tên đã cấu hình sử dụng `NameConventions.ToSnakeCase()` / `ToPascalCase()`:

```csharp
// Trước: public string customer_name { get; set; }
// Sau:   public string CustomerName { get; set; }
```

**Triển khai:** Sử dụng `Renamer.RenameSymbolAsync()` để đổi tên symbol an toàn across solution.

#### Thêm attribute [Column]

Thêm attribute `[Column]` rõ ràng khi tên property không khớp tên cột database:

```csharp
[global::System.ComponentModel.DataAnnotations.Schema.Column("customer_name")]
public string CustomerName { get; set; }
```

#### Thêm attribute [MaxLength]

Thêm attribute `[MaxLength]` để sửa diagnostic sai lệch độ dài:

```csharp
[global::System.ComponentModel.DataAnnotations.MaxLength(100)]
public string Name { get; set; }
```

**Triển khai:** Sử dụng `SyntaxFactory.Attribute()` với `SyntaxFactory.LiteralExpression()` cho giá trị độ dài.

#### Đề xuất CLOB/NCLOB

Thêm comment đề xuất thay đổi kiểu cột thành CLOB/NCLOB cho trường text lớn.

#### Thêm ghi chú chuyển đổi phương ngữ

Thêm comment ghi chú rằng cần chuyển đổi phương ngữ thủ công:

```csharp
// DataGuard: Manual dialect conversion required - Oracle DECODE needs CASE WHEN in SQL Server
```

#### Thêm .UseOracle()

Đề xuất thêm `.UseOracle()` vào `DbContextOptionsBuilder` cho DG012 (Không khớp tùy chọn provider).

## Hỗ trợ batch fix

Tất cả provider hỗ trợ `FixAllProvider` qua `WellKnownFixAllProviders.BatchFixer`:

```csharp
public sealed override FixAllProvider GetFixAllProvider()
    => WellKnownFixAllProviders.BatchFixer;
```

Cho phép các hành động "Fix All in Document", "Fix All in Project", và "Fix All in Solution" trong IDE.

## Mẫu Syntax Factory

Các code fix sử dụng mẫu nhất quán để tạo attribute:

### Attribute với tham số chuỗi

```csharp
SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::DataGuard.Contracts.SkipContractCheck"))
    .WithArgumentList(SyntaxFactory.AttributeArgumentList(
        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal("reason")))
            .WithNameEquals(SyntaxFactory.NameEquals(
                SyntaxFactory.IdentifierName("Reason"))))));
```

### Attribute với tham số số

```csharp
SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.ComponentModel.DataAnnotations.MaxLength"))
    .WithArgumentList(SyntaxFactory.AttributeArgumentList(
        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(100))))));
```

### Global qualification

Tất cả tên attribute sử dụng qualification `global::` để tránh xung đột namespace:

- `global::DataGuard.Contracts.SkipContractCheck`
- `global::System.ComponentModel.DataAnnotations.MaxLength`
- `global::System.ComponentModel.DataAnnotations.Schema.Column`

## Sử dụng trong IDE

### Visual Studio

Click chuột phải vào squiggle diagnostic → "Quick Actions and Refactorings" → Chọn fix.

### VS Code

Hover qua diagnostic → Click "Quick Fix" (bóng đèn) → Chọn fix.

### Phím tắt

- **Visual Studio:** `Ctrl+.` (Windows) / `Cmd+.` (Mac)
- **VS Code:** `Ctrl+.` (Windows) / `Cmd+.` (Mac)
