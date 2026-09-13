# Code Fix Providers

DataGuard cung cấp năm Roslyn code fix provider cho vi phạm contract. Tất cả provider nhắm đến `netstandard2.0` và sử dụng `Microsoft.CodeAnalysis.CSharp` để thao tác cú pháp. Bộ test code-fix kiểm chứng accounting provider/action; một diagnostic chỉ được quảng bá khi có biến đổi cú pháp thực sự.

## Kiến trúc

```mermaid
graph TB
    subgraph "Diagnostics"
        DG001[DG001: Unvalidated SQL Call]
        DG002[DG002: Parameter Mismatch]
        DG006[DG006: Naming Convention]
        DG007[DG007: Length Exceeds Column]
        DG012[DG012: Provider Option Mismatch]
    end

    subgraph "Code Fix Providers"
        DCFP[DataGuardCodeFixProvider]
        MAFP[AddMaxLengthAttributeFixProvider]
        SCFP[SkipContractCheckFixProvider]
        NCFP[NamingConventionFixProvider]
        UOFP[UseOracleCodeFixProvider]
    end

    subgraph "Fix Actions"
        F1[Thêm [SkipContractCheck]]
        F2[Áp dụng SQL replacement đã xác thực]
        F3[Tự đổi tên property]
        F4[Thêm [Column] attribute]
        F5[Thêm [MaxLength]]
        F6[Thay UseSqlServer bằng UseOracle]
    end

    DG001 --> DCFP
    DG002 --> DCFP
    DG007 --> DCFP

    DCFP --> F1
    DCFP --> F2
    DCFP --> F3
    DCFP --> F4
    DCFP --> F5
    DCFP --> F6

    DG007 --> MAFP
    DG001 --> SCFP
    DG006 --> NCFP
    DG012 --> UOFP
```

## File nguồn

| File | Dòng | Mục đích |
|------|------|----------|
| `CodeFixProviders.cs` | — | Code-fix provider và các biến đổi |

## DataGuardCodeFixProvider

Code fix provider chính xử lý DG001, DG002 và DG007. Biến đổi naming và provider-option do provider chuyên biệt xử lý.

`FixableDiagnosticIds` được quảng bá chỉ gồm các diagnostic bên dưới có transformation
an toàn đã đăng ký; diagnostic chưa có sửa đổi an toàn được chủ ý loại khỏi danh sách.

### Diagnostic được hỗ trợ

| Diagnostic | Hành động sửa |
|------------|---------------|
| DG001 | Thêm declaration `[SkipContractCheck]`, `[DataContract]` hoặc `[SqlParameter]` |
| DG002 | Áp dụng SQL replacement do verifier phê duyệt (chỉ khi có thuộc tính manifest hợp lệ) |
| DG007 | Thêm attribute `[MaxLength]` |

### Flow đăng ký fix

```mermaid
flowchart TD
    A[Nhận Diagnostic] --> B{Diagnostic ID?}
    B -->|DG001| C[RegisterUnvalidatedSqlCallFixes]
    B -->|DG002| D[RegisterParameterMismatchFixes]
    B -->|DG007| G[RegisterLengthFixes]

    C --> C1[Thêm contract declaration hoặc SkipContractCheck]
    D --> D1[Áp dụng SQL replacement đã xác thực]
    G --> G1[Thêm [MaxLength]]
```

### Triển khai fix

#### Thêm [SkipContractCheck]

Thêm `SkipContractCheckAttribute` vào method hoặc class bao quanh:

```csharp
[global::DataGuard.Contracts.SkipContractCheck(Reason = "Dynamic SQL - manual review required")]
public IQueryable<Customer> Search(string query) { ... }
```

**Triển khai:** Sử dụng `DocumentEditor.AddAttribute()` trên ancestor `MemberDeclarationSyntax`.

#### Thêm khai báo manual contract

Với SQL call DG001 trong một type, provider có thể thêm declaration
`[DataContract]` fully qualified vào type đó. Khi method bao quanh có parameter,
nó có thể thêm một declaration `[SqlParameter]` fully qualified cho mỗi
parameter. Hai action không tự tạo database type hay routine identity; manual
extractor dùng CLR parameter name/type cho đến khi có metadata đã xác minh. Cả
hai transformation được compile-check trong code-fix tests.

#### Cập nhật SQL khớp tham số

Chỉ đưa SQL replacement khi diagnostic mang cả replacement đã được verifier phê duyệt và digest SHA-256 64 ký tự của offline contract manifest. Action thay string literal nên vẫn biên dịch được. Manifest thiếu, sai định dạng hoặc không ràng buộc sẽ không có automatic edit; comment hay heuristic không thể xác lập identity, kiểu hoặc direction của routine.

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

Áp dụng giá trị `[MaxLength]` đã được verifier phê duyệt cho DG007/DG009 chỉ khi
 diagnostic có manifest digest hợp lệ và độ dài dương đã xác minh. Provider thay
`MaxLength` hoặc `StringLength` có sẵn để tránh attribute trùng:

```csharp
[global::System.ComponentModel.DataAnnotations.MaxLength(100)]
public string Name { get; set; }
```

Provider không tự suy diễn database length và không đưa action khi thiếu evidence này.

#### CLOB/NCLOB và chuyển đổi phương ngữ

Không có automatic conversion cho CLOB/NCLOB hay phương ngữ SQL. Hai thao tác cần schema evidence theo provider; comment không phải remediation và provider không quảng bá diagnostic này là fixable khi chưa có biến đổi đã được xác thực.

#### Thêm .UseOracle()

Thay `.UseSqlServer()` bằng `.UseOracle()` trong `DbContextOptionsBuilder` cho DG012 (Không khớp tùy chọn provider), giữ nguyên connection-string argument.

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
