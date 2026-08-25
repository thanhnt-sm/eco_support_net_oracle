# MySQL Adapter

The MySQL Adapter provides contract validation between .NET entities and MySQL stored procedures using the MySqlConnector library and `INFORMATION_SCHEMA` views.

## Architecture

```mermaid
graph TB
    subgraph "DataGuard.MySql.Adapter"
        MSP[MySqlStoredProcedureParser]
        MDC[MySqlDialectChecker]
        MLMD[MySqlLengthMismatchDetector]
    end

    subgraph "MySQL INFORMATION_SCHEMA"
        R[(ROUTINES)]
        P[(PARAMETERS)]
    end

    subgraph "Rules Engine"
        MY001[MySqlSyntaxRule]
        MY002[NonMySqlSyntaxRule]
        MY003[MySqlLengthExceedsColumnRule]
    end

    MSP -->|SQL queries| R
    MSP -->|SQL queries| P

    MY001 --> MDC
    MY002 --> MDC
    MY003 --> MLMD
```

## Source Files

| File | Lines | Purpose |
|------|-------|---------|
| `MySqlStoredProcedureParser.cs` | ~110 | IContractSource implementation for MySQL SPs |
| `MySqlDialectChecker.cs` | ~100 | Dialect detection + MY001/MY002 rules |
| `MySqlLengthMismatchDetector.cs` | ~60 | Length mismatch detection + MY003 rule |

## Dependencies

```xml
<PackageReference Include="MySqlConnector" Version="2.4.3" />
<ProjectReference Include="..\DataGuard.Core\DataGuard.Core.csproj" />
```

## MySqlStoredProcedureParser

Implements `IContractSource` to extract stored procedure contracts from MySQL's `INFORMATION_SCHEMA`.

### Query Pattern

```sql
SELECT r.ROUTINE_NAME, p.PARAMETER_NAME, p.DATA_TYPE, p.PARAMETER_MODE,
       p.ORDINAL_POSITION, p.CHARACTER_MAXIMUM_LENGTH, p.NUMERIC_PRECISION,
       p.NUMERIC_SCALE, r.ROUTINE_SCHEMA
FROM information_schema.ROUTINES r
LEFT JOIN information_schema.PARAMETERS p
  ON r.SPECIFIC_SCHEMA = p.SPECIFIC_SCHEMA AND r.SPECIFIC_NAME = p.SPECIFIC_NAME
WHERE r.ROUTINE_TYPE = 'PROCEDURE' AND (@schema = '' OR r.ROUTINE_SCHEMA = @schema)
ORDER BY r.ROUTINE_SCHEMA, r.ROUTINE_NAME, p.ORDINAL_POSITION
```

### Key Design Decisions

- **LEFT JOIN**: Procedures without parameters still appear in results (as a single row with NULL parameter fields). These are skipped via `reader.IsDBNull(1)` check.
- **Schema filtering**: Empty schema string means "all schemas"; otherwise filters by exact match.
- **Overload handling**: MySQL does not support procedure overloading, so each procedure name maps to exactly one contract.
- **Length normalization**: `CHARACTER_MAXIMUM_LENGTH` returns `BIGINT` — normalized to `int?` with overflow protection.

### Direction Mapping

| MySQL Mode | DataGuard Direction |
|------------|---------------------|
| `IN` | `Input` |
| `OUT` | `Output` |
| `INOUT` | `InputOutput` |

### Contract ID Format

```
mysql:{schema}.{procedure_name}
```

## MySqlDialectChecker

Detects MySQL-specific syntax in non-MySQL contexts and vice versa using keyword matching via `SqlKeywordMatcher.ContainsAny()`.

### MySQL-Only Keywords

| Keyword | Purpose |
|---------|---------|
| `ON DUPLICATE KEY` | MySQL upsert syntax |
| `REPLACE INTO` | MySQL replace syntax |
| `` ` `` (backtick) | MySQL identifier quoting |
| `ENGINE=InnoDB` | MySQL storage engine |
| `AUTO_INCREMENT` | MySQL auto-increment |

### Non-MySQL Keywords (detected in MySQL context)

| Keyword | Origin |
|---------|--------|
| `NVL` | Oracle |
| `TOP ` | SQL Server |
| `ROWNUM` | Oracle |
| `GETDATE` | SQL Server |
| `FETCH FIRST` | Standard SQL / PostgreSQL |

## MySqlLengthMismatchDetector

Compares entity `MaxLength` against MySQL column `CHARACTER_MAXIMUM_LENGTH`. Simple direct comparison — MySQL does not have Oracle's CHAR/BYTE semantics complexity.

### Detection Logic

```csharp
foreach (var property in entity.Properties)
{
    var column = columns.FirstOrDefault(c =>
        string.Equals(c.Name, property.ColumnName, StringComparison.OrdinalIgnoreCase));
    if (column == null || !property.MaxLength.HasValue || !column.MaxLength.HasValue)
        continue;

    if (property.MaxLength.Value > column.MaxLength.Value)
        yield return new ContractViolation("MY003", ...);
}
```

## Rules Reference

### MY001 — MySQL Syntax in Non-MySQL Context

| Property | Value |
|----------|-------|
| **Severity** | Warning |
| **Trigger** | MySQL keywords (`ON DUPLICATE KEY`, backticks, etc.) in non-MySQL SQL |
| **Message** | MySQL-specific syntax '{syntax}' used in non-MySQL context |

### MY002 — Non-MySQL Syntax in MySQL Context

| Property | Value |
|----------|-------|
| **Severity** | Warning |
| **Trigger** | Oracle/SQL Server keywords (`NVL`, `TOP`, `GETDATE`, etc.) in MySQL SQL |
| **Message** | Non-MySQL syntax '{syntax}' used in MySQL context |

### MY003 — Entity Length Exceeds MySQL Column Length

| Property | Value |
|----------|-------|
| **Severity** | Error |
| **Trigger** | `property.MaxLength > column.MaxLength` |
| **Message** | Entity property '{name}' MaxLength={n} exceeds column '{col}' length={m} |

## Usage in CLI

```bash
# Validate MySQL contracts
dataguard validate --provider mysql --connection "Server=localhost;Database=mydb;Uid=root;..."

# With schema filter
dataguard validate --provider mysql --connection "..." --schema mydb
```

## MySQL-Specific Considerations

### No Package Support

Unlike Oracle, MySQL does not have packages. The `PackageName` field is always empty in MySQL contracts.

### INFORMATION_SCHEMA Limitations

MySQL's `INFORMATION_SCHEMA.PARAMETERS` may not be available for all MySQL versions or configurations. The adapter gracefully handles missing data by skipping NULL parameter rows.

### Character Set Awareness

MySQL's `CHARACTER_MAXIMUM_LENGTH` is always in characters (not bytes), regardless of the column's character set. This simplifies length comparison compared to Oracle's CHAR/BYTE semantics.
