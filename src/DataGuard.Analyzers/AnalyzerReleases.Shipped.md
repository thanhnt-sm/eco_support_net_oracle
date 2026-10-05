; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.2.2

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DG001 | DataGuard.IDE | Warning | UnvalidatedSqlCallGenerator (IDE generator)
DG002 | DataGuard.Contracts | Error | Advertised only; produced by the CLI rules engine
DG003 | DataGuard.Contracts | Error | Advertised only; produced by the CLI rules engine
DG004 | DataGuard.Contracts | Error | ContractValidationAnalyzer (literal SELECT list vs. mapped type)
DG005 | DataGuard.Contracts | Warning | Advertised only; produced by the CLI rules engine
DG006 | DataGuard.Contracts | Warning | Advertised only; produced by the CLI rules engine
DG007 | DataGuard.Length | Error | Advertised only; produced by the CLI rules engine
DG008 | DataGuard.Length | Warning | Advertised only; produced by the CLI rules engine
DG009 | DataGuard.Length | Warning | Advertised only; produced by the CLI rules engine
DG010 | DataGuard.Dialect | Warning | Advertised only; produced by the CLI rules engine
DG011 | DataGuard.Dialect | Warning | Advertised only; produced by the CLI rules engine
DG012 | DataGuard.Dialect | Error | Advertised only; produced by the CLI rules engine
DG013 | DataGuard.Dialect | Warning | Advertised only; produced by the CLI rules engine
DG014 | DataGuard.Dialect | Warning | Advertised only; produced by the CLI rules engine
DG015 | DataGuard.Contracts | Error | Advertised only; produced by the CLI rules engine
DG016 | DataGuard.Contracts | Error | Advertised only; produced by the CLI rules engine
DG017 | DataGuard.Performance | Warning | ContractValidationAnalyzer (SELECT *)
DG098 | DataGuard.Contracts | Warning | ContractValidationAnalyzer (SELECT without FROM)
DG099 | DataGuard.Security | Warning | ContractValidationAnalyzer (injection pattern)
