# Log Guide

## Log Sources

### 1. Console Output (Default)

StackAccord outputs validation results to stdout by default.

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

## Visual Studio Output and Local Log

Tools → StackAccord (Beta) writes command banners, result blocks, and rule inventory as separate logical lines in the Output pane. The local diagnostic file mirrors each line with its own UTC timestamp and level. The existing privacy policy redacts recognized credential, private-path, and SQL patterns and removes control characters before the extension adds line breaks. Redaction is best effort, not a guarantee of anonymity. A newline inside one logical record or decoded field cannot create an additional output record; raw CLI stderr still uses physical lines as message framing.

A zero exit code describes the CLI policy result; it does not prove every configured rule or contract was evaluated. For example, a run may report:

```text
Result: 0 errors, 0 warnings
Ground truth: schema+entities
Coverage: incomplete; unavailable rules 1. Review Rule ... not evaluated, UNEVALUATED and ACQUISITION lines above.
```

Follow the `Rule ... not evaluated`, `UNEVALUATED`, and `ACQUISITION` lines to identify missing prerequisites and retry after resolving them. When no ground truth was loaded, the extension reports syntactic-only validation and suggests Refresh Snapshot or an EF model snapshot. Counts absent from an older CLI summary are unknown, not evidence of full coverage. Rule inventory remains an execution-event list, not a per-contract coverage certificate.

Exit code 3 reports incomplete validation. The exit explanation does not determine Error List publication: the extension separately reports loaded diagnostics, missing or unreadable SARIF, discarded results, or an unavailable Error List provider. A valid SARIF with the IDE-safe handshake can replace previous tasks, including clearing them when it has no diagnostics.

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
