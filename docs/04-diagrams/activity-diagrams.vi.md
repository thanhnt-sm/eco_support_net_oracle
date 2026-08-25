# Sơ Đồ Hoạt Động

## 1. Hoạt Động Lệnh Validate

```mermaid
flowchart TD
    START(["🚀 Bắt đầu: dataguard validate"]) --> LOADCFG["Tải cấu hình .dataguard.yml"]
    LOADCFG --> DETECT{"Tự động phát hiện<br/>provider?"}
    DETECT -->|Có| SCAN["AutoDetectionEngine.ScanProject()"]
    DETECT -->|Không| USECFG["Dùng cờ --provider"]
    SCAN --> DETECTED["Phát hiện: SqlServer/Oracle/MySql/Pg"]
    USECFG & DETECTED --> GTMODE{"Chế độ Ground Truth?"}
    
    GTMODE -->|Full| CONN["Kết nối database<br/>(ZeroTrustCredentialProvider)"]
    GTMODE -->|Snapshot| SNAP["Tải snapshot.json"]
    GTMODE -->|Manual| ASM["Tải đường dẫn --assembly"]
    
    CONN --> EXTRACT["Trích xuất contracts qua adapter"]
    SNAP --> BUILD["Tạo descriptors từ snapshot"]
    ASM --> REFLECT["Reflect attributes từ assembly"]
    
    EXTRACT & BUILD & REFLECT --> RULES["Resolve rules cho provider<br/>(BuiltInRuleDependencies)"]
    RULES --> ORDER["Sắp xếp topo rules<br/>(RuleDependencyGraph)"]
    ORDER --> VALIDATE["ConcurrentValidationEngine.ValidateAsync()"]
    
    VALIDATE --> PARALLEL["Parallel.ForEachAsync<br/>(MaxDegreeOfParallelism)"]
    PARALLEL --> FORALL["Với mỗi cặp (rule, contract)"]
    FORALL --> RULECHECK["rule.ValidateAsync(contract, allContracts)"]
    RULECHECK --> VIOLATIONS{"Có violations?"}
    VIOLATIONS -->|Có| ADD["Thêm vào ConcurrentBag<br/>(kiểm tra backpressure)"]
    VIOLATIONS -->|Không| NEXT["Cặp tiếp theo"]
    ADD --> NEXT
    NEXT --> FORALL
    
    PARALLEL --> SORT["Sắp xếp theo RuleId + Message"]
    SORT --> EMIT["DiagnosticEmitter.EmitAsync()"]
    EMIT --> FORMAT{"--format?"}
    FORMAT -->|text| CONSOLE["Console output"]
    FORMAT -->|sarif| SARIF["Ghi file SARIF"]
    FORMAT -->|evidence| EVIDENCE["Ghi evidence artifact"]
    
    CONSOLE & SARIF & EVIDENCE --> EXIT{"Có lỗi?"}
    EXIT -->|Có| EXIT1(["Exit code 1"])
    EXIT -->|Không| EXIT0(["Exit code 0"])
```

## 2. Hoạt Động Lệnh Baseline

```mermaid
flowchart TD
    START(["🚀 Bắt đầu: dataguard baseline"]) --> LOADCFG["Tải cấu hình"]
    LOADCFG --> CONN["Kết nối database"]
    CONN --> VALIDATE["Chạy validation đầy đủ"]
    VALIDATE --> COMPUTE["Tính schema hash"]
    COMPUTE --> GETVER["Lấy phiên bản database"]
    GETVER --> BUILD["Tạo BaselineFile v2"]
    BUILD --> WRITE["Ghi .dataguard-baseline.json"]
    WRITE --> DONE(["✅ Baseline đã tạo"])
```

## 3. Hoạt Động Lệnh Snapshot

```mermaid
flowchart TD
    START(["🚀 Bắt đầu: dataguard snapshot"]) --> SUBCMD{"Lệnh con?"}
    
    SUBCMD -->|refresh| REF["Kết nối database"]
    REF --> EXTRACT["Trích xuất tất cả bảng + cột"]
    EXTRACT --> BUILD["Tạo SnapshotTable[]"]
    BUILD --> WRITE["Ghi .dataguard-snapshot.json"]
    WRITE --> DONE1(["✅ Snapshot đã làm mới"])
    
    SUBCMD -->|show| LOAD["Tải snapshot.json"]
    LOAD --> DISPLAY["Hiển thị thông tin:<br/>số bảng, số cột,<br/>lần sửa cuối"]
    DISPLAY --> DONE2(["✅ Đã hiển thị"])
    
    SUBCMD -->|diff| LOADSNAP["Tải snapshot.json"]
    LOADSNAP --> CONN2["Kết nối database"]
    CONN2 --> EXTRACT2["Trích xuất schema hiện tại"]
    EXTRACT2 --> COMPARE["So sánh: snapshot vs live"]
    COMPARE --> DIFF{"Có khác biệt?"}
    DIFF -->|Có| REPORT["Báo cáo drift<br/>(exit 1 nếu --fail-on-drift)"]
    DIFF -->|Không| NODRIFT["Không phát hiện drift"]
    REPORT --> DONE3(["Exit code 0 hoặc 1"])
    NODRIFT --> DONE4(["✅ Không drift"])
```

## 4. Hoạt Động Lệnh Assess

```mermaid
flowchart TD
    START(["🚀 Bắt đầu: dataguard assess"]) --> LOADCFG["Tải cấu hình"]
    LOADCFG --> DISCOVER["InventoryPack.DiscoverProjects()"]
    DISCOVER --> PROJS{"Tìm thấy projects?"}
    PROJS -->|Không| ERR1(["Lỗi: DG1005<br/>Không tìm thấy projects"])
    PROJS -->|Có| INVENTORY["InventoryPack.Assess()<br/>(Trạng thái hỗ trợ TFM)"]
    
    INVENTORY --> FOREACH["Với mỗi project"]
    FOREACH --> READ["ProjectInventoryReader.Read()"]
    READ --> READOK{"Đọc OK?"}
    READOK -->|Không| SKIP["Bỏ qua (tiếp tục siblings)"]
    READOK -->|Có| DEP["DependencyHealthPack.Assess()"]
    SKIP & DEP --> FOREACH
    
    FOREACH --> BCI["BuildCiPack.Assess()<br/>(SDK pinning, CI matrix)"]
    BCI --> SECRETS["SecretsPack.AssessFile()<br/>(quét .config, .yml)"]
    SECRETS --> MACHINE["SecretsPack.AssessMachinePaths()"]
    
    MACHINE --> AGGREGATE["Tổng hợp findings + errors"]
    AGGREGATE --> REPORT["Tạo AssessmentReport"]
    REPORT --> OUTPUT{"--format?"}
    OUTPUT -->|json| JSON["Ghi JSON"]
    OUTPUT -->|sarif| SARIF["Ghi SARIF"]
    OUTPUT -->|text| TEXT["Console text"]
    
    JSON & SARIF & TEXT --> EXIT{"Có findings hoặc errors?"}
    EXIT -->|Có| EXIT1(["Exit code 1"])
    EXIT -->|Không| EXIT0(["Exit code 0"])
```

## 5. Hoạt Động Oracle Check

```mermaid
flowchart TD
    START(["🚀 Bắt đầu: dataguard oracle-check"]) --> LOADCFG["Tải cấu hình"]
    LOADCFG --> CONN["Kết nối Oracle"]
    CONN --> NLS["Đọc tham số NLS<br/>(NlsSessionReader)"]
    NLS --> ARGS["Đọc ALL_ARGUMENTS<br/>(AllArgumentsReader)"]
    ARGS --> TABS["Đọc ALL_TAB_COLUMNS<br/>(AllTabColumnsReader)"]
    TABS --> BUILD["Tạo descriptors"]
    
    BUILD --> RULES["Chạy rules Oracle:<br/>DG007 (Length vượt cột)<br/>DG008 (Tràn byte-length)<br/>DG009 (Fallback size suy luận)<br/>DG010-DG014 (Kiểm tra dialect)"]
    
    RULES --> LEN["LengthMismatchDetector<br/>(EfCoreInferenceSimulator)"]
    LEN --> DIALECT["OracleDialectChecker"]
    DIALECT --> MERGE["Gộp tất cả violations"]
    MERGE --> EMIT["Phát kết quả"]
    EMIT --> DONE(["Exit code 0 hoặc 1"])
```

## 6. Hoạt Động Lệnh Init

```mermaid
flowchart TD
    START(["🚀 Bắt đầu: dataguard init"]) --> DETECT{"--provider?"}
    DETECT -->|oracle| ORACFG["Tạo cấu hình Oracle"]
    DETECT -->|sqlserver| SSCFG["Tạo cấu hình SQL Server"]
    DETECT -->|auto| AUTO["AutoDetectionEngine"]
    
    AUTO --> SCAN["Quét file .csproj"]
    SCAN --> FIND["Tìm EF Core / Dapper refs"]
    FIND --> GEN["Tạo smart defaults"]
    
    ORACFG & SSCFG & GEN --> YAML["Serialize sang YAML"]
    YAML --> WRITE["Ghi .dataguard.yml"]
    WRITE --> SNAPSHOT["Tạo snapshot.json rỗng"]
    SNAPSHOT --> DONE(["✅ Đã khởi tạo cấu hình"])
```

## 7. Hoạt Động IDE Analysis (Roslyn Analyzer)

```mermaid
flowchart TD
    START(["🎹 Người dùng gõ trong IDE"]) --> ROSLYN["Roslyn incremental generator"]
    ROSLYN --> SCAN["Quét syntax tree<br/>(EF Core DbContext, Dapper calls)"]
    SCAN --> FIND{"Tìm thấy SQL calls?"}
    FIND -->|Không| IDLE(["Không có diagnostics"])
    FIND -->|Có| MATCH["Khớp với patterns đã biết"]
    MATCH --> DIAG{"Vi phạm contract?"}
    DIAG -->|Có| MARK["Đánh dấu DG001 diagnostic<br/>(gạch chân squiggly)"]
    DIAG -->|Không| OK(["Sạch"])
    MARK --> QUICKFIX["Đề xuất quick fixes:<br/>- AddMaxLength<br/>- SkipContractCheck<br/>- FixNaming<br/>- UseOracleProvider"]
```
