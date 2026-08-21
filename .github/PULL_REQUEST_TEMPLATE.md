<!-- Pull request checklist — CI enforces build + tests; you own the rest. -->

## What

<!-- One-sentence summary of the change. -->

## Why

<!-- The drift/contract failure class, bug, or standard this addresses. Link issue(s) if any. -->

## Evidence (required)

<!-- Every claim needs a command + output. Redact secrets. -->

```bash
dotnet build DataGuard.sln --no-incremental --nologo   # 0 errors, no NEW warnings
dotnet test DataGuard.sln --nologo                      # all pass
```

## Checklist

- [ ] Tests written **red before green** for bug fixes (test fails on old code)
- [ ] No new analyzer warnings (baseline: SA1000 in test project only)
- [ ] No public API breaking change (or it is explicitly approved in the linked issue)
- [ ] No secrets, connection strings, or credentials in code, logs, or test fixtures
- [ ] `README.md` / `docs/` updated when user-visible behavior changed
- [ ] Conventional commit message (`fix:`, `feat:`, `test:`, `docs:`, `chore:`)
