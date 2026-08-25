# Incident Runbook

## Common Incidents

### INC-001: Validation Fails in CI

**Symptoms:** CI pipeline exits with code 1 on `dataguard validate`

**Diagnosis:**
```bash
# Check what violations were found
dataguard validate --verbose 2>&1 | grep "DG[0-9]"

# Compare with baseline
dataguard validate 2>&1 | diff - .dataguard-baseline.json
```

**Resolution:**
1. If new violations: fix code or update baseline
2. If schema drift: `dataguard snapshot refresh`
3. If false positive: add `[SkipContractCheck]` attribute

**Escalation:** If violations are in third-party code, add to `excludedProcedures` in config.

---

### INC-002: Database Connection Failure

**Symptoms:** `CredentialError` or `ExtractionError` exit code

**Diagnosis:**
```bash
# Test connection directly
dataguard validate --verbose 2>&1 | grep -i "connection\|credential\|timeout"

# Check credential resolution
echo $CONNECTION_STRING
dataguard config show | grep -i connection
```

**Resolution:**
1. Verify `CONNECTION_STRING` env var is set
2. Check network connectivity to database
3. Verify credentials are not expired
4. For Oracle: check TNS names / service name
5. For SQL Server: check instance name and port

---

### INC-003: Snapshot Drift Detected

**Symptoms:** `dataguard snapshot diff` reports drift

**Diagnosis:**
```bash
# See what changed
dataguard snapshot diff --verbose

# Compare snapshot with current
dataguard snapshot show
```

**Resolution:**
1. Review drift changes with DBA
2. If intentional: `dataguard snapshot refresh`
3. If unintentional: alert DBA, investigate schema change
4. Update baseline if new violations introduced

---

### INC-004: Credential Rotation Warning

**Symptoms:** Warning about credential rotation in output

**Diagnosis:**
```bash
# Check credential age
dataguard config show | grep -i rotation

# Check audit log
cat audit-log.jsonl | grep "credential" | tail -5
```

**Resolution:**
1. Rotate credentials in secret manager
2. Update `CONNECTION_STRING` env var
3. Verify new credentials work: `dataguard validate --verbose`

---

### INC-005: Assessment Finds Critical Issues

**Symptoms:** `dataguard assess` reports Critical severity findings

**Diagnosis:**
```bash
# Get full assessment report
dataguard assess --format json --output assessment.json
cat assessment.json | jq '.findings[] | select(.severity == "Critical")'
```

**Resolution:**
1. Review each Critical finding
2. For unsupported TFM: plan upgrade (see `dataguard assess` upgrade steps)
3. For secrets in config: move to secret manager
4. For missing lock file: `dotnet restore --lock-file`

---

### INC-006: Analyzer Not Working in IDE

**Symptoms:** No squiggly underlines for SQL calls in VS Code / VS 2022

**Diagnosis:**
```bash
# Check analyzer package is installed
dotnet list package | grep DataGuard.Analyzers

# Check .csproj has analyzer reference
grep -r "DataGuard.Analyzers" *.csproj
```

**Resolution:**
1. Install analyzer package: `dotnet add package DataGuard.Analyzers`
2. Restart IDE
3. Check Output panel for analyzer errors
4. Verify project targets netstandard2.0+ or net6.0+

---

### INC-007: Docker Build Fails

**Symptoms:** Docker image build fails in CI

**Diagnosis:**
```bash
# Build locally
docker build -t dataguard:test .

# Check build logs
docker build -t dataguard:test . 2>&1 | tail -50
```

**Resolution:**
1. Verify .NET SDK version in Dockerfile matches project
2. Check `packages.lock.json` is committed
3. Ensure `DataGuard.Cli.csproj` is in build context
4. For multi-arch: ensure QEMU is set up for arm64

---

### INC-008: Audit Log Integrity Failure

**Symptoms:** Audit log hash chain verification fails

**Diagnosis:**
```bash
# Check audit log
cat audit-log.jsonl | jq '.hash' | head -20

# Verify chain
dataguard validate --verbose 2>&1 | grep -i "audit\|hash\|integrity"
```

**Resolution:**
1. **CRITICAL:** Audit log may have been tampered with
2. Preserve current log: `cp audit-log.jsonl audit-log.jsonl.bak`
3. Investigate access to audit log file
4. Consider rotating to new audit log
5. Report security incident per SECURITY.md

## Escalation Matrix

| Severity | Response Time | Action |
|----------|---------------|--------|
| Critical (INC-008) | Immediate | Security incident response |
| High (INC-001, INC-002) | 1 hour | Block CI, fix or bypass |
| Medium (INC-003, INC-004) | 4 hours | Schedule fix |
| Low (INC-005, INC-006) | Next sprint | Plan remediation |
