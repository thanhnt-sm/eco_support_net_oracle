# Best Practices

These examples assume an exact reviewed CLI build/artifact is already installed and on PATH; `dataguard version` checks the command, not installation. See the [installation guide](installation-guide.md). Public NuGet availability is not assumed.

Live examples use `DATAGUARD_DB` supplied by an approved session/CI secret mechanism; do not put its value in command arguments. Source checked at `ca68a3159425495f771da866188df75a8237ab4b`: YAML uses PascalCase as defined by `ConfigDocument`. Runtime checks still needed are the actual database/schema, audit/secret backend and CI upload on the target host.

## Installation & Setup

### 1. Use Snapshot Mode for CI

```yaml
# .dataguard.yml for CI
GroundTruthMode: Snapshot
SnapshotFilePath: ".dataguard-snapshot.json"
```

**Why:** No database credentials needed in CI. Review and provision the snapshot, then validate offline.

### 2. Review generated metadata before sharing

Review config, snapshots and baselines for credentials, schema/procedure definitions, paths and finding text. Commit only metadata approved for that repository; use access-controlled artifacts when necessary. Baselines suppress known findings, so each change needs review.

### 3. Pin the CLI independently of application dependencies

```bash
dataguard version
```

**Why:** An explicitly selected artifact/version is reproducible without adding the CLI as an application dependency.

## Configuration

### 4. Never Hardcode Connection Strings

```yaml
# ❌ Bad
ConnectionString: "Server=prod-db;Password=secret123"

# ✅ Good - use env var
ConnectionString: null  # Set DATAGUARD_CONNECTION_STRING env var
```

### 5. Enable Audit Logging in Production

```yaml
EnableAuditLogging: true
AuditLogPath: "/var/log/dataguard/audit.jsonl"
```

**Why:** Audit events support investigation. Keyed HMAC needs `DATAGUARD_AUDIT_KEY` or `AuditKeyFile`; logging alone does not establish compliance.

### 6. Disable Plaintext Fallback

```yaml
AllowPlaintextConfigFallback: false  # Default, never change in production
```

**Why:** Prevents silent credential downgrade. Only enable in local development.

## Validation Workflow

### 7. Review baselines before enforcement

```bash
dataguard baseline --connection-env DATAGUARD_DB --provider oracle
dataguard validate --offline
```

Baseline matching suppresses known findings when enabled; it does not suppress acquisition failures or prove coverage. Review stderr, unevaluated contracts and the command's exit status. Recreating a baseline can accept regressions, so do not do it merely to make CI pass.

### 8. Use `--fail-on-drift` in CI

```bash
dataguard snapshot diff --fail-on-drift
```

**Why:** Catches schema changes that break contracts before they reach production.

### 9. Run Oracle Check Separately

```bash
dataguard oracle-check --format sarif --output oracle-results.sarif
```

**Why:** This live command checks Oracle catalog column metadata; it is not a second execution of all source/analyzer rules. It emits console diagnostics and optional SARIF; its accepted `--format` option does not change that output implementation.

## Code Practices

### 10. Use `[SkipContractCheck]` Sparingly

```csharp
[SkipContractCheck]  // Only for truly dynamic SQL
public IQueryable<T> DynamicQuery<T>(string whereClause) { ... }
```

**Why:** Each skip is a blind spot. Document why in the attribute or a comment.

### 11. Declare manual ground truth deliberately

```csharp
[ExpectedColumn("CUSTOMER_ID", "System.Int32", IsNullable = false)]
public int CustomerId { get; set; }
```

These attributes supply manual-mode expectations through `ManualContractSource`; they do not independently verify the live schema or guarantee compile-time checks. Review the trusted assembly and explicit CLI mode in [usage](../USAGE.md).

### 12. Keep Snapshots Fresh

```bash
# After any schema change
dataguard snapshot refresh --connection-env DATAGUARD_DB --provider oracle
```

**Why:** Stale snapshots give false confidence. Refresh after every DBA schema change.

## CI/CD Integration

### 13. Upload SARIF to GitHub

```yaml
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: results.sarif
```

**Why:** Violations appear in GitHub Security tab, linked to code locations.

### 14. Preserve all failure states

```bash
dataguard validate --offline
```

Do not interpret every nonzero code as a new violation: invalid configuration, incomplete evidence and execution failure have separate outcomes. See the command-specific [exit codes](../USAGE.md).

### 15. Run Assessment on Schedule

```yaml
on:
  schedule:
    - cron: '0 6 * * 1'  # Weekly Monday 6am
jobs:
  assess:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dataguard version
      - run: dataguard assess --format sarif --output assess.sarif
```

**Why:** Assessment reports supported dependency, framework and config findings; it does not guarantee detection of every vulnerable dependency or leaked secret. Provision the CLI before this workflow fragment runs.

## Security

### 16. Use Secret Managers

```yaml
# Production: use Key Vault / Secrets Manager
KeyVaultUri: "https://my-vault.vault.azure.net/"
# or
AwsRegion: "us-east-1"
```

### 17. Rotate Credentials Regularly

```yaml
EnableCredentialRotationDetection: true
CredentialRotationWarningDays: 30
```

### 18. Review Audit Logs

```bash
# Check for failed access attempts
cat audit-log.jsonl | jq 'select(.Success == false)'
```

## Performance

### 19. Tune Parallelism

```yaml
# For large codebases
MaxDegreeOfParallelism: 4  # Limit to avoid DB overload
MaxViolationQueueSize: 50000  # Reduce for memory-constrained CI
```

### 20. Measure snapshot performance

Snapshot mode avoids live database round-trips, but no fixed speedup is established here. Compare the same source/snapshot corpus, rule set, hardware and warm/cold process conditions; record elapsed time, memory and unevaluated counts.

## Anti-Patterns

| ❌ Don't | ✅ Do Instead |
|----------|--------------|
| Hardcode connection strings | Use env vars or secret managers |
| Skip baseline for legacy code | Baseline first, enforce incrementally |
| Ignore findings or unevaluated contracts | Review the rule and available evidence |
| Run Full mode in CI | Use Snapshot mode |
| Disable audit logging | Keep enabled, rotate logs |
| Use `[SkipContractCheck]` everywhere | Fix the actual contract mismatch |
