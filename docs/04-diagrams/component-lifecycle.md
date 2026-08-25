# Component Active Lifetime Diagrams

## 1. DataGuard CLI Process Lifetime

```mermaid
sequenceDiagram
    participant OS as Operating System
    participant CLI as DataGuard.Cli Process
    participant Config as Configuration
    participant ZTCP as CredentialProvider
    participant Adapter as DB Adapter
    participant Sources as Contract Sources
    participant Engine as Validation Engine
    participant Rules as Rules
    participant Emitter as DiagnosticEmitter
    participant Telemetry as TelemetryCollector

    OS->>CLI: Process starts
    activate CLI
    
    CLI->>Config: Parse args + load .dataguard.yml
    activate Config
    Config-->>CLI: DataGuardConfiguration
    Note over Config: Active until process exit
    
    CLI->>ZTCP: Initialize
    activate ZTCP
    ZTCP->>ZTCP: Resolve credential sources
    Note over ZTCP: Active during validation only
    
    CLI->>Adapter: Initialize with credentials
    activate Adapter
    Adapter->>Adapter: Open connection pool
    Note over Adapter: Active during extraction only
    
    CLI->>Sources: Initialize (EfModelSource, etc.)
    activate Sources
    Note over Sources: Active during extraction only
    
    CLI->>Engine: Initialize (ConcurrentValidationEngine)
    activate Engine
    Engine->>Engine: Configure parallelism
    Note over Engine: Active during validation only
    
    CLI->>Rules: Load rules (built-in + plugins)
    activate Rules
    Note over Rules: Active during validation only
    
    CLI->>Emitter: Initialize sinks
    activate Emitter
    Note over Emitter: Active during output only
    
    opt Telemetry enabled
        CLI->>Telemetry: Initialize
        activate Telemetry
        Note over Telemetry: Active until process exit
    end
    
    Note over CLI: === Extraction Phase ===
    CLI->>Sources: ExtractContractsAsync()
    Sources->>Adapter: Query database
    Adapter-->>Sources: Raw metadata
    Sources-->>CLI: ContractDescriptor[]
    
    Note over CLI: === Validation Phase ===
    CLI->>Engine: ValidateAsync(contracts, rules)
    Engine->>Rules: For each (rule, contract)
    Rules-->>Engine: ContractViolation[]
    Engine-->>CLI: Sorted violations
    
    Note over CLI: === Output Phase ===
    CLI->>Emitter: EmitAsync(violations)
    Emitter-->>CLI: Output written
    
    Note over CLI: === Cleanup Phase ===
    CLI->>Emitter: Dispose
    deactivate Emitter
    CLI->>Rules: Unload plugins
    deactivate Rules
    CLI->>Engine: Dispose
    deactivate Engine
    deactivate Sources
    CLI->>Adapter: Close connections
    deactivate Adapter
    CLI->>ZTCP: Clear credentials
    deactivate ZTCP
    opt Telemetry
        CLI->>Telemetry: Flush + Dispose
        deactivate Telemetry
    end
    deactivate Config
    
    OS->>CLI: Process exits
    deactivate CLI
```

## 2. Roslyn Analyzer Lifetime (IDE Session)

```mermaid
sequenceDiagram
    participant IDE as Visual Studio / VS Code
    participant Roslyn as Roslyn Host
    participant Gen as IncrementalGenerator
    participant Cache as Syntax Cache
    participant Diag as Diagnostic Analyzer

    IDE->>Roslyn: Open solution
    activate Roslyn
    Roslyn->>Gen: Initialize
    activate Gen
    Gen->>Gen: Register syntax receiver
    
    loop Every keystroke / file change
        IDE->>Roslyn: Document changed
        Roslyn->>Gen: Execute (incremental)
        Gen->>Cache: Check syntax tree delta
        Cache-->>Gen: Changed nodes only
        Gen->>Gen: Scan for SQL calls
        Gen-->>Roslyn: Diagnostic results
        Roslyn->>IDE: Update squiggly underlines
    end
    
    IDE->>Roslyn: Close solution
    Roslyn->>Gen: Dispose
    deactivate Gen
    deactivate Roslyn
```

## 3. VS Code Extension Lifetime

```mermaid
sequenceDiagram
    participant VSCode as VS Code
    participant Ext as DataGuard Extension
    participant SB as StatusBarItem
    participant OC as OutputChannel
    participant Diag as DiagnosticCollection
    participant CLI as dataguard CLI Process

    VSCode->>Ext: activate(context)
    activate Ext
    Ext->>SB: Create status bar item
    activate SB
    Ext->>OC: Create output channel
    activate OC
    Ext->>Diag: Create diagnostic collection
    activate Diag
    Ext->>Ext: Register commands
    
    Note over Ext: Extension active, waiting for user
    
    VSCode->>Ext: dataguard.runValidation command
    Ext->>SB: setStatus("running")
    Ext->>CLI: spawn("dataguard", ["validate", "--format", "sarif"])
    activate CLI
    CLI-->>Ext: stdout/stderr stream
    Ext->>OC: Append output lines
    
    CLI-->>Ext: Process exit
    deactivate CLI
    Ext->>Ext: Load SARIF output
    Ext->>Diag: Set diagnostics from SARIF
    Ext->>SB: setStatus("idle" / "warning" / "error")
    
    VSCode->>Ext: deactivate()
    Ext->>SB: Dispose
    deactivate SB
    Ext->>OC: Dispose
    deactivate OC
    Ext->>Diag: Dispose
    deactivate Diag
    deactivate Ext
```

## 4. Visual Studio Extension Lifetime

```mermaid
sequenceDiagram
    participant VS as Visual Studio 2022
    participant Pkg as DataGuardPackage
    participant Menu as Menu Commands
    participant EL as ErrorListProvider
    participant OP as Output Pane
    participant CLI as dataguard CLI Process

    VS->>Pkg: InitializeAsync()
    activate Pkg
    Pkg->>Menu: Register Validate + Cancel commands
    Pkg->>EL: Create ErrorListProvider
    activate EL
    Pkg->>OP: Create output pane
    activate OP
    
    Note over Pkg: Package loaded, waiting for commands
    
    VS->>Menu: Validate command clicked
    Menu->>Pkg: RunValidationAsync()
    Pkg->>Pkg: processGate lock
    Pkg->>CLI: Process.Start("dataguard", "validate --format sarif")
    activate CLI
    CLI-->>Pkg: stdout/stderr
    Pkg->>OP: Write output lines
    
    alt User cancels
        VS->>Menu: Cancel command clicked
        Menu->>Pkg: CancelValidationAsync()
        Pkg->>CLI: Kill process tree
        deactivate CLI
    else Process completes
        CLI-->>Pkg: Exit code
        deactivate CLI
        Pkg->>Pkg: Load SARIF
        Pkg->>EL: Publish SARIF diagnostics
    end
    
    Pkg->>Pkg: processGate unlock
    
    VS->>Pkg: Dispose
    Pkg->>EL: Dispose
    deactivate EL
    Pkg->>OP: Dispose
    deactivate OP
    deactivate Pkg
```

## 5. MEF Plugin Assembly Lifetime

```mermaid
sequenceDiagram
    participant PM as RulePluginManager
    participant ALC as AssemblyLoadContext
    participant ASM as Plugin Assembly
    participant MEF as MEF Container
    participant Rule as Rule Instance

    PM->>ALC: new PluginLoadContext(path)
    activate ALC
    ALC->>ASM: LoadFromAssemblyPath()
    activate ASM
    
    PM->>MEF: new ContainerConfiguration()
    MEF->>MEF: WithAssembly(asm)
    MEF-->>PM: CompositionContainer
    
    PM->>MEF: GetExports<Lazy<IContractRule, IRuleMetadata>>()
    MEF-->>PM: Plugin catalog
    
    Note over PM: Plugins ready for validation
    
    PM->>Rule: lazy.Value (create instance)
    activate Rule
    Rule->>Rule: ValidateCoreAsync()
    Rule-->>PM: ContractViolation[]
    deactivate Rule
    
    Note over PM: Validation complete
    
    PM->>MEF: Dispose container
    PM->>ALC: Unload()
    deactivate ASM
    deactivate ALC
```

## 6. Audit Log Hash Chain Lifetime

```mermaid
sequenceDiagram
    participant App as DataGuard Application
    participant Logger as FileAuditLogger
    participant Chain as Hash Chain
    participant FS as audit-log.jsonl

    App->>Logger: Initialize
    Logger->>Chain: Initialize with empty previous hash
    
    App->>Logger: LogDatabaseOperationAsync()
    Logger->>Logger: Create AuditEntry
    Logger->>Chain: Compute SHA256(entry + previousHash)
    Chain-->>Logger: New hash
    Logger->>FS: Append JSON line
    Note over Chain: Each entry links to previous via hash
    
    App->>Logger: LogCredentialAccessAsync()
    Logger->>Chain: Compute SHA256(entry + previousHash)
    Chain-->>Logger: New hash
    Logger->>FS: Append JSON line
    
    App->>Logger: LogConfigurationChangeAsync()
    Logger->>Chain: Compute SHA256(entry + previousHash)
    Chain-->>Logger: New hash
    Logger->>FS: Append JSON line
    
    Note over Chain: Chain integrity verifiable at any point
    
    App->>Logger: VerifyChainIntegrityAsync()
    Logger->>FS: Read all entries
    Logger->>Chain: Recompute hashes
    Chain-->>Logger: Integrity result
```
