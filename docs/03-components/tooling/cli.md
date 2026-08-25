# CLI Reference

The DataGuard CLI (`dataguard`) is the primary interface for contract validation, schema management, and environment assessment. Built with `System.CommandLine`, it provides 9 commands with consistent option patterns.

## Command Tree

```mermaid
graph TB
    ROOT[dataguard] --> V[validate]
    ROOT --> B[baseline]
    ROOT --> S[snapshot]
    ROOT --> I[init]
    ROOT --> C[config]
    ROOT --> OC[oracle-check]
    ROOT --> M[migrate]
    ROOT --> A[assess]
    ROOT --> VER[version]

    S --> SR[refresh]
    S --> SS[show]
    S --> SD[diff]

    C --> CS[show]
    C --> CV[validate]
```

## Commands

### `validate`

Validates entity contracts against database schema or snapshot.

```bash
dataguard validate [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | Database connection string |
| `--config` | — | Path to `.dataguard.yml` config file |
| `--output` | — | Output file path (required for sarif/evidence) |
| `--format` | `text` | Output format: `text`, `sarif`, `evidence`, `contracts`, `typescript` |
| `--offline` | `false` | Run in offline mode (no DB connection, requires `--assembly`) |
| `--verbose` | `false` | Enable verbose output |
| `--provider` | `sqlserver` | Database provider: `sqlserver`, `oracle`, `mysql`, `postgresql` |
| `--schema` | — | Database schema/owner name |
| `--assembly` | — | Path to compiled assembly for Manual ground-truth mode |

**Behavior:**
- Without `--connection`: validates against committed snapshot (Snapshot mode)
- With `--offline`: requires `--assembly` for Manual ground-truth mode using `[ExpectedColumn]`/`[ExpectedSpParameter]` attributes
- `--format contracts`: exports extracted contracts as JSON
- `--format typescript`: exports TypeScript DTOs from entity descriptors

### `baseline`

Creates a baseline from current violations for drift detection.

```bash
dataguard baseline [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | Database connection string |
| `--config` | — | Path to config file |
| `--output` | `.dataguard-baseline.json` | Baseline output path |
| `--verbose` | `false` | Verbose output |
| `--provider` | `sqlserver` | Database provider |
| `--schema` | — | Schema/owner name |
| `--package` | — | Oracle package name |

**Output includes:**
- Violation list with rule IDs and messages
- Database version (from `@@VERSION` or `V$VERSION`)
- Schema hash (SHA-256, first 16 hex chars)

### `snapshot`

Manages schema snapshots for offline validation and drift detection.

#### `snapshot refresh`

Refreshes the snapshot from the live database.

```bash
dataguard snapshot refresh [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | Database connection string |
| `--config` | — | Path to config file |
| `--verbose` | `false` | Verbose output |
| `--provider` | `sqlserver` | Database provider |
| `--schema` | — | Schema/owner name |
| `--package` | — | Oracle package name |

**Oracle-specific:** When provider is Oracle, captures the full schema (all tables, all columns with `CHAR_USED`, `CHAR_LENGTH`) into the snapshot for offline length-mismatch detection.

#### `snapshot show`

Displays current snapshot metadata.

```bash
dataguard snapshot show [--config <path>]
```

**Output:**
- Snapshot file path
- Version, schema version, ground truth mode
- Database version, schema hash
- Creation timestamp, violation count

#### `snapshot diff`

Compares current schema with the committed snapshot.

```bash
dataguard snapshot diff [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--fail-on-drift` | `false` | Exit non-zero when drift is detected |

**Drift detection:**
- Uses schema-based hashing when snapshot contains persisted schema (Oracle)
- Falls back to violation-based hashing for legacy v1 snapshots
- In CI environments (`CI` or `GITHUB_ACTIONS` set), warns about drift even without `--fail-on-drift`

### `init`

Initializes a DataGuard configuration file.

```bash
dataguard init [--output <path>] [--provider <name>]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--output` | `.dataguard.yml` | Output config file path |
| `--provider` | `sqlserver` | Default provider |

**Generated config:**
```yaml
GroundTruthMode: Snapshot
SnapshotFilePath: .dataguard-snapshot.json
BaselineFilePath: .dataguard-baseline.json
NamingConvention: SnakeCaseToPascalCase
EnableBaseline: true
```

### `config`

Manages DataGuard configuration.

#### `config show`

Displays current configuration with secrets redacted.

```bash
dataguard config show [--config <path>]
```

**Security:** Connection strings are always redacted to `***redacted***` in output.

#### `config validate`

Validates a configuration file.

```bash
dataguard config validate [--config <path>]
```

### `oracle-check`

Runs Oracle-specific dialect and length checks.

```bash
dataguard oracle-check [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--connection` | — | **Required.** Oracle connection string |
| `--config` | — | Path to config file |
| `--output` | — | Output file path |
| `--format` | `text` | Output format |
| `--verbose` | `false` | Verbose output |
| `--schema` | — | Oracle owner/schema |
| `--package` | — | Oracle package name |

**Pipeline:**
1. Resolves NLS length semantics (CHAR vs BYTE)
2. Reads full schema (all tables, all columns)
3. Runs dialect checks against column types
4. Reports unmapped type usage

### `migrate`

Migrates a legacy baseline file (v1) to v2 format.

```bash
dataguard migrate [--baseline <path>]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--baseline` | `.dataguard-baseline.json` | Path to baseline file to migrate |

### `assess`

Runs read-only environment/dependency/config assessment.

```bash
dataguard assess [options]
```

| Option | Default | Description |
|--------|---------|-------------|
| `--workspace` | `.` | Workspace root to assess |
| `--project-filter` | — | Optional project path filters (substring, case-insensitive) |
| `--output` | — | Output file path |
| `--format` | `text` | Output format: `text`, `json`, `sarif` |
| `--verbose` | `false` | Verbose output |

**Assessment packs:**
- Inventory: project files, target frameworks
- Dependencies: NuGet packages, version analysis
- Build/CI: build scripts, CI configuration
- Secrets: hardcoded credentials detection
- Dependency health: outdated/vulnerable packages

### `version`

Displays DataGuard version information.

```bash
dataguard version
```

**Output:**
- CLI version (from `AssemblyInformationalVersion`)
- .NET runtime version
- OS version
- Component versions: Core, Oracle.Adapter, SqlServer.Adapter, Analyzers

## Common Options

| Option | Short | Description |
|--------|-------|-------------|
| `--connection` | — | Database connection string |
| `--config` | `-c` | Path to `.dataguard.yml` |
| `--output` | `-o` | Output file path |
| `--format` | `-f` | Output format |
| `--offline` | — | Offline mode (no DB) |
| `--verbose` | `-v` | Verbose output |
| `--provider` | `-p` | Database provider |
| `--schema` | `-s` | Schema/owner name |
| `--package` | — | Oracle package name |
| `--assembly` | — | Assembly path for Manual mode |
| `--fail-on-drift` | — | Exit non-zero on drift |

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Pass — no errors found |
| `1` | Fail — errors detected or operational failure |
| `2` | Config error — invalid options or unsupported format |

## Output Formats

### `text` (default)

Human-readable console output with color-coded severity.

### `sarif`

SARIF 2.1.0 JSON format for IDE integration and CI pipelines. Requires `--output`.

### `evidence`

Contract evidence JSON for audit trails. Requires `--output`.

### `contracts`

Exported contract descriptors as JSON. Requires `--output`.

### `typescript`

TypeScript DTO definitions exported from entity descriptors. Requires `--output`.

## Configuration File

The `.dataguard.yml` file supports all configuration options:

```yaml
GroundTruthMode: Snapshot          # Snapshot | Manual | Full
ConnectionString: "Server=..."     # Prefer env DATAGUARD_CONNECTION_STRING
DefaultSchema: dbo
DefaultPackage: ""                 # Oracle package name
NamingConvention: SnakeCaseToPascalCase
EnableBaseline: true
BaselineFilePath: .dataguard-baseline.json
SnapshotFilePath: .dataguard-snapshot.json
EnableConcurrentValidation: true
MaxDegreeOfParallelism: 4
```

**Security note:** Never commit connection strings to source control. Use environment variable `DATAGUARD_CONNECTION_STRING` instead.

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `DATAGUARD_CONNECTION_STRING` | Database connection string (overrides config) |
| `CI` | Detected for CI-specific behavior |
| `GITHUB_ACTIONS` | Detected for GitHub Actions-specific behavior |
