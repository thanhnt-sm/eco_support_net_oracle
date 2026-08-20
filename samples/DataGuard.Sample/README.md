# DataGuard Sample — End-to-end demo

A runnable sample that shows DataGuard in action without a database:

1. **Manual ground truth** — the entity/SP contracts below are declared with
   `[ExpectedColumn]` / `[ExpectedSpParameter]` attributes.
2. **Validate offline** — `dataguard validate --offline --assembly` reads the
   attributes and runs the rules (naming, nullability, direction, ...).

## Run

```bash
dotnet build -c Release
cd ../../  # repo root
dotnet run --project src/DataGuard.Cli -- validate --offline \
  --assembly samples/DataGuard.Sample/bin/Release/net9.0/DataGuard.Sample.dll \
  --provider sqlserver
```

You should see DG006 naming violations for `PhoneNo` vs `PHONE` (snake_case convention),
and DG005 nullability warnings for required-but-nullable `Email`.

## With a real database

1. `dataguard init`
2. `dataguard snapshot refresh --connection "<your-connection-string>" --provider <sqlserver|oracle|mysql|postgresql>`
3. `dataguard validate --provider <provider>` (Full mode, live DB)
4. `dataguard snapshot diff --fail-on-drift` (drift gate for CI)
