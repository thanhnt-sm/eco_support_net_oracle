using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Abstractions;

/// <summary>
/// Represents a source of contract information (EF model, stored procedure, raw SQL).
/// </summary>
public interface IContractSource
{
    /// <summary>
    /// Gets the unique identifier for this source.
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Gets the display name for this source.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Extracts contract descriptors from the source.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a contract rule that validates a specific aspect of the contract.
/// </summary>
public interface IContractRule
{
    /// <summary>
    /// Gets the unique rule identifier (e.g., "DG001").
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Gets the human-readable rule name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the rule severity.
    /// </summary>
    DiagnosticSeverity Severity { get; }

    /// <summary>
    /// Gets the rule description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Validates the contract and returns violations.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a contract violation found during validation.
/// </summary>
public record ContractViolation(
    string RuleId,
    string Message,
    DiagnosticSeverity Severity,
    Location? Location = null,
    IReadOnlyDictionary<string, object?>? Properties = null);

/// <summary>
/// Represents a contract descriptor (entity, stored procedure, or raw SQL).
/// </summary>
public abstract record ContractDescriptor(
    string Id,
    string Name,
    ContractType Type,
    Location? Location = null);

/// <summary>
/// Type of contract descriptor.
/// </summary>
public enum ContractType
{
    Entity,
    StoredProcedure,
    RawSql,
    DatabaseSchema,
}

/// <summary>
/// Represents an entity contract descriptor.
/// </summary>
public record EntityDescriptor(
    string Id,
    string Name,
    string ClrTypeName,
    string? TableName,
    IReadOnlyList<PropertyDescriptor> Properties,
    Location? Location = null): ContractDescriptor(Id, Name, ContractType.Entity, Location);

/// <summary>
/// Represents a property descriptor within an entity.
/// </summary>
public record PropertyDescriptor(
    string Name,
    string ClrTypeName,
    string? ColumnName,
    string? ColumnType,
    bool IsNullable,
    int? MaxLength,
    bool IsPrimaryKey,
    bool IsForeignKey,
    IReadOnlyDictionary<string, object?>? Annotations = null);

/// <summary>
/// Represents a stored procedure parameter descriptor.
/// </summary>
public record ParameterDescriptor(
    string Name,
    string DataType,
    ParameterDirection Direction,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    int OrdinalPosition,
    int Overload = 0,
    int Sequence = 0,
    string? TypeOwner = null,
    string? TypeName = null,
    string? TypeSubname = null,
    string? ClrType = null,
    ParameterDirection? CallSiteDirection = null);

/// <summary>
/// Parameter direction.
/// </summary>
public enum ParameterDirection
{
    Input,
    Output,
    InputOutput,
    ReturnValue,
}

/// <summary>
/// Represents a stored procedure contract descriptor.
/// </summary>
public record StoredProcedureDescriptor(
    string Id,
    string Name,
    string Schema,
    string PackageName,
    IReadOnlyList<ParameterDescriptor> Parameters,
    IReadOnlyList<ColumnDescriptor> ResultColumns,
    bool ReturnsRefCursor,
    Location? Location = null): ContractDescriptor(Id, Name, ContractType.StoredProcedure, Location);

/// <summary>
/// Represents a column descriptor in a result set.
/// </summary>
public record ColumnDescriptor(
    string Name,
    string DataType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    string? CharUsed, // 'C' = CHAR, 'B' = BYTE for Oracle
    int? CharLength = null,
    string? DataDefault = null,
    int ColumnId = 0);

/// <summary>
/// Represents a raw SQL contract descriptor.
/// </summary>
public record RawSqlDescriptor(
    string Id,
    string SqlText,
    IReadOnlyList<ParameterDescriptor> Parameters,
    IReadOnlyList<ColumnDescriptor> ResultColumns,
    Location? Location = null): ContractDescriptor(Id, "Raw SQL", ContractType.RawSql, Location);

/// <summary>
/// Represents database ground-truth schema (tables + columns) used by length/dialect rules.
/// </summary>
public record DatabaseSchemaDescriptor(
    string Id,
    IReadOnlyList<DatabaseTableDescriptor> Tables,
    string LengthSemantics,
    Location? Location = null): ContractDescriptor(Id, "DatabaseSchema", ContractType.DatabaseSchema, Location);

/// <summary>
/// A database table's columns.
/// </summary>
public record DatabaseTableDescriptor(
    string Name,
    IReadOnlyList<ColumnDescriptor> Columns);