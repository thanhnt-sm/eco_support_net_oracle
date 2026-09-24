## Context

The DataGuard Visual Studio extension is completely non-functional when installed. Both commands (`validate` and `assess`) fail with exit codes 3 and 4 respectively, producing zero diagnostics. The user wants a single VSIX file that works immediately — no separate CLI installation.

Four independent root causes:
1. **Missing `--project`**: The VS extension never passes `--project <solutionDir>` to the CLI, so `ProjectCSharpSqlSource` never runs — no C# source scanned for SQL. Without a DB connection or snapshot, `AcquireContractsAsync` returns `Unavailable` → exit code 3.
2. **Broken path quoting**: `Quote()` in `DataGuardPackage.cs:190` doesn't handle Windows trailing backslashes. `solutionDirectory` from VS API ends with `\`, producing `"D:\path\"` where `\"` is parsed as escaped quote — entire remaining cmdline absorbed into path value → `Directory.Exists` fails → exit code 4.
3. **SARIF relative URI filtering**: `PublishSarifAsync` at line 962 skips all results where `!Path.IsPathRooted(uri)`. But the SARIF emitter (`DiagnosticEmitter.ProjectArtifactUri`) intentionally produces relative paths with `uriBaseId: "%SRCROOT%"`. Even if validation succeeds, 100% of findings are silently dropped from the Error List.
4. **CLI not bundled**: Extension requires separately-installed CLI (`dotnet tool install -g DataGuard.Cli`). User wants one VSIX file.

The VS Code extension works correctly — it passes `--project`, uses array-based args (no quoting), and resolves SARIF URIs. The VS extension must be brought to parity.

## Approach

### Step 1: Fix `Quote()` — trailing backslash escapes closing quote

**File**: `src/DataGuard.VisualStudio/DataGuardPackage.cs:190`

Current: `private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";`

Windows argument parsing: `"D:\path\"` → `\"` at end parsed as escaped quote → rest of cmdline absorbed into value.

Replace with:
```csharp
private static string Quote(string value)
{
    // On Windows, a trailing backslash before the closing quote is parsed as an
    // escaped quote (CommandLineToArgvW): "D:\path\" → D:\path" (literal quote).
    // Double trailing backslashes so \\" → literal-backslash + end-quote.
    var escaped = value.Replace("\"", "\\\"");
    if (escaped.EndsWith("\\"))
    {
        escaped += "\\";
    }

    return "\"" + escaped + "\"";
}
```

Also add defensive `TrimEnd` on `solutionDirectory` immediately after the null/empty guard (line 524):
```csharp
solutionDirectory = solutionDirectory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
```

### Step 2: Add `--project` to CLI validate invocation

**File**: `src/DataGuard.VisualStudio/DataGuardPackage.cs:591-593`

Current validate args:
```
validate --config <configPath> --format sarif --output <sarifPath> --progress [--skip-rules ...]
```

Change to (matching VS Code's `command-args.ts:23-36`):
```csharp
Arguments = command == "validate"
    ? "validate --config " + Quote(configPath) + " --format sarif --output " + Quote(sarifPath)
        + " --project " + Quote(solutionDirectory) + " --progress" + skipArg
    : "assess --workspace " + Quote(solutionDirectory) + " --format sarif --output " + Quote(sarifPath) + " --progress",
```

This gives `ProjectCSharpSqlSource` a path to scan. It will discover all `.cs` files, parse them with Roslyn, find Dapper/EF Core invocations (Query, FromSqlRaw, Execute, etc.), extract SQL text, resolve target C# types and their properties, and produce `RawSqlDescriptor` contracts. Those contracts then flow into all applicable rules: `ColumnShapeMatchRule` (DG004), `SelectStarUsageRule` (DG017), `RawSqlParseStatusRule` (DG016), `PhantomIdentifierRule` (DG015), `NamingConventionRule` (DG006), etc.

### Step 3: Fix `PublishSarifAsync` — resolve `%SRCROOT%` relative URIs

**File**: `src/DataGuard.VisualStudio/DataGuardPackage.cs:960-965`

Current code drops all relative URIs:
```csharp
var uri = physical.GetProperty("artifactLocation").GetProperty("uri").GetString();
if (string.IsNullOrWhiteSpace(uri) || !Path.IsPathRooted(uri))
{
    continue;
}
```

The SARIF standard uses `uriBaseId` to resolve relative URIs. DataGuard emits `uriBaseId: "%SRCROOT%"` where `%SRCROOT%` = the solution directory. Fix:

```csharp
var artifactLocation = physical.GetProperty("artifactLocation");
var uri = artifactLocation.GetProperty("uri").GetString();
if (string.IsNullOrWhiteSpace(uri))
{
    continue;
}

// Resolve relative URIs using uriBaseId (%SRCROOT% = solution directory)
if (!Path.IsPathRooted(uri))
{
    var uriBaseId = artifactLocation.TryGetProperty("uriBaseId", out var baseIdNode)
        ? baseIdNode.GetString()
        : null;
    if (!string.IsNullOrEmpty(uriBaseId) && uriBaseId == "%SRCROOT%")
    {
        uri = Path.GetFullPath(Path.Combine(solutionDirectory, uri.Replace('/', '\\')));
    }
    else
    {
        continue;
    }
}
```

This requires `solutionDirectory` to be available in `PublishSarifAsync`. Change its signature to accept it:
```csharp
private async Task<int> PublishSarifAsync(string sarifPath, string solutionDirectory)
```

Update the callsite at line 720: `var diagnosticCount = await this.PublishSarifAsync(sarifPath, solutionDirectory);`

### Step 4: Bundle CLI inside VSIX — self-contained single-file

#### 4a. MSBuild target to publish CLI into VSIX

**File**: `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`

Add after the existing `IncludeNuGetDepsInVsix` target:

```xml
<PropertyGroup>
  <DataGuardCliPublishDir>$(IntermediateOutputPath)cli\</DataGuardCliPublishDir>
</PropertyGroup>

<Target Name="PublishDataGuardCli" BeforeTargets="GetVsixSourceItems">
  <Exec Command="dotnet publish &quot;$(MSBuildThisFileDirectory)..\DataGuard.Cli\DataGuard.Cli.csproj&quot; -c $(Configuration) -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o &quot;$(DataGuardCliPublishDir)&quot;" />
  <ItemGroup>
    <VSIXSourceItem Include="$(DataGuardCliPublishDir)dataguard.exe">
      <VSIXSubPath>cli</VSIXSubPath>
    </VSIXSourceItem>
  </ItemGroup>
</Target>
```

Result: `cli/dataguard.exe` bundled inside VSIX. Self-contained so it runs without .NET SDK on target machine.

#### 4b. Prioritize bundled CLI in `FindCliExecutable`

**File**: `src/DataGuard.VisualStudio/DataGuardLogger.cs:200`

Insert after the custom-path check (line 206) but before env/candidate checks (line 208):

```csharp
// Check for CLI bundled inside the extension directory
var extensionDir = Path.GetDirectoryName(typeof(DataGuardLogger).Assembly.Location);
if (!string.IsNullOrEmpty(extensionDir))
{
    var bundledCli = Path.Combine(extensionDir, "cli", "dataguard.exe");
    if (File.Exists(bundledCli))
    {
        return bundledCli;
    }
}
```

Search order: custom path → bundled → env var → dotnet-tools → PATH.

### Step 5: Tests

**File**: `tests/DataGuard.VisualStudio.Tests/`

1. **`Quote` trailing backslash**: `Quote(@"D:\path\to\solution\")` → parsed result is exactly `D:\path\to\solution\`, not mangled.
2. **`PublishSarifAsync` relative URI resolution**: Given SARIF with `uri: "src/Foo.cs"` and `uriBaseId: "%SRCROOT%"`, verify the Error List entry has `Document = @"D:\solution\src\Foo.cs"`.
3. **`FindCliExecutable` bundled priority**: When assembly directory contains `cli\dataguard.exe`, it is returned before any PATH search.

## Critical files & anchors

| File | Symbol/Region | Why |
|------|--------------|-----|
| `DataGuardPackage.cs:190` | `Quote()` | Trailing backslash quoting bug |
| `DataGuardPackage.cs:591-593` | `startInfo.Arguments` in `RunCliAsync` | Missing `--project` |
| `DataGuardPackage.cs:960-965` | `PublishSarifAsync` URI filter | Drops 100% of relative-path findings |
| `DataGuardLogger.cs:200` | `FindCliExecutable` | Must prioritize bundled CLI |
| `DataGuard.VisualStudio.csproj` | Build target | Bundle self-contained CLI |

## Verification

1. **Build VSIX**: `msbuild src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj /t:Rebuild /p:CreateVsixContainer=true /p:Configuration=Release /restore`
2. **Inspect VSIX**: Rename `.vsix` → `.zip`, confirm `cli/dataguard.exe` exists.
3. **Unit tests**: Run `dotnet test tests/DataGuard.VisualStudio.Tests/` — all Quote, SARIF, and CLI discovery tests pass.
4. **End-to-end**: Install VSIX in VS, open real project with Dapper/EF Core code, run DataGuard → Validate:
   - CLI found (bundled)
   - `--project` passed → SQL queries discovered → contracts > 0
   - Rules evaluated → diagnostics appear in Error List (not silently dropped)
   - Exit code 0 or 1 — NOT 3 or 4
5. **Assess**: Run DataGuard → Assess. Workspace path parsed correctly, projects discovered, findings in Error List.

## Assumptions & contingencies

- Self-contained single-file for `win-x64` is acceptable — VSIX already targets only `amd64` Windows per manifest. If size unacceptable (>100MB): `PublishTrimmed=true` reduces to ~30-40MB.
- `solutionDirectory` from `IVsSolution.GetSolutionInfo` may/may not end with `\`. Both `Quote` fix and `TrimEnd` applied defensively.
- The `%SRCROOT%` uriBaseId convention is DataGuard's own — no external SARIF consumers depend on it. Resolving to absolute paths in `PublishSarifAsync` is the correct approach for VS Error List integration.
