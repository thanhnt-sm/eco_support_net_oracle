using System;

namespace DataGuard.Contracts;

/// <summary>
/// Parameter direction for a stored procedure parameter (used by ExpectedSpParameter).
/// netstandard2.0-compatible mirror of the engine's direction enum so the IDE
/// analyzer layer never needs a reference to the net9.0 engine assembly.
/// </summary>
public enum ParameterDirection
{
    Input,
    Output,
    InputOutput,
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
    public string? Reason { get; set; }
}

/// <summary>
/// Attribute to declare an expected column for manual ground-truth mode.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ExpectedColumnAttribute : Attribute
{
    public ExpectedColumnAttribute(string columnName, string clrTypeName)
    {
        ColumnName = columnName;
        ClrTypeName = clrTypeName;
    }

    public string ColumnName { get; }
    public string ClrTypeName { get; }
    public bool IsNullable { get; set; }
    public int MaxLength { get; set; }
}

/// <summary>
/// Attribute to declare an expected stored procedure parameter.
/// The direction argument is parsed leniently (invalid values default to Input).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ExpectedSpParameterAttribute : Attribute
{
    public ExpectedSpParameterAttribute(string name, string dbType, string direction)
    {
        Name = name;
        DbType = dbType;
        Direction = Enum.TryParse(direction, true, out ParameterDirection parsed)
            ? parsed
            : ParameterDirection.Input;
    }

    public string Name { get; }
    public string DbType { get; }
    public ParameterDirection Direction { get; set; }
    public int MaxLength { get; set; }
    public byte? Precision { get; set; }
    public byte? Scale { get; set; }
}
