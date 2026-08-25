# Future Directions & Roadmap

## Current State (v0.1.x)

- ✅ 16+ validation rules (DG001-DG016, MY001-003, PG001-003)
- ✅ 4 database adapters (Oracle, SQL Server, MySQL, PostgreSQL)
- ✅ 3 ground-truth modes (Full, Snapshot, Manual)
- ✅ CLI tool with 9 commands
- ✅ Roslyn analyzers + quick fixes
- ✅ VS Code + Visual Studio extensions
- ✅ Zero-trust security architecture
- ✅ Baseline management + drift detection
- ✅ SARIF output for CI
- ✅ Assessment engine for legacy codebases
- ✅ Plugin architecture (MEF)
- ✅ 291+ tests, 68.7% coverage

## Short-Term Roadmap (v0.2.x — Q1 2027)

### 1. Enhanced Oracle Support

- **Oracle Package Body parsing**: Extract contracts from PACKAGE BODY, not just PACKAGE spec
- **Oracle Type support**: OBJECT types, VARRAY, NESTED TABLE
- **Oracle Materialized View contracts**: Validate MV refresh SQL
- **Oracle Synonym resolution**: Follow synonyms to base objects

### 2. SQL Server Enhancements

- **Table-Valued Parameter (TVP) support**: Validate TVP type definitions
- **SQLCLR procedure contracts**: Extract contracts from CLR stored procedures
- **Temporal table support**: Validate SYSTEM_TIME columns
- **Graph table support**: MATCH clause validation

### 3. MySQL & PostgreSQL Maturation

- **MySQL stored function contracts**: Extract RETURNS type
- **PostgreSQL PL/pgSQL parsing**: Extract from DO blocks and functions
- **PostgreSQL composite types**: Validate against entity properties
- **PostgreSQL ENUM types**: Validate enum mappings

### 4. IDE Improvements

- **Real-time validation**: Validate on save, not just on keystroke
- **Quick fix for all rules**: Currently only DG001 has quick fixes
- **CodeLens integration**: Show contract status inline
- **Rider support**: JetBrains Rider extension

## Medium-Term Roadmap (v0.3.x — Q3 2027)

### 5. EF Core Deep Integration

- **EF Core 10 support**: Track .NET 10 preview features
- **Owned type validation**: Validate owned entity column mappings
- **Shadow property detection**: Warn about shadow properties in contracts
- **Query filter validation**: Validate global query filter SQL

### 6. Dapper Deep Integration

- **Dapper.SqlMapper extension**: Extract contracts from Dapper queries
- **Dapper.Contrib validation**: Validate Insert/Update/Delete contracts
- **Multi-mapping validation**: Validate multi-result-set mappings

### 7. Advanced Reporting

- **HTML report generation**: Rich interactive reports
- **Trend tracking**: Track violation count over time
- **Dashboard integration**: Grafana/Datadog metrics export
- **Slack/Teams notifications**: Alert on new violations

### 8. Cloud-Native Features

- **Azure DevOps extension**: Native Azure Pipelines integration
- **AWS CodeBuild integration**: Custom action for AWS CI
- **GitHub App**: Automated PR comments with violation details
- **GitLab CI template**: Ready-to-use `.gitlab-ci.yml`

## Long-Term Roadmap (v1.0.x — 2028)

### 9. AI-Powered Features

- **AI contract suggestion**: Use LLM to suggest expected contracts from SQL
- **Auto-fix generation**: Generate code fixes for complex violations
- **Natural language queries**: "Show me all Oracle length mismatches"
- **Predictive drift detection**: Predict schema changes from migration patterns

### 10. Enterprise Features

- **Multi-repo support**: Validate contracts across repository boundaries
- **Centralized baseline management**: Shared baseline server
- **RBAC**: Role-based access to validation results
- **Compliance reporting**: SOX, PCI-DSS, HIPAA compliance templates

### 11. Performance & Scale

- **Incremental validation**: Only validate changed files
- **Distributed validation**: Split across multiple CI agents
- **Cache layer**: Cache database schema for faster Full mode
- **Streaming validation**: Process results as they're found

### 12. Ecosystem Expansion

- **MongoDB adapter**: Validate against MongoDB schemas
- **Redis adapter**: Validate Redis key patterns
- **GraphQL adapter**: Validate GraphQL schema ↔ entity mappings
- **gRPC adapter**: Validate protobuf ↔ entity mappings

## Technology Tracking

### .NET Platform

| Technology | Status | DataGuard Impact |
|------------|--------|------------------|
| .NET 10 Preview | Active | Monitor EF Core 10 changes |
| C# 14 | Preview | Pattern matching enhancements |
| EF Core 10 | Preview | New stored procedure APIs |
| Roslyn 5.x | Stable | Analyzer platform updates |

### Database Platforms

| Platform | Version | DataGuard Impact |
|----------|---------|------------------|
| Oracle 23c | GA | JSON-Relational duality views |
| SQL Server 2025 | Preview | JSON support enhancements |
| MySQL 9.0 | GA | VECTOR type support |
| PostgreSQL 17 | GA | Enhanced PL/pgSQL |

### Competitor Landscape

| Tool | Focus | DataGuard Differentiator |
|------|-------|-------------------------|
| dbt | Data engineering contracts | DataGuard targets .NET SP/SQL, not ELT |
| SQLFluff | SQL linting | DataGuard validates entity↔SP contract |
| sqlcheck | SQL anti-patterns | DataGuard has ground-truth validation |
| EF Core | ORM | EF Core declined SP contract validation (issue #245) |

## Contributing to the Roadmap

See [Contributor Guide](../08-developers/contributor-guide.md) for how to contribute features.

Priority labels:
- **P0**: Breaking issues, security vulnerabilities
- **P1**: High-demand features (GitHub issues with most 👍)
- **P2**: Nice-to-have improvements
- **P3**: Experimental/research features
