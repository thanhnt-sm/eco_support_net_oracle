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

            var properties = new List<PropertyDescriptor>();
            foreach (var prop in type.GetProperties())
            {
                foreach (var expected in prop.GetCustomAttributes<ExpectedColumnAttribute>())
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
            }

            if (properties.Count > 0)
            {
                contracts.Add(new EntityDescriptor(
                    Id: $"manual-entity:{type.FullName}",
                    Name: type.Name,
                    ClrTypeName: type.FullName ?? type.Name,
                    TableName: null,
                    Properties: properties,
                    Location: null));
            }

            foreach (var method in type.GetMethods())
            {
                var expectedParams = method.GetCustomAttributes<ExpectedSpParameterAttribute>().ToList();
                if (expectedParams.Count == 0)
                    continue;

                var parameters = expectedParams.Select(p => new ParameterDescriptor(
                    Name: p.Name,
                    DataType: p.DbType,
                    Direction: p.Direction switch
                    {
                        Contracts.ParameterDirection.Output => Abstractions.ParameterDirection.Output,
                        Contracts.ParameterDirection.InputOutput => Abstractions.ParameterDirection.InputOutput,
                        Contracts.ParameterDirection.ReturnValue => Abstractions.ParameterDirection.ReturnValue,
                        _ => Abstractions.ParameterDirection.Input
                    },
                    MaxLength: p.MaxLength > 0 ? p.MaxLength : (int?)null,
                    Precision: p.Precision > 0 ? (byte?)p.Precision : null,
                    Scale: p.Scale >= 0 ? (byte?)p.Scale : null,
                    IsNullable: true,
                    OrdinalPosition: 0)).ToList();

                contracts.Add(new StoredProcedureDescriptor(
                    Id: $"manual-sp:{type.FullName}.{method.Name}",
                    Name: method.Name,
                    Schema: null,
                    PackageName: "",
                    Parameters: parameters,
                    ResultColumns: new List<ColumnDescriptor>(),
                    ReturnsRefCursor: false,
                    Location: null));
            }
        }

        return Task.FromResult<IReadOnlyList<ContractDescriptor>>(contracts);
    }
}
