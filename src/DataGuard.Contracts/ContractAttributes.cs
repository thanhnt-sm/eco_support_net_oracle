// <copyright file="ContractAttributes.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Contracts;

using System;

/// <summary>
/// Parameter direction for a stored procedure parameter (used by ExpectedSpParameter).
/// netstandard2.0-compatible mirror of the engine's direction enum so the IDE
/// analyzer layer never needs a reference to the net9.0 engine assembly.
/// </summary>
public enum ParameterDirection
{
    /// <summary>Input-only parameter (default).</summary>
    Input,

    /// <summary>Output parameter (call site uses out).</summary>
    Output,

    /// <summary>Input/output parameter (call site uses ref).</summary>
    InputOutput,

    /// <summary>Function return value.</summary>
    ReturnValue,
}

/// <summary>
/// Declares that a CLR type participates in a manual DataGuard contract.
/// This compatibility attribute is intentionally in the DataGuard namespace so
/// consumers can qualify it and avoid collisions with
/// <c>System.Runtime.Serialization.DataContractAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DataContractAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DataContractAttribute"/> class using the CLR type name as the table name.</summary>
    public DataContractAttribute()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DataContractAttribute"/> class with an explicit table name.</summary>
    /// <param name="tableName">Database table name.</param>
    public DataContractAttribute(string tableName)
    {
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
    }

    /// <summary>Gets the optional database table name.</summary>
    public string? TableName { get; }

    /// <summary>Gets or sets the optional database schema.</summary>
    public string? Schema { get; set; }
}

/// <summary>
/// Declares one stored-procedure parameter on a method parameter for manual
/// contract extraction. It is an additive facade for
/// <see cref="ExpectedSpParameterAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class SqlParameterAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="SqlParameterAttribute"/> class using its CLR name and type.</summary>
    public SqlParameterAttribute()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SqlParameterAttribute"/> class with an explicit database name and type.</summary>
    /// <param name="name">Database parameter name.</param>
    /// <param name="dbType">Database type name.</param>
    public SqlParameterAttribute(string name, string dbType)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DbType = dbType ?? throw new ArgumentNullException(nameof(dbType));
    }

    /// <summary>Gets or sets the database parameter name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the database type name.</summary>
    public string? DbType { get; set; }

    /// <summary>Gets or sets the expected parameter direction.</summary>
    public ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <summary>Gets or sets maximum parameter length (0 = unspecified).</summary>
    public int MaxLength { get; set; }

    /// <summary>Gets or sets expected numeric precision.</summary>
    public byte? Precision { get; set; }

    /// <summary>Gets or sets expected numeric scale.</summary>
    public byte? Scale { get; set; }
}

/// <summary>
/// Declares one result-set column on a method for manual contract extraction.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ResultSetAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ResultSetAttribute"/> class.</summary>
    /// <param name="columnName">Database result column name.</param>
    /// <param name="clrTypeName">Expected CLR type name.</param>
    public ResultSetAttribute(string columnName, string clrTypeName)
    {
        ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
        ClrTypeName = clrTypeName ?? throw new ArgumentNullException(nameof(clrTypeName));
    }

    /// <summary>Gets the database result column name.</summary>
    public string ColumnName { get; }

    /// <summary>Gets the expected CLR type name.</summary>
    public string ClrTypeName { get; }

    /// <summary>Gets or sets whether the result column allows NULL.</summary>
    public bool IsNullable { get; set; }

    /// <summary>Gets or sets maximum result-column length (0 = unspecified).</summary>
    public int MaxLength { get; set; }
}

/// <summary>
/// Attribute to skip contract validation for dynamic SQL or complex cases.
/// Lives in the netstandard2.0 contracts assembly so quick-fixes can emit it
/// into any consumer project.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SkipContractCheckAttribute : Attribute
{
    /// <summary>Gets or sets optional reason the check was skipped (shown in diagnostics).</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Attribute to declare an expected column for manual ground-truth mode.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ExpectedColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExpectedColumnAttribute"/> class.
    /// Initializes an expected-column declaration.
    /// </summary>
    /// <param name="columnName">Database column name.</param>
    /// <param name="clrTypeName">Expected CLR type name.</param>
    public ExpectedColumnAttribute(string columnName, string clrTypeName)
    {
        this.ColumnName = columnName;
        this.ClrTypeName = clrTypeName;
    }

    /// <summary>Gets expected database column name.</summary>
    public string ColumnName { get; }

    /// <summary>Gets cLR type name of the expected value.</summary>
    public string ClrTypeName { get; }

    /// <summary>Gets or sets a value indicating whether whether the database column allows NULL.</summary>
    public bool IsNullable { get; set; }

    /// <summary>Gets or sets maximum length of the column (0 = unspecified).</summary>
    public int MaxLength { get; set; }
}

/// <summary>
/// Attribute to declare an expected stored procedure parameter.
/// The direction argument is parsed leniently (invalid values default to Input).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ExpectedSpParameterAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExpectedSpParameterAttribute"/> class.
    /// Initializes an expected stored-procedure parameter declaration.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="dbType">Expected database type name.</param>
    /// <param name="direction">Expected parameter direction.</param>
    public ExpectedSpParameterAttribute(string name, string dbType, string direction)
    {
        this.Name = name;
        this.DbType = dbType;
        this.Direction = Enum.TryParse(direction, true, out ParameterDirection parsed)
            ? parsed
            : ParameterDirection.Input;
    }

    /// <summary>Gets expected parameter name.</summary>
    public string Name { get; }

    /// <summary>Gets expected database type name.</summary>
    public string DbType { get; }

    /// <summary>Gets or sets expected parameter direction.</summary>
    public ParameterDirection Direction { get; set; }

    /// <summary>Gets or sets maximum parameter length (0 = unspecified).</summary>
    public int MaxLength { get; set; }

    /// <summary>Gets or sets expected numeric precision, if applicable.</summary>
    public byte? Precision { get; set; }

    /// <summary>Gets or sets expected numeric scale, if applicable.</summary>
    public byte? Scale { get; set; }

    /// <summary>Gets or sets expected CLR type name (e.g. "int", "string"); enables DG002 type-compatibility checks. Null = unknown (rule skipped).</summary>
    public string? ClrType { get; set; }
}
