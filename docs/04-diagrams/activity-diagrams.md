# Activity Diagrams

## 1. Validate Command Activity

```mermaid
flowchart TD
    START(["🚀 Start: dataguard validate"]) --> LOADCFG["Load .dataguard.yml config"]
    LOADCFG --> DETECT{"Auto-detect<br/>provider?"}
    DETECT -->|Yes| SCAN["AutoDetectionEngine.ScanProject()"]
    DETECT -->|No| USECFG["Use --provider flag"]
    SCAN --> DETECTED["Detected: SqlServer/Oracle/MySql/Pg"]
    USECFG & DETECTED --> GTMODE{"Ground Truth Mode?"}
    
    GTMODE -->|Full| CONN["Connect to database<br/>(ZeroTrustCredentialProvider)"]
    GTMODE -->|Snapshot| SNAP["Load snapshot.json"]
    GTMODE -->|Manual| ASM["Load --assembly path"]
    
    CONN --> EXTRACT["Extract contracts via adapter"]
    SNAP --> BUILD["Build descriptors from snapshot"]
    ASM --> REFLECT["Reflect attributes from assembly"]
    
    EXTRACT & BUILD & REFLECT --> RULES["Resolve rules for provider<br/>(BuiltInRuleDependencies)"]
    RULES --> ORDER["Topological sort rules<br/>(RuleDependencyGraph)"]
    ORDER --> VALIDATE["ConcurrentValidationEngine.ValidateAsync()"]
    
    VALIDATE --> PARALLEL["Parallel.ForEachAsync<br/>(MaxDegreeOfParallelism)"]
    PARALLEL --> FORALL["For each (rule, contract)"]
    FORALL --> RULECHECK["rule.ValidateAsync(contract, allContracts)"]
    RULECHECK --> VIOLATIONS{"Violations?"}
    VIOLATIONS -->|Yes| ADD["Add to ConcurrentBag<br/>(check backpressure)"]
    VIOLATIONS -->|No| NEXT["Next pair"]
    ADD --> NEXT
    NEXT --> FORALL
    
    PARALLEL --> SORT["Sort by RuleId + Message"]
    SORT --> EMIT["DiagnosticEmitter.EmitAsync()"]
    EMIT --> FORMAT{"--format?"}
    FORMAT -->|text| CONSOLE["Console output"]
    FORMAT -->|sarif| SARIF["Write SARIF file"]
    FORMAT -->|evidence| EVIDENCE["Write evidence artifact"]
    
    CONSOLE & SARIF & EVIDENCE --> EXIT{"Has errors?"}
    EXIT -->|Yes| EXIT1(["Exit code 1"])
    EXIT -->|No| EXIT0(["Exit code 0"])
```

## 2. Baseline Command Activity

```mermaid
flowchart TD
    START(["🚀 Start: dataguard baseline"]) --> LOADCFG["Load config"]
    LOADCFG --> CONN["Connect to database"]
    CONN --> VALIDATE["Run full validation"]
    VALIDATE --> COMPUTE["Compute schema hash"]
    COMPUTE --> GETVER["Get database version"]
    GETVER --> BUILD["Build BaselineFile v2"]
    BUILD --> WRITE["Write .dataguard-baseline.json"]
    WRITE --> DONE(["✅ Baseline created"])
```

## 3. Snapshot Command Activity

```mermaid
flowchart TD
    START(["🚀 Start: dataguard snapshot"]) --> SUBCMD{"Sub-command?"}
    
    SUBCMD -->|refresh| REF["Connect to database"]
    REF --> EXTRACT["Extract all tables + columns"]
    EXTRACT --> BUILD["Build SnapshotTable[]"]
    BUILD --> WRITE["Write .dataguard-snapshot.json"]
    WRITE --> DONE1(["✅ Snapshot refreshed"])
    
    SUBCMD -->|show| LOAD["Load snapshot.json"]
    LOAD --> DISPLAY["Display info:<br/>table count, column count,<br/>last modified"]
    DISPLAY --> DONE2(["✅ Info displayed"])
    
    SUBCMD -->|diff| LOADSNAP["Load snapshot.json"]
    LOADSNAP --> CONN2["Connect to database"]
    CONN2 --> EXTRACT2["Extract current schema"]
    EXTRACT2 --> COMPARE["Compare: snapshot vs live"]
    COMPARE --> DIFF{"Differences?"}
    DIFF -->|Yes| REPORT["Report drift<br/>(exit 1 if --fail-on-drift)"]
    DIFF -->|No| NODRIFT["No drift detected"]
    REPORT --> DONE3(["Exit code 0 or 1"])
    NODRIFT --> DONE4(["✅ No drift"])
```

## 4. Assess Command Activity

```mermaid
flowchart TD
    START(["🚀 Start: dataguard assess"]) --> LOADCFG["Load config"]
    LOADCFG --> DISCOVER["InventoryPack.DiscoverProjects()"]
    DISCOVER --> PROJS{"Projects found?"}
    PROJS -->|No| ERR1(["Error: DG1005<br/>No projects found"])
    PROJS -->|Yes| INVENTORY["InventoryPack.Assess()<br/>(TFM support status)"]
    
    INVENTORY --> FOREACH["For each project"]
    FOREACH --> READ["ProjectInventoryReader.Read()"]
    READ --> READOK{"Read OK?"}
    READOK -->|No| SKIP["Skip (continue siblings)"]
    READOK -->|Yes| DEP["DependencyHealthPack.Assess()"]
    SKIP & DEP --> FOREACH
    
    FOREACH --> BCI["BuildCiPack.Assess()<br/>(SDK pinning, CI matrix)"]
    BCI --> SECRETS["SecretsPack.AssessFile()<br/>(scan .config, .yml)"]
    SECRETS --> MACHINE["SecretsPack.AssessMachinePaths()"]
    
    MACHINE --> AGGREGATE["Aggregate findings + errors"]
    AGGREGATE --> REPORT["Build AssessmentReport"]
    REPORT --> OUTPUT{"--format?"}
    OUTPUT -->|json| JSON["Write JSON"]
    OUTPUT -->|sarif| SARIF["Write SARIF"]
    OUTPUT -->|text| TEXT["Console text"]
    
    JSON & SARIF & TEXT --> EXIT{"Findings or errors?"}
    EXIT -->|Yes| EXIT1(["Exit code 1"])
    EXIT -->|No| EXIT0(["Exit code 0"])
```

## 5. Oracle Check Activity

```mermaid
flowchart TD
    START(["🚀 Start: dataguard oracle-check"]) --> LOADCFG["Load config"]
    LOADCFG --> CONN["Connect to Oracle"]
    CONN --> NLS["Read NLS parameters<br/>(NlsSessionReader)"]
    NLS --> ARGS["Read ALL_ARGUMENTS<br/>(AllArgumentsReader)"]
    ARGS --> TABS["Read ALL_TAB_COLUMNS<br/>(AllTabColumnsReader)"]
    TABS --> BUILD["Build descriptors"]
    
    BUILD --> RULES["Run Oracle-specific rules:<br/>DG007 (Length exceeds column)<br/>DG008 (Byte-length overflow)<br/>DG009 (Inferred size fallback)<br/>DG010-DG014 (Dialect checks)"]
    
    RULES --> LEN["LengthMismatchDetector<br/>(EfCoreInferenceSimulator)"]
    LEN --> DIALECT["OracleDialectChecker"]
    DIALECT --> MERGE["Merge all violations"]
    MERGE --> EMIT["Emit results"]
    EMIT --> DONE(["Exit code 0 or 1"])
```

## 6. Init Command Activity

```mermaid
flowchart TD
    START(["🚀 Start: dataguard init"]) --> DETECT{"--provider?"}
    DETECT -->|oracle| ORACFG["Generate Oracle config"]
    DETECT -->|sqlserver| SSCFG["Generate SQL Server config"]
    DETECT -->|auto| AUTO["AutoDetectionEngine"]
    
    AUTO --> SCAN["Scan .csproj files"]
    SCAN --> FIND["Find EF Core / Dapper refs"]
    FIND --> GEN["Generate smart defaults"]
    
    ORACFG & SSCFG & GEN --> YAML["Serialize to YAML"]
    YAML --> WRITE["Write .dataguard.yml"]
    WRITE --> SNAPSHOT["Create empty snapshot.json"]
    SNAPSHOT --> DONE(["✅ Config initialized"])
```

## 7. IDE Analysis Activity (Roslyn Analyzer)

```mermaid
flowchart TD
    START(["🎹 User types in IDE"]) --> ROSLYN["Roslyn incremental generator"]
    ROSLYN --> SCAN["Scan syntax tree<br/>(EF Core DbContext, Dapper calls)"]
    SCAN --> FIND{"SQL calls found?"}
    FIND -->|No| IDLE(["No diagnostics"])
    FIND -->|Yes| MATCH["Match against known patterns"]
    MATCH --> DIAG{"Contract violation?"}
    DIAG -->|Yes| MARK["Mark DG001 diagnostic<br/>(squiggly underline)"]
    DIAG -->|No| OK(["Clean"])
    MARK --> QUICKFIX["Offer quick fixes:<br/>- AddMaxLength<br/>- SkipContractCheck<br/>- FixNaming<br/>- UseOracleProvider"]
```
