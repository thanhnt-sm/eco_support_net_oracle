using System;

namespace DataGuard.Contracts;

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
    ReturnValue
}

/// <summary>
/// Attribute to skip contract validation for dynamic SQL or complex cases.
/// Lives in the netstandard2.0 contracts assembly so quick-fixes can emit it
/// into any consumer project.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SkipContractCheckAttribute : Attribute
{
    /// <summary>Optional reason the check was skipped (shown in diagnostics).</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Attribute to declare an expected column for manual ground-truth mode.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ExpectedColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes an expected-column declaration.
    /// </summary>
    /// <param name="columnName">Database column name.</param>
    /// <param name="clrTypeName">Expected CLR type name.</param>
    public ExpectedColumnAttribute(string columnName, string clrTypeName)
    {
        ColumnName = columnName;
        ClrTypeName = clrTypeName;
    }

    /// <summary>Expected database column name.</summary>
    public string ColumnName { get; }
    /// <summary>CLR type name of the expected value.</summary>
    public string ClrTypeName { get; }
    /// <summary>Whether the database column allows NULL.</summary>
    public bool IsNullable { get; set; }
    /// <summary>Maximum length of the column (0 = unspecified).</summary>
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
    /// Initializes an expected stored-procedure parameter declaration.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="dbType">Expected database type name.</param>
    /// <param name="direction">Expected parameter direction.</param>
    public ExpectedSpParameterAttribute(string name, string dbType, string direction)
    {
        Name = name;
        DbType = dbType;
        Direction = Enum.TryParse(direction, true, out ParameterDirection parsed)
            ? parsed
            : ParameterDirection.Input;
    }

    /// <summary>Expected parameter name.</summary>
    public string Name { get; }
    /// <summary>Expected database type name.</summary>
    public string DbType { get; }
    /// <summary>Expected parameter direction.</summary>
    public ParameterDirection Direction { get; set; }
    /// <summary>Maximum parameter length (0 = unspecified).</summary>
    public int MaxLength { get; set; }
    /// <summary>Expected numeric precision, if applicable.</summary>
    public byte? Precision { get; set; }
    /// <summary>Expected numeric scale, if applicable.</summary>
    public byte? Scale { get; set; }
}
