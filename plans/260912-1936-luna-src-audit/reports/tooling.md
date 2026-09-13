# Báo cáo Luna — tooling DataGuard

## Phạm vi và phương pháp

Đã đọc đầy đủ 14 file tracked thuộc `src/DataGuard.Contracts/`,
`src/DataGuard.Analyzers/` và `src/DataGuard.CodeFixes/` (source, project,
lock, `stylecop.json`, polyfill), đối chiếu với test Roslyn/code-fix, caller
trong Core/CLI, cùng các tài liệu tooling/contracts, feature showcase,
`docs/PRODUCT.md` và ADR-002. Baseline được cung cấp là `93bf728...`; không
chạy build/test theo phân công verification worker và không sửa source.

## Ledger file đã đọc

| File | Nội dung đã kiểm tra |
|---|---|
| `src/DataGuard.Contracts/ContractAttributes.cs` | `ParameterDirection`, ba contract attributes và nullable/direction parsing |
| `src/DataGuard.Contracts/NameConventions.cs` | `ToSnakeCase`, `ToPascalCase` |
| `src/DataGuard.Contracts/DataGuard.Contracts.csproj` | `netstandard2.0`, package metadata, StyleCop additional file |
| `src/DataGuard.Contracts/packages.lock.json` | dependency graph/lock hash |
| `src/DataGuard.Contracts/stylecop.json` | documentation/company/using policy |
| `src/DataGuard.Analyzers/Analyzers.cs` | IDs/descriptors, incremental generator, semantic analyzer và emission paths |
| `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj` | Roslyn packages, project reference, packaging analyzer/generator/contracts |
| `src/DataGuard.Analyzers/IsExternalInit.cs` | `init` polyfill |
| `src/DataGuard.Analyzers/packages.lock.json` | dependency graph/lock hash |
| `src/DataGuard.Analyzers/stylecop.json` | documentation/company/using policy |
| `src/DataGuard.CodeFixes/CodeFixProviders.cs` | 3 exported providers, fixable IDs, action registration và helpers |
| `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj` | Workspaces package, references, codefix packaging |
| `src/DataGuard.CodeFixes/packages.lock.json` | dependency graph/lock hash |
| `src/DataGuard.CodeFixes/stylecop.json` | documentation/company policy |

## Bản đồ thực tế

`DiagnosticIds` khai báo `DG001`–`DG016` và `DG098`/`DG099` tại
`src/DataGuard.Analyzers/Analyzers.cs:24-61`. `DG001` thuộc
`UnvalidatedSqlCallGenerator` (IDE); generator phát hiện các method EF
`FromSql*`, `ExecuteSql*`, prefix Dapper `Query*`/`Execute*`, hoặc literal có
keyword SQL (`Analyzers.cs:238-383`). `ContractValidationAnalyzer` quảng bá 17
descriptor còn lại (`Analyzers.cs:474-499`) nhưng implementation inline chỉ
phát ra `DG099`, `DG098`, và đường stored-procedure có `DG002`
(`Analyzers.cs:642-706`). Các rule có ground truth thật (`DG101`, `DG002`–`DG006`,
`DG015`/`DG016`) nằm ở Core; ví dụ `ParameterCountRule` là `DG101`
(`src/DataGuard.Core/Rules/ContractRules.cs:52-60`), còn `DG002` là
`ParameterTypeMatchRule` (`:100-108`). Vì vậy `DG001` không phải parameter-count
rule và không được gộp với `DG101`.

Source chỉ có 3 class có `[ExportCodeFixProvider]`:
`DataGuardCodeFixProvider`, `AddMaxLengthAttributeFixProvider`,
`SkipContractCheckFixProvider` (`src/DataGuard.CodeFixes/CodeFixProviders.cs:26-28,
437-439,493-495`). Provider chính khai báo 14 fixable IDs (`:30-46`) nhưng
switch chỉ đăng ký action cho `DG001`, `DG002`, `DG006`, `DG007`, `DG010`,
`DG011`, `DG012`, `DG013` (`:62-85`). `DG003`, `DG004`, `DG005`, `DG008`, và
`DG014` vì thế bị quảng bá nhưng không có action; `DG009` chỉ được provider
riêng xử lý. Provider riêng xử lý `DG007`/`DG009` (`:441-469`) và provider
skip xử lý `DG001` (`:497-525`). Các helper thêm
`ExpectedSpParameterAttribute` tồn tại nhưng không có caller
(`:98-112,254-294`); action hiện tại cho `DG002` chỉ thêm comment review,
không cập nhật SQL (`:296-315`).

## Findings

### TL-01 — P1, confidence cao: advertised code-fix IDs không khớp action

`DataGuardCodeFixProvider.FixableDiagnosticIds` nhận 14 IDs nhưng switch
không xử lý 5 ID nói trên; riêng `DG009` tạo impression provider chính xử lý
nhưng thực tế chỉ provider phụ có action. Đây là lỗi contract của Roslyn:
IDE có thể chọn provider nhưng không nhận được code action. Test hiện chỉ kiểm
tra `FixableDiagnosticIds`/FixAll, không gọi `RegisterCodeFixesAsync`
(`tests/DataGuard.CodeFixes.Tests/CodeFixProviderTests.cs:20-163`), nên không
bắt được. Khuyến nghị: mỗi provider chỉ quảng bá ID thực sự có registration,
hoặc thêm action/tests thực thi cho từng ID; kiểm thử mapping ID → số action.

### TL-02 — P2, confidence cao: tài liệu đếm nhầm provider và mô tả action không tồn tại

`docs/03-components/tooling/code-fixes.md:3,18-24,60-80` và bản `.vi.md`
mô tả 5 provider (`NamingConventionFixProvider`, `UseOracleProviderFixProvider`)
nhưng source không có hai class đó; đây thực chất là các nhánh action bên
trong provider chính. Tài liệu còn liệt kê 9 action chính, trong khi source có
11 registration kể cả hai provider phụ (`CodeFixProviders.cs:138-237,
464-525`). `docs/PRODUCT.md:142-148` ghi “12 CodeFixProviders”, cần sửa thành
3 exported providers và phân biệt riêng số action/ID. `feature-showcase.md`
(`:211-218`) lại quảng bá `[DataContract]`, `[SqlParameter]` và
`DataGuard.Validate()` không có trong source code-fix này. Khuyến nghị cập
nhật cả bản tiếng Anh/Việt và giữ bảng canonical ID → provider → action.

### TL-03 — P2, confidence cao: mô tả “CI heavy/full semantic + database” sai

`Analyzers.cs:470-473` ghi full semantic/database, nhưng implementation chỉ
đăng ký `OperationKind.Invocation` và các kiểm tra string inline
(`:501-540,642-706`); chính comment source nói ground-truth engine chạy CLI
(`:646-648`). ADR-002 đã quyết định đúng rằng analyzer netstandard là light
layer và heavy engine chạy CLI (`plans/adr/002-core-dependency-scope.md:37-43`),
nhưng `docs/03-components/tooling/analyzers.md:127-156` và showcase
(`docs/01-overview/feature-showcase.md:195-209`) vẫn nói analyzer CI có DB và
validate tất cả rule. Khuyến nghị ghi rõ analyzer chỉ syntactic/limited
invocation checks; Core/CLI mới là nơi chạy `DG101`, `DG002`–`DG016` với
ground truth.

### TL-04 — P2, confidence cao: `DG002` bị dùng cho hai semantics khác nhau

Core dùng `DG101` cho parameter count (`ContractRules.cs:52-60`) và `DG002`
cho parameter type (`:100-108`), trong khi analyzer inline ghi stored-proc
format/prefix violation là `DG002` (`Analyzers.cs:675-686`). Điều này làm
SARIF/IDE nhận một ID “parameter type” nhưng message lại là EXEC prefix. Hơn
nữa, `isStoredProc` chỉ được đặt true khi text đã bắt đầu `exec`/`execute`
(`Analyzers.cs:603-604,625-627`), nên điều kiện kiểm tra prefix bên trong luôn
false (`:676-681`). Khuyến nghị đổi rule prefix thành một ID riêng hoặc bỏ
khỏi analyzer; nếu giữ thì sửa control-flow và cập nhật descriptor/test.

### TL-05 — P3, confidence cao về source chưa có caller: helper attribute tạo metadata không đủ

`CreateExpectedSpParameterAttribute` tạo `dbType` và `direction` là chuỗi rỗng
(`CodeFixProviders.cs:98-110`), dù constructor contract yêu cầu ba tham số và
direction rỗng sẽ âm thầm thành `Input` (`ContractAttributes.cs:72-94`). Hai
helper thêm attributes không được gọi (`:254-294`), còn action `DG002` chỉ ghi
comment. Khuyến nghị không sinh attribute cho tới khi suy luận được type/
direction; nếu sinh placeholder phải yêu cầu người dùng nhập và thêm test
syntax/semantic output.

### TL-06 — P2, confidence cao: test coverage kiểm tra metadata thay vì hành vi

`GeneratorExecutionTests` chỉ xác minh một case `ExecuteSqlRaw` phát ra
`DG001` và một non-SQL không phát ra (`tests/DataGuard.Analyzers.Tests/GeneratorExecutionTests.cs:19-91`).
`DescriptorArityTests` chỉ kiểm tra placeholder/ID (`:20-56`). Code-fix tests
chỉ kiểm tra ID và có FixAll (`tests/DataGuard.CodeFixes.Tests/CodeFixProviderTests.cs:20-163`),
không kiểm tra document sau action, duplicate attributes, null syntax root,
marker suppression, hoặc các ID bị rơi. Khuyến nghị bổ sung end-to-end Roslyn
tests cho mỗi action và một ma trận descriptor/emission/fixability.

## Rủi ro và khuyến nghị chưa giải quyết

- `DataGuardCodeFixProvider.RegisterCodeFixesAsync` dùng `root!` sau khi lấy
  syntax root nhưng không guard null (`CodeFixProviders.cs:56-70`); các provider
  phụ có guard. Nên thêm guard đồng nhất.
- `FixNamingConventionAsync` gọi `ToPascalCase` trên identifier property
  (`:318-348`), nên property đã PascalCase là no-op; việc xác định tên DB phải
  đến từ diagnostic/contract, không phải chỉ tên C# hiện tại.
- `NameConventions.ToSnakeCase` cố ý chèn `_` trước mọi uppercase sau ký tự
  đầu (`src/DataGuard.Contracts/NameConventions.cs:20-39`), tạo `i_d`/`x_m_l`
  cho acronym; docs contract xác nhận hành vi này (`contract-attributes.md:229-240`)
  nhưng cần quyết định đó có thật sự là convention sản phẩm hay không.
- Packaging comments/cấu hình bundle Contracts là nhất quán với ADR-002;
  lock files được đọc nhưng chưa được restore/verify trong phiên này.

## Verification còn thiếu

Worker này không chạy test/build theo phân công. [Verification](verification.md) ghi kết quả mới: toàn solution 484 pass, trong đó Analyzers 5 và CodeFixes 12. Các test action Roslyn bổ sung vẫn là đề xuất, chưa được triển khai/chạy trong audit. Không có thay đổi source, commit, push hay publish.
