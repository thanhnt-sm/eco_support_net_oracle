# Context
The request is to perform a comprehensive architectural review and upgrade across the entire `DataGuard` source code ecosystem. The system consists of a core rules/assessment engine, multiple database adapters (MySQL, Oracle, PostgreSQL, SQL Server), Roslyn-based analyzers and code fixes, a .NET 9 CLI, and IDE extensions (VSCode and Visual Studio). The overarching goal is to modernize the execution pipelines for better throughput, harden credential handling across the CLI and IDE extensions, and standardize the adapter contracts to reduce duplicated logic.

# Approach

1. **Concurrent Pipeline Modernization**
   - **Target**: `DataGuard.Core/Assessment/AssessmentEngine.cs` and `RulePluginManager.cs`
   - **Change**: Refactor the rule evaluation loop to return and consume `IAsyncEnumerable<Diagnostic>` instead of buffering results in memory. Update `ConcurrentValidationEngine.cs` to use `System.Threading.Channels` for producer-consumer workloads, allowing database adapters to yield schema objects asynchronously while the assessment engine validates them in parallel.
   - **Handling**: Ensure cancellation tokens are aggressively passed down to all dialect checkers to handle rapid aborts from the CLI or IDE.

2. **Security & Credential Hardening**
   - **Target**: `DataGuard.Core/Security/ZeroTrustCredentialProvider.cs` and `CredentialManager.cs`
   - **Change**: Replace plain `string` based credential caching with `System.Security.SecureString` or `ProtectedMemory` for the lifetime of the CLI process. 
   - **Target**: `DataGuard.VSCode/src/security.ts`
   - **Change**: Migrate credential handling from legacy configuration files or environment variables to the native VSCode `SecretStorage` API (`context.secrets.store`). Ensure `security.test.ts` is updated to mock this API.

3. **Adapter Contract Standardization**
   - **Target**: `DataGuard.Contracts/Abstractions` (new interface `IDialectAnalyzer`)
   - **Change**: Extract the common dialect validation logic found across `MySqlDialectChecker`, `OracleDialectChecker`, and `PostgreSqlDialectChecker`. Introduce a single unified `IDialectAnalyzer` contract. Modify all adapter projects to implement this contract rather than relying on ad-hoc parser signatures.
   - **Deletions**: Remove redundant base parsing logic in individual adapter implementations.

4. **CLI Modernization**
   - **Target**: `DataGuard.Cli/Program.cs`
   - **Change**: Update `System.CommandLine` invocations to use the latest async handler patterns and ensure the CLI project is prepared for AOT compilation (trimming warnings addressed in the CLI root).

# Critical files & anchors
- `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs`: *Anchor for the new `System.Threading.Channels` implementation.*
- `src/DataGuard.VSCode/src/security.ts`: *Needs migration to `SecretStorage`.*
- `src/DataGuard.Contracts/ContractAttributes.cs`: *Directory where the new `IDialectAnalyzer` will reside.*
- `src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs`: *Template for the dialect refactoring that must be applied to all DB adapters.*

# Verification
1. **Concurrent Pipeline**: Execute the `DataGuard.Cli` against a mocked or sample database schema using the maximum rule set. Verify via telemetry or logs that validation operations are processed concurrently without deadlocks.
2. **Security Hardening**: Run `npm test` in `src/DataGuard.VSCode` to confirm `security.test.ts` passes with the new `SecretStorage` mock.
3. **Adapter Standardization**: Compile the entire solution (`dotnet build src/DataGuard.Cli/DataGuard.Cli.csproj`) to ensure all adapters correctly implement the new `IDialectAnalyzer` contract and no legacy dialect checker references remain.

# Assumptions & contingencies
- **Assumption**: The target VSCode engine version in `package.json` supports `SecretStorage` (requires VSCode 1.53+). 
- **Contingency**: If the engine version is older, I will bump the `engines.vscode` requirement in `package.json` to `^1.53.0`.
- **Assumption**: The .NET 9 SDK is fully available to compile the updated `System.Threading.Channels` and security APIs.