# Máy Trạng Thái & Luồng Trạng Thái

## 1. Máy Trạng Thái Vòng Đời Validation

```mermaid
stateDiagram-v2
    [*] --> Idle: CLI được gọi
    
    Idle --> LoadingConfig: dataguard validate
    LoadingConfig --> ConfigLoaded: .dataguard.yml đã parse
    LoadingConfig --> ConfigError: Cấu hình không hợp lệ/thiếu
    
    ConfigLoaded --> DetectingProvider: Tự động phát hiện bật
    ConfigLoaded --> ResolvingCredentials: Provider đã chỉ định
    
    DetectingProvider --> ResolvingCredentials: Đã phát hiện provider
    DetectingProvider --> UnknownProvider: Không tìm EF Core/Dapper
    
    ResolvingCredentials --> ExtractingContracts: Credentials đã resolve
    ResolvingCredentials --> CredentialError: Không tìm credentials
    
    ExtractingContracts --> Validating: Contracts đã trích xuất
    ExtractingContracts --> ExtractionError: Kết nối DB thất bại
    
    Validating --> EmittingResults: Tất cả rules đã đánh giá
    Validating --> ValidationTimeout: Hết timeout (300s mặc định)
    
    EmittingResults --> Complete: Output đã ghi
    
    Complete --> [*]: Exit code 0 (không lỗi)
    Complete --> [*]: Exit code 1 (có violations)
    
    ConfigError --> [*]: Exit code 2
    UnknownProvider --> [*]: Exit code 2
    CredentialError --> [*]: Exit code 1
    ExtractionError --> [*]: Exit code 1
    ValidationTimeout --> [*]: Exit code 1
```

## 2. Máy Trạng Thái Vòng Đời Baseline

```mermaid
stateDiagram-v2
    [*] --> NoBaseline: Project mới
    
    NoBaseline --> CreatingBaseline: dataguard baseline
    CreatingBaseline --> BaselineActive: .dataguard-baseline.json đã ghi
    
    BaselineActive --> Validating: dataguard validate
    Validating --> FilteringBaseline: Tải baseline
    FilteringBaseline --> ReportingNew: Diff hiện tại vs baseline
    ReportingNew --> BaselineActive: Chỉ hiển thị violations mới
    
    BaselineActive --> Drifting: Phát hiện thay đổi schema
    Drifting --> UpdatingBaseline: dataguard baseline (đóng băng lại)
    UpdatingBaseline --> BaselineActive: Baseline mới đã ghi
    
    BaselineActive --> Migrating: dataguard migrate
    Migrating --> BaselineActive: v1 → v2 đã chuyển đổi
    
    BaselineActive --> Deleting: Người dùng xóa file
    Deleting --> NoBaseline: Quay lại trạng thái mới
```

## 3. Máy Trạng Thái Vòng Đời Snapshot

```mermaid
stateDiagram-v2
    [*] --> NoSnapshot: Không có file snapshot
    
    NoSnapshot --> CreatingSnapshot: dataguard snapshot refresh
    CreatingSnapshot --> SnapshotActive: .dataguard-snapshot.json đã ghi
    
    SnapshotActive --> OfflineValidation: dataguard validate --offline
    OfflineValidation --> SnapshotActive: Validation hoàn tất
    
    SnapshotActive --> DriftCheck: dataguard snapshot diff
    DriftCheck --> NoDrift: Schema khớp
    DriftCheck --> DriftDetected: Schema khác
    
    NoDrift --> SnapshotActive: Tiếp tục
    DriftDetected --> SnapshotActive: --fail-on-drift → exit 1
    DriftDetected --> RefreshingSnapshot: dataguard snapshot refresh
    RefreshingSnapshot --> SnapshotActive: Đã cập nhật
    
    SnapshotActive --> Showing: dataguard snapshot show
    Showing --> SnapshotActive: Hiển thị thông tin
```

## 4. Máy Trạng Thái Báo Cáo Assessment

```mermaid
stateDiagram-v2
    [*] --> Requested: dataguard assess
    
    Requested --> Discovering: InventoryPack.DiscoverProjects()
    Discovering --> NoProjects: Tìm thấy 0 projects
    Discovering --> Assessing: Tìm thấy projects
    
    Assessing --> InventoryPass: Phân tích TFM
    InventoryPass --> DependencyPass: Kiểm tra lock file
    DependencyPass --> BuildCiPass: Kiểm tra SDK/CI
    BuildCiPass --> SecretsPass: Quét secret
    SecretsPass --> Aggregating: Tất cả passes hoàn tất
    
    Aggregating --> ReportReady: AssessmentReport đã tạo
    
    ReportReady --> JsonOutput: --format json
    ReportReady --> SarifOutput: --format sarif
    ReportReady --> TextOutput: --format text (mặc định)
    
    JsonOutput --> [*]: Exit 0 hoặc 1
    SarifOutput --> [*]: Exit 0 hoặc 1
    TextOutput --> [*]: Exit 0 hoặc 1
    NoProjects --> [*]: Exit 1
```

## 5. Máy Trạng Thái Vòng Đời Plugin

```mermaid
stateDiagram-v2
    [*] --> Discovering: RulePluginManager được tạo
    
    Discovering --> Loading: MEF composition
    Loading --> Validating: Assembly đã tải
    Validating --> Ready: Metadata hợp lệ
    Validating --> Rejected: Sai phiên bản / không hợp lệ
    
    Ready --> Executing: Yêu cầu validation
    Executing --> Ready: Violations được trả về
    
    Ready --> Unloading: Dispose
    Unloading --> [*]: AssemblyLoadContext đã unload
    
    Rejected --> [*]: Plugin bị bỏ qua
```

## 6. Máy Trạng Thái Vòng Đời Credential

```mermaid
stateDiagram-v2
    [*] --> Resolving: GetDatabaseConnectionAsync()
    
    Resolving --> FromEnv: CONNECTION_STRING được đặt
    Resolving --> FromVault: KeyVault/SecretsManager đã cấu hình
    Resolving --> FromConfig: AllowPlaintextConfigFallback=true
    Resolving --> Failed: Không có nguồn nào
    
    FromEnv --> HandleCreated: CredentialHandle đã tạo
    FromVault --> HandleCreated
    FromConfig --> HandleCreated
    
    HandleCreated --> Active: Giá trị có thể truy cập
    Active --> Rotating: Cảnh báo 30 ngày
    Rotating --> Active: Credential mới đã lấy
    
    Active --> Disposed: IDisposable.Dispose()
    Disposed --> [*]: Giá trị đã xóa khỏi bộ nhớ
    
    Failed --> [*]: CredentialError
```

## 7. Máy Trạng Thái CI Pipeline

```mermaid
stateDiagram-v2
    [*] --> Triggered: Push/PR đến main/develop
    
    Triggered --> Building: Build job bắt đầu
    Triggered --> SecurityScan: Security job bắt đầu
    Triggered --> SbomGen: SBOM job bắt đầu
    Triggered --> CodeQlRun: CodeQL job bắt đầu
    
    Building --> BuildPassed: Tất cả tests pass, coverage ≥60%
    Building --> BuildFailed: Tests fail hoặc coverage <60%
    
    SecurityScan --> SecurityClean: Không vulns, không secrets
    SecurityScan --> SecurityFailed: Tìm thấy vuln hoặc secret
    
    SbomGen --> SbomReady: SBOM đã tạo
    CodeQlRun --> CodeQlClean: Không có vấn đề
    
    BuildPassed --> DockerSmoke: docker-smoke job
    DockerSmoke --> DockerPassed: Image build, --help hoạt động
    DockerSmoke --> DockerFailed: Build hoặc smoke thất bại
    
    BuildPassed & SecurityClean & SbomReady & CodeQlClean & DockerPassed --> AllGreen: Tất cả jobs pass
    BuildFailed & SecurityFailed & DockerFailed --> Failed: Ít nhất một thất bại
    
    AllGreen --> [*]: ✅ CI xanh
    Failed --> [*]: ❌ CI đỏ
```
