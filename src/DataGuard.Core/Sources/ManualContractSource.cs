using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using DataGuard.Contracts;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Sources;

/// <summary>
/// Manual ground-truth source: reads [ExpectedColumn] / [ExpectedSpParameter] (and the DataGuard.Contracts
/// [DataContract] / [SqlParameter] / [ResultSet]) attributes from a compiled user assembly with zero database access.
/// The assembly is inspected through <see cref="MetadataLoadContext"/>: its metadata is read, no code from it runs
/// (no module initializer, static constructor or attribute constructor), and it is never loaded into the default
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
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
        var assemblyPath = Path.GetFullPath(_assemblyPath);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Manual-mode assembly not found: {assemblyPath}", assemblyPath);
        }

        using var context = new MetadataLoadContext(new PathAssemblyResolver(ResolverPaths(assemblyPath)));
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        var contracts = new List<ContractDescriptor>();

        foreach (var type in GetLoadableTypes(assembly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var declaredContract = Read<DataContractAttribute>(type.GetCustomAttributesData()).FirstOrDefault();

            var properties = new List<PropertyDescriptor>();
            foreach (var prop in type.GetProperties())
            {
                var expectedColumns = Read<ExpectedColumnAttribute>(prop.GetCustomAttributesData()).ToList();
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
                        IsNullable: !prop.PropertyType.IsValueType || IsNullableValueType(prop.PropertyType),
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
                var expectedParams = Read<ExpectedSpParameterAttribute>(method.GetCustomAttributesData()).ToList();
                var declaredParams = method.GetParameters()
                    .Select(parameter => (Parameter: parameter, Attribute: Read<SqlParameterAttribute>(parameter.GetCustomAttributesData()).FirstOrDefault()))
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
                var declaredResults = Read<ResultSetAttribute>(method.GetCustomAttributesData())
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

    /// <summary>
    /// Resolver candidates: the target assembly and its directory (its dependencies), the running framework, and the
    /// trusted DataGuard.Contracts assembly. Only metadata is read from any of them.
    /// </summary>
    private static IEnumerable<string> ResolverPaths(string assemblyPath)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { assemblyPath };
        var directory = Path.GetDirectoryName(assemblyPath);
        if (!string.IsNullOrEmpty(directory))
        {
            paths.UnionWith(Directory.EnumerateFiles(directory, "*.dll"));
        }

        paths.UnionWith(Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));
        var contractsPath = typeof(ExpectedColumnAttribute).Assembly.Location;
        if (!string.IsNullOrEmpty(contractsPath))
        {
            paths.Add(contractsPath);
        }

        return paths;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>();
        }
    }

    private static bool IsNullableValueType(Type type) =>
        type.IsGenericType && string.Equals(type.GetGenericTypeDefinition().FullName, "System.Nullable`1", StringComparison.Ordinal);

    /// <summary>
    /// Rebuilds DataGuard.Contracts attributes from their metadata (constructor and named arguments). The attribute types
    /// are DataGuard's own, so the instances come from the trusted Contracts assembly, never from the inspected one.
    /// </summary>
    private static IEnumerable<T> Read<T>(IEnumerable<CustomAttributeData> attributes)
        where T : Attribute
    {
        var fullName = typeof(T).FullName;
        foreach (var data in attributes)
        {
            if (!string.Equals(data.AttributeType.FullName, fullName, StringComparison.Ordinal))
            {
                continue;
            }

            var parameterTypes = data.Constructor.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray();
            var constructor = typeof(T).GetConstructors()
                .FirstOrDefault(candidate => candidate.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameterTypes));
            if (constructor is null)
            {
                continue;
            }

            var runtimeParameters = constructor.GetParameters();
            var arguments = data.ConstructorArguments
                .Select((argument, index) => ConvertArgument(argument, runtimeParameters[index].ParameterType))
                .ToArray();
            var instance = (T)constructor.Invoke(arguments);
            foreach (var named in data.NamedArguments)
            {
                var property = typeof(T).GetProperty(named.MemberName);
                if (property is { CanWrite: true })
                {
                    property.SetValue(instance, ConvertArgument(named.TypedValue, property.PropertyType));
                }
            }

            yield return instance;
        }
    }

    private static object? ConvertArgument(CustomAttributeTypedArgument argument, Type targetType)
    {
        if (argument.Value is null || argument.Value is ReadOnlyCollection<CustomAttributeTypedArgument>)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsEnum)
        {
            return Enum.ToObject(underlying, argument.Value);
        }

        return underlying.IsInstanceOfType(argument.Value) ? argument.Value : Convert.ChangeType(argument.Value, underlying, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Abstractions.ParameterDirection ToCoreDirection(Contracts.ParameterDirection direction) => direction switch
    {
        Contracts.ParameterDirection.Output => Abstractions.ParameterDirection.Output,
        Contracts.ParameterDirection.InputOutput => Abstractions.ParameterDirection.InputOutput,
        Contracts.ParameterDirection.ReturnValue => Abstractions.ParameterDirection.ReturnValue,
        _ => Abstractions.ParameterDirection.Input,
    };
}
