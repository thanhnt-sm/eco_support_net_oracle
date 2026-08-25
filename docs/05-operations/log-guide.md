# Log Guide

## Log Sources

### 1. Console Output (Default)

DataGuard outputs validation results to stdout by default.

```bash
# Standard output
dataguard validate

# Verbose output (includes timing, provider info)
dataguard validate --verbose
```

### 2. SARIF Output

Machine-readable format for CI integration.

```bash
dataguard validate --format sarif --output results.sarif
```

SARIF structure:
```json
{
  "version": "2.1.0",
  "runs": [{
    "tool": { "driver": { "name": "DataGuard", "version": "0.1.0" } },
    "results": [{
      "ruleId": "DG001",
      "level": "error",
      "message": { "text": "Parameter count mismatch..." },
      "locations": [{ "physicalLocation": { "artifactLocation": { "uri": "..." } } }]
    }]
  }]
}
```

### 3. Evidence Artifact

Versioned, redacted evidence for CI.

```bash
dataguard validate --format evidence --output evidence.json
```

### 4. Audit Log

Security audit trail with hash chain.

```jsonl
{"timestamp":"2026-01-15T10:30:00Z","eventType":"DatabaseOperation","operation":"Validate","provider":"oracle","connectionStringHash":"abc123","details":"Full validation","success":true,"hash":"sha256:...","previousHash":"sha256:..."}
{"timestamp":"2026-01-15T10:30:05Z","eventType":"CredentialAccess","operation":"GetConnection","provider":"oracle","connectionStringHash":"abc123","hash":"sha256:...","previousHash":"sha256:..."}
```

## Understanding Output

### Exit Codes

| Code | Meaning | Action |
|------|---------|--------|
| `0` | Success — no violations, no drift | None needed |
| `1` | Violations found or drift detected | Review violations, fix or baseline |
| `2` | Config/usage error | Fix command args or config file |

### Violation Format

```
DG001: Parameter count mismatch for 'GetCustomer': expected 3, got 2
  at MyApp.Data.CustomerRepository.GetCustomer(int id) (CustomerRepository.cs:45)
```

Components:
- **Rule ID**: `DG001` (see Rules Reference)
- **Message**: Human-readable description
- **Location**: File and line number (when available)

### Rule IDs Reference

| ID | Rule | Severity |
|----|------|----------|
| DG001 | Parameter count match | Error |
| DG002 | Parameter type match | Error |
| DG003 | Parameter direction match | Error |
| DG004 | Result-set column shape | Error |
| DG005 | Nullability match | Warning |
| DG006 | Naming convention | Warning |
| DG007 | Length exceeds column (Oracle) | Error |
| DG008 | Byte-length overflow (Oracle) | Error |
| DG009 | Inferred size fallback (Oracle) | Warning |
| DG010 | Oracle syntax in non-Oracle context | Warning |
| DG011 | Non-Oracle function in Oracle context | Warning |
| DG012 | Provider option mismatch | Warning |
| DG013 | SQL Server syntax leak in Oracle | Error |
| DG014 | Raw SQL unmapped type usage | Warning |
| DG015 | Phantom table (AI hallucination) | Error |
| DG016 | Phantom column (AI hallucination) | Error |
| MY001 | MySQL syntax check | Warning |
| MY002 | MySQL length check | Warning |
| MY003 | MySQL type check | Warning |
| PG001 | PostgreSQL syntax check | Warning |
| PG002 | PostgreSQL length check | Warning |
| PG003 | PostgreSQL type check | Warning |

## Log Analysis Patterns

### Find All Errors

```bash
dataguard validate 2>&1 | grep "error"
```

### Count Violations by Rule

```bash
dataguard validate --format sarif --output /dev/stdout | \
  jq '.runs[0].results | group_by(.ruleId) | map({rule: .[0].ruleId, count: length})'
```

### Find Oracle-Specific Issues

```bash
dataguard oracle-check --verbose 2>&1 | grep "DG0[0-9][0-9]"
```

### Check Audit Log Integrity

```bash
cat audit-log.jsonl | jq -r '.hash' | head -20
```

### Monitor Validation Duration

```bash
time dataguard validate --verbose 2>&1 | grep "Duration"
```

## CI Log Integration

### GitHub Actions

```yaml
- name: Validate contracts
  run: dataguard validate --format sarif --output results.sarif
  continue-on-error: true

- name: Upload SARIF
  uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: results.sarif
```

### Azure DevOps

```yaml
- script: dataguard validate --format sarif --output $(Build.SourcesDirectory)/results.sarif
  displayName: 'Validate contracts'

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: '$(Build.SourcesDirectory)/results.sarif'
    artifactName: 'sarif-results'
```
