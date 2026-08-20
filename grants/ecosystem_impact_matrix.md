# Ecosystem Impact Matrix — DataGuard

| Dimension | Evidence |
|-----------|----------|
| **Problem size** | Legacy .NET + stored procedures is a large, under-tooled surface; EF Core issue #245 (2014) documents the missing contract-validation gap. |
| **Pattern proven elsewhere** | dbt model contracts (Core v1.5, 2023) — preflight checks at compile time; DataGuard ports the pattern to .NET SP/raw SQL. |
| **Adoption surface** | NuGet (8 packages), `dotnet tool install -g DataGuard.Cli`, Roslyn analyzers for any C# IDE, VS Code extension. |
| **Providers** | SQL Server, Oracle, MySQL, PostgreSQL — one engine, four adapters. |
| **AI-hallucination defense** | DG015/DG016 phantom table/column detection catches invented SQL identifiers — a growing AI-codegen failure mode. |
| **Security posture** | MIT license; credentials via secret managers/env only (plaintext fallback disabled); supply chain: Sigstore signing, SBOM, provenance, SHA-pinned CI actions, vulnerability gate. |
| **Verification** | 65 automated tests (analyzer execution, golden corpus strict-mode, per-rule coverage); offline demo in `scripts/demo_scan.sh` + `samples/`; real-DB Testcontainers suite planned with grant support. |
| **Grant leverage** | Funding → container-based DB integration tests, NuGet Trusted Publishing rollout (deadline 2026-11-01), companion Claude skill, documentation. |
