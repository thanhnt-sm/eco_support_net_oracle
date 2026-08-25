# Best Practices

## Installation & Setup

### 1. Use Snapshot Mode for CI

```yaml
# .dataguard.yml for CI
groundTruthMode: Snapshot
snapshotFilePath: ".dataguard-snapshot.json"
```

**Why:** No database credentials needed in CI. Snapshot is committed to repo, validated offline.

### 2. Commit All DataGuard Files

```bash
git add .dataguard.yml .dataguard-snapshot.json .dataguard-baseline.json
```

**Why:** Team shares same validation baseline. Drift detection works against committed snapshot.

### 3. Use Global Tool, Not Project Reference

```bash
dotnet tool install -g DataGuard.Cli
```

**Why:** Consistent version across all projects. No dependency bloat in project files.

## Configuration

### 4. Never Hardcode Connection Strings

```yaml
# ❌ Bad
connectionString: "Server=prod-db;Password=secret123"

# ✅ Good - use env var
connectionString: null  # Set CONNECTION_STRING env var
```

### 5. Enable Audit Logging in Production

```yaml
enableAuditLogging: true
auditLogPath: "/var/log/dataguard/audit.jsonl"
```

**Why:** Hash-chain audit trail for compliance and incident investigation.

### 6. Disable Plaintext Fallback

```yaml
allowPlaintextConfigFallback: false  # Default, never change in production
```

**Why:** Prevents silent credential downgrade. Only enable in local development.

## Validation Workflow

### 7. Baseline Before Enforcing

```bash
# First: create baseline for existing violations
dataguard baseline --connection "..." --provider oracle

# Then: CI only fails on NEW violations
dataguard validate
```

**Why:** Legacy codebases have existing violations. Baseline lets you enforce incrementally.

### 8. Use `--fail-on-drift` in CI

```bash
dataguard snapshot diff --fail-on-drift
```

**Why:** Catches schema changes that break contracts before they reach production.

### 9. Run Oracle Check Separately

```bash
dataguard oracle-check --format sarif --output oracle-results.sarif
```

**Why:** Oracle-specific rules (CHAR/BYTE semantics, dialect checks) are separate from general validation.

## Code Practices

### 10. Use `[SkipContractCheck]` Sparingly

```csharp
[SkipContractCheck]  // Only for truly dynamic SQL
public IQueryable<T> DynamicQuery<T>(string whereClause) { ... }
```

**Why:** Each skip is a blind spot. Document why in the attribute or a comment.

### 11. Prefer Manual Attributes for New Code

```csharp
[ExpectedColumn("CUSTOMER_ID", "int", IsNullable = false)]
public int CustomerId { get; set; }
```

**Why:** Zero database access needed. Catches mismatches at compile time via Roslyn analyzer.

### 12. Keep Snapshots Fresh

```bash
# After any schema change
dataguard snapshot refresh --connection "..." --provider oracle
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

### 14. Fail CI on New Violations Only

```bash
# Baseline filters out known violations
dataguard validate  # Exit 1 only for new violations
```

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
      - run: dotnet tool install -g DataGuard.Cli
      - run: dataguard assess --format sarif --output assess.sarif
```

**Why:** Catches dependency rot, unsupported TFMs, and secret leaks before they become incidents.

## Security

### 16. Use Secret Managers

```yaml
# Production: use Key Vault / Secrets Manager
keyVaultUri: "https://my-vault.vault.azure.net/"
# or
awsRegion: "us-east-1"
```

### 17. Rotate Credentials Regularly

```yaml
enableCredentialRotationDetection: true
credentialRotationWarningDays: 30
```

### 18. Review Audit Logs

```bash
# Check for failed access attempts
cat audit-log.jsonl | jq 'select(.success == false)'
```

## Performance

### 19. Tune Parallelism

```yaml
# For large codebases
maxDegreeOfParallelism: 4  # Limit to avoid DB overload
maxViolationQueueSize: 50000  # Reduce for memory-constrained CI
```

### 20. Use Snapshot Mode for Speed

Snapshot mode is 10-100x faster than Full mode (no DB round-trips).

## Anti-Patterns

| ❌ Don't | ✅ Do Instead |
|----------|--------------|
| Hardcode connection strings | Use env vars or secret managers |
| Skip baseline for legacy code | Baseline first, enforce incrementally |
| Ignore DG006 warnings | Fix naming conventions early |
| Run Full mode in CI | Use Snapshot mode |
| Disable audit logging | Keep enabled, rotate logs |
| Use `[SkipContractCheck]` everywhere | Fix the actual contract mismatch |
