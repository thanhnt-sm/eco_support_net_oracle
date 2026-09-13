using System.Reflection;
using DataGuard.Contracts;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Sources;

/// <summary>
/// Manual ground-truth source: reads [ExpectedColumn] / [ExpectedSpParameter]
/// attributes from a compiled user assembly (reflection, zero database access).
/// </summary>
public sealed class ManualContractSource : IContractSource
{
    private readonly string _assemblyPath;

    public ManualContractSource(string assemblyPath)
    {
        _assemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
    }

    public string SourceId => "manual";

    public string DisplayName => "Manual Attributes";

    public Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        var assembly = Assembly.LoadFrom(_assemblyPath);
        var contracts = new List<ContractDescriptor>();

        foreach (var type in assembly.GetTypes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var declaredContract = type.GetCustomAttribute<DataContractAttribute>();

            var properties = new List<PropertyDescriptor>();
            foreach (var prop in type.GetProperties())
            {
                var expectedColumns = prop.GetCustomAttributes<ExpectedColumnAttribute>().ToList();
                foreach (var expected in expectedColumns)
                {
                    properties.Add(new PropertyDescriptor(
                        Name: prop.Name,
                        ClrTypeName: expected.ClrTypeName ?? prop.PropertyType.Name,
                        ColumnName: expected.ColumnName,
                        ColumnType: null,
                        IsNullable: expected.IsNullable,
                        MaxLength: expected.MaxLength > 0 ? expected.MaxLength : (int?)null,
                        IsPrimaryKey: false,
                        IsForeignKey: false,
                        Annotations: null));
                }

                if (declaredContract is not null && expectedColumns.Count == 0)
                {
                    properties.Add(new PropertyDescriptor(
                        Name: prop.Name,
                        ClrTypeName: prop.PropertyType.Name,
                        ColumnName: prop.Name,
                        ColumnType: null,
                        IsNullable: !prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) is not null,
                        MaxLength: null,
                        IsPrimaryKey: false,
                        IsForeignKey: false,
                        Annotations: null));
                }
            }

            if (properties.Count > 0)
            {
                contracts.Add(new EntityDescriptor(
                    Id: $"manual-entity:{type.FullName}",
                    Name: type.Name,
                    ClrTypeName: type.FullName ?? type.Name,
                    TableName: declaredContract?.TableName ?? type.Name,
                    Properties: properties,
                    Location: null));
            }

            foreach (var method in type.GetMethods())
            {
                var expectedParams = method.GetCustomAttributes<ExpectedSpParameterAttribute>().ToList();
                var declaredParams = method.GetParameters()
                    .Select(parameter => (Parameter: parameter, Attribute: parameter.GetCustomAttribute<SqlParameterAttribute>()))
                    .Where(entry => entry.Attribute is not null)
                    .Select(entry => new ParameterDescriptor(
                        Name: entry.Attribute!.Name ?? entry.Parameter.Name ?? "parameter",
                        DataType: entry.Attribute.DbType ?? entry.Parameter.ParameterType.Name,
                        Direction: ToCoreDirection(entry.Attribute.Direction),
                        MaxLength: entry.Attribute.MaxLength > 0 ? entry.Attribute.MaxLength : (int?)null,
                        Precision: entry.Attribute.Precision,
                        Scale: entry.Attribute.Scale,
                        IsNullable: true,
                        OrdinalPosition: entry.Parameter.Position,
                        ClrType: entry.Parameter.ParameterType.Name))
                    .ToList();
                var declaredResults = method.GetCustomAttributes<ResultSetAttribute>()
                    .Select((result, index) => new ColumnDescriptor(
                        Name: result.ColumnName,
                        DataType: result.ClrTypeName,
                        MaxLength: result.MaxLength > 0 ? result.MaxLength : null,
                        Precision: null,
                        Scale: null,
                        IsNullable: result.IsNullable,
                        CharUsed: null,
                        ColumnId: index))
                    .ToList();
                if (expectedParams.Count == 0 && declaredParams.Count == 0 && declaredResults.Count == 0)
                {
                    continue;
                }

                var parameters = expectedParams.Select(p => new ParameterDescriptor(
                    Name: p.Name,
                    DataType: p.DbType,
                    Direction: ToCoreDirection(p.Direction),
                    MaxLength: p.MaxLength > 0 ? p.MaxLength : (int?)null,
                    Precision: p.Precision > 0 ? (byte?)p.Precision : null,
                    Scale: p.Scale >= 0 ? (byte?)p.Scale : null,
                    IsNullable: true,
                    OrdinalPosition: 0,
                    ClrType: p.ClrType)).ToList();
                parameters.AddRange(declaredParams);

                contracts.Add(new StoredProcedureDescriptor(
                    Id: $"manual-sp:{type.FullName}.{method.Name}",
                    Name: method.Name,
                    Schema: string.Empty,
                    PackageName: "",
                    Parameters: parameters,
                    ResultColumns: declaredResults,
                    ReturnsRefCursor: false,
                    Location: null));
            }
        }

        return Task.FromResult<IReadOnlyList<ContractDescriptor>>(contracts);
    }

    private static Abstractions.ParameterDirection ToCoreDirection(Contracts.ParameterDirection direction) => direction switch
    {
        Contracts.ParameterDirection.Output => Abstractions.ParameterDirection.Output,
        Contracts.ParameterDirection.InputOutput => Abstractions.ParameterDirection.InputOutput,
        Contracts.ParameterDirection.ReturnValue => Abstractions.ParameterDirection.ReturnValue,
        _ => Abstractions.ParameterDirection.Input,
    };
}
