using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace DataGuard.Core.Sources;

/// <summary>
/// Extracts entity contracts from EF Core model.
/// Supports both runtime IModel and design-time ModelSnapshot.cs.
/// </summary>
public class EfModelSource : IContractSource
{
    private readonly DbContext _context;
    private readonly DataGuardConfiguration _config;

    public EfModelSource(DbContext context, DataGuardConfiguration config)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public string SourceId => "ef-model";

    public string DisplayName => "EF Core Model";

    public async Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        var model = _context.Model;
        var entities = new List<EntityDescriptor>();

        foreach (var entityType in model.GetEntityTypes())
        {
            if (_config.ExcludedEntities?.Contains(entityType.ClrType.FullName ?? "") == true)
            {
                continue;
            }

            if (entityType.IsOwned())
            {
                continue;
            }

            var properties = new List<PropertyDescriptor>();

            foreach (var property in entityType.GetProperties())
            {
                if (property.IsShadowProperty())
                {
                    continue;
                }

                string? columnName = null;
                string? columnType = null;
                try
                {
                    columnName = property.GetColumnName();
                    columnType = property.GetColumnType();
                }
                catch (InvalidCastException)
                {
                    // Non-relational providers (e.g. InMemory) throw InvalidCastException
                    // when querying relational extensions. Fall back to property metadata.
                }

                columnName ??= property.Name;
                var maxLength = property.GetMaxLength();
                var isNullable = property.IsNullable;
                var isPrimaryKey = property.IsPrimaryKey();
                var isForeignKey = property.IsForeignKey();
                var isUnicode = property.IsUnicode();
                var valueGenerated = property.ValueGenerated;

                var annotations = property.GetAnnotations()
                    .ToImmutableDictionary(a => a.Name, a => a.Value);

                properties.Add(new PropertyDescriptor(
                    Name: property.Name,
                    ClrTypeName: property.ClrType.FullName ?? property.ClrType.Name,
                    ColumnName: columnName,
                    ColumnType: columnType,
                    IsNullable: isNullable,
                    MaxLength: maxLength,
                    IsPrimaryKey: isPrimaryKey,
                    IsForeignKey: isForeignKey,
                    Annotations: annotations));
            }

            // Get table/schema info
            var tableName = entityType.GetTableName();
            var schema = entityType.GetSchema();
            var viewName = entityType.GetViewName();
            var viewSchema = entityType.GetViewSchema();

            var fullTableName = BuildFullName(schema, tableName!);
            var fullViewName = viewName != null ? BuildFullName(viewSchema, viewName) : null;

            // Get table comments/description
            string? tableComment = null;
            try
            {
                tableComment = entityType.GetComment();
            }
            catch (InvalidOperationException)
            {
                // Read-optimized models throw when querying design-time annotations
            }
            var location = await GetEntityLocationAsync(entityType, cancellationToken);

            entities.Add(new EntityDescriptor(
                Id: $"entity:{entityType.ClrType.FullName}",
                Name: entityType.ClrType.Name,
                ClrTypeName: entityType.ClrType.FullName ?? entityType.ClrType.Name,
                TableName: fullTableName,
                Properties: properties,
                Location: location));
        }

        return entities.Cast<ContractDescriptor>().ToList();
    }

    private static string BuildFullName(string? schema, string name)
        => string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";

    private async Task<Location?> GetEntityLocationAsync(IEntityType entityType, CancellationToken cancellationToken)
    {
        try
        {
            var clrType = entityType.ClrType;
            var syntaxTree = await GetSyntaxTreeForTypeAsync(clrType, cancellationToken);
            if (syntaxTree != null)
            {
                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var classDecl = root.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.ValueText == clrType.Name);

                if (classDecl != null)
                {
                    return Location.Create(syntaxTree, classDecl.Span);
                }
            }
        }
        catch
        {
            // Ignore location errors
        }

        return null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> SourceFileCache = new (StringComparer.Ordinal);

    private static Task<Microsoft.CodeAnalysis.SyntaxTree?> GetSyntaxTreeForTypeAsync(Type type, CancellationToken cancellationToken)
    {
        // Best-effort location resolution: locate the type's source file relative to its
        // assembly and parse it with Roslyn. Returns null when source is unavailable
        // (location is optional metadata). Lookup is cached; bin/obj are excluded.
        var fileName = $"{type.Name}.cs";
        var sourceFile = SourceFileCache.GetOrAdd(fileName, _ => LocateSourceFile(type, fileName));
        if (sourceFile == null)
        {
            return Task.FromResult<Microsoft.CodeAnalysis.SyntaxTree?>(null);
        }

        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile));
        return Task.FromResult<Microsoft.CodeAnalysis.SyntaxTree?>(tree);
    }

    private static string? LocateSourceFile(Type type, string fileName)
    {
        var assemblyDir = Path.GetDirectoryName(type.Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDir))
        {
            return null;
        }

        // Walk up out of bin/obj to the project root, then scan source files once.
        var dir = assemblyDir;
        while (dir != null && new DirectoryInfo(dir).Name is "bin" or "obj")
        {
            dir = Path.GetDirectoryName(dir);
        }

        if (dir == null)
        {
            return null;
        }

        return Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Alternative: Extract from ModelSnapshot.cs file (design-time, no runtime needed).
    /// Parses the EF Core model snapshot JSON structure.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<IReadOnlyList<EntityDescriptor>> ExtractFromModelSnapshotAsync(
        string snapshotFilePath,
        DataGuardConfiguration? config = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(snapshotFilePath))
        {
            return new List<EntityDescriptor>();
        }

        var json = await File.ReadAllTextAsync(snapshotFilePath, cancellationToken);
        return ParseModelSnapshot(json, config);
    }

    /// <summary>
    /// Parses the EF Core ModelSnapshot JSON emitted by the generated DbContext ModelSnapshot file.
    /// </summary>
    public static IReadOnlyList<EntityDescriptor> ParseModelSnapshot(
        string json,
        DataGuardConfiguration? config = null)
    {
        var entities = new List<EntityDescriptor>();

        try
        {
            var jsonNode = JsonNode.Parse(json);
            if (jsonNode == null)
            {
                return entities;
            }

            // EF Core ModelSnapshot has a specific structure
            // Look for the modelBuilder.Entity<...>() calls in the BuildModel method
            var buildModel = FindBuildModelMethod(jsonNode);
            if (buildModel == null)
            {
                return entities;
            }

            var entityConfigs = ExtractEntityConfigurations(buildModel);

            foreach (var entityConfig in entityConfigs)
            {
                var entity = ParseEntityConfiguration(entityConfig, config);
                if (entity != null)
                {
                    entities.Add(entity);
                }
            }
        }
        catch
        {
            // Ignore parse errors - fallback to runtime model
        }

        return entities;
    }

    private static JsonNode? FindBuildModelMethod(JsonNode root)
    {
        // The ModelSnapshot typically has a BuildModel method with modelBuilder.Entity<T>()
        // Navigate the JSON structure to find it
        if (root is JsonObject obj)
        {
            // Look for common patterns in EF Core snapshot JSON
            if (obj.TryGetPropertyValue("BuildModel", out var buildModel))
            {
                return buildModel;
            }

            // Sometimes it's nested
            foreach (var prop in obj)
            {
                if (prop.Value is JsonObject nested && nested.TryGetPropertyValue("BuildModel", out var bm))
                {
                    return bm;
                }
            }
        }

        return root;
    }

    private static IEnumerable<JsonNode> ExtractEntityConfigurations(JsonNode? buildModel)
    {
        var results = new List<JsonNode>();

        if (buildModel is JsonArray array)
        {
            foreach (var item in array)
            {
                if (IsEntityCall(item))
                {
                    results.Add(item!);
                }
                else if (item is JsonObject obj)
                {
                    // Recurse into nested objects
                    results.AddRange(ExtractEntityConfigurationsFromObject(obj));
                }
            }
        }
        else if (buildModel is JsonObject obj)
        {
            results.AddRange(ExtractEntityConfigurationsFromObject(obj));
        }

        return results;
    }

    private static IEnumerable<JsonNode> ExtractEntityConfigurationsFromObject(JsonObject obj)
    {
        var results = new List<JsonNode>();
        foreach (var prop in obj)
        {
            if (prop.Value is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (IsEntityCall(item))
                    {
                        results.Add(item!);
                    }
                    else if (item is JsonObject nested)
                    {
                        results.AddRange(ExtractEntityConfigurationsFromObject(nested));
                    }
                }
            }
            else if (prop.Value is JsonObject nested)
            {
                results.AddRange(ExtractEntityConfigurationsFromObject(nested));
            }
        }

        return results;
    }

    private static bool IsEntityCall(JsonNode? node)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue("Method", out var methodNode))
        {
            var methodName = methodNode?.GetValue<string>() ?? "";
            return methodName == "Entity" || methodName.StartsWith("Entity<", StringComparison.Ordinal);
        }

        return false;
    }

    private static EntityDescriptor? ParseEntityConfiguration(JsonNode entityConfig, DataGuardConfiguration? config)
    {
        try
        {
            // Extract entity type name from generic argument or Type property
            string? clrTypeName = null;
            string? tableName = null;
            string? schema = null;
            var properties = new List<PropertyDescriptor>();

            if (entityConfig is JsonObject obj)
            {
                // Get CLR type from generic argument
                if (obj.TryGetPropertyValue("GenericArguments", out var genArgs) && genArgs is JsonArray args)
                {
                    foreach (var arg in args)
                    {
                        if (arg is JsonObject argObj && argObj.TryGetPropertyValue("Name", out var nameNode))
                        {
                            clrTypeName = nameNode!.GetValue<string>();
                            break;
                        }
                    }
                }

                // Get table name and schema
                if (obj.TryGetPropertyValue("ToTable", out var toTable) && toTable is JsonArray tableArgs)
                {
                    if (tableArgs.Count > 0 && tableArgs[0] is JsonObject tObj)
                    {
                        if (tObj.TryGetPropertyValue("Value", out var tableNameNode))
                        {
                            tableName = tableNameNode!.GetValue<string>();
                        }

                        if (tableArgs.Count > 1 && tableArgs[1] is JsonObject sObj)
                        {
                            if (sObj.TryGetPropertyValue("Value", out var schemaNode))
                            {
                                schema = schemaNode!.GetValue<string>();
                            }
                        }
                    }
                }

                // Parse properties from Property calls
                if (obj.TryGetPropertyValue("Properties", out var props) && props is JsonArray propArray)
                {
                    foreach (var prop in propArray)
                    {
                        var property = ParsePropertyConfiguration(prop!);
                        if (property != null)
                        {
                            properties.Add(property);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(clrTypeName))
            {
                return null;
            }

            if (config?.ExcludedEntities?.Contains(clrTypeName) == true)
            {
                return null;
            }

            var fullTableName = BuildFullName(schema, tableName!);

            return new EntityDescriptor(
                Id: $"entity:{clrTypeName}",
                Name: clrTypeName.Split('.').Last(),
                ClrTypeName: clrTypeName,
                TableName: fullTableName,
                Properties: properties,
                Location: null);
        }
        catch
        {
            return null;
        }
    }

    private static PropertyDescriptor? ParsePropertyConfiguration(JsonNode propConfig)
    {
        try
        {
            if (propConfig is not JsonObject obj)
            {
                return null;
            }

            string? name = null;
            string? clrType = null;
            string? columnName = null;
            string? columnType = null;
            bool isNullable = true;
            int? maxLength = null;
            bool isPrimaryKey = false;
            bool isForeignKey = false;
            var annotations = ImmutableDictionary<string, object?>.Empty;

            if (obj.TryGetPropertyValue("Name", out var nameNode))
            {
                name = nameNode!.GetValue<string>();
            }

            if (obj.TryGetPropertyValue("ClrType", out var clrTypeNode))
            {
                clrType = clrTypeNode!.GetValue<string>();
            }

            // Parse HasColumnName, HasColumnType, IsRequired, HasMaxLength, etc.
            if (obj.TryGetPropertyValue("Calls", out var calls) && calls is JsonArray callArray)
            {
                foreach (var call in callArray)
                {
                    if (call is JsonObject callObj)
                    {
                        if (callObj.TryGetPropertyValue("Method", out var methodNode))
                        {
                            var method = methodNode!.GetValue<string>() ?? "";
                            switch (method)
                            {
                                case "HasColumnName":
                                    {
                                        if (callObj.TryGetPropertyValue("Arguments", out var hcnArgs) && hcnArgs is JsonArray hcnArr && hcnArr.Count > 0)
                                        {
                                            columnName = hcnArr[0]!.GetValue<string>();
                                        }

                                        break;
                                    }

                                case "HasColumnType":
                                    {
                                        if (callObj.TryGetPropertyValue("Arguments", out var hctArgs) && hctArgs is JsonArray hctArr && hctArr.Count > 0)
                                        {
                                            columnType = hctArr[0]!.GetValue<string>();
                                        }

                                        break;
                                    }

                                case "IsRequired":
                                    {
                                        if (callObj.TryGetPropertyValue("Arguments", out var irArgs) && irArgs is JsonArray irArr && irArr.Count > 0)
                                        {
                                            isNullable = !irArr[0]!.GetValue<bool>();
                                        }
                                        else
                                        {
                                            isNullable = false;
                                        }

                                        break;
                                    }

                                case "HasMaxLength":
                                    {
                                        if (callObj.TryGetPropertyValue("Arguments", out var hmlArgs) && hmlArgs is JsonArray hmlArr && hmlArr.Count > 0)
                                        {
                                            maxLength = hmlArr[0]!.GetValue<int?>();
                                        }

                                        break;
                                    }

                                case "IsPrimaryKey":
                                    isPrimaryKey = true;
                                    break;
                                case "IsForeignKey":
                                    isForeignKey = true;
                                    break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(clrType))
            {
                return null;
            }

            return new PropertyDescriptor(
                Name: name,
                ClrTypeName: clrType,
                ColumnName: columnName,
                ColumnType: columnType,
                IsNullable: isNullable,
                MaxLength: maxLength,
                IsPrimaryKey: isPrimaryKey,
                IsForeignKey: isForeignKey,
                Annotations: annotations);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create EF Model Source from DbContext options (for design-time services).
    /// </summary>
    /// <returns></returns>
    public static EfModelSource CreateFromOptions(
        DbContextOptions options,
        DataGuardConfiguration config)
    {
        var context = Activator.CreateInstance(options.ContextType, options) as DbContext
            ?? throw new InvalidOperationException("Failed to create DbContext instance");
        return new EfModelSource(context, config);
    }

    /// <summary>
    /// Create EF Model Source using IDesignTimeServices for design-time model extraction.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<IReadOnlyList<EntityDescriptor>> ExtractFromDesignTimeAsync(
        string projectPath,
        string contextTypeName,
        DataGuardConfiguration? config = null,
        CancellationToken cancellationToken = default)
    {
        // 1. ModelSnapshot parsing (fast, no build required).
        var snapshotPath = FindModelSnapshot(projectPath, contextTypeName);
        if (snapshotPath != null)
        {
            var entities = await ExtractFromModelSnapshotAsync(snapshotPath, config, cancellationToken);
            if (entities.Count > 0)
            {
                return entities;
            }

            // Snapshot parsing produced no entities - fall through to the built assembly.
        }

        // 2. Fallback: read the EF model from an already-built assembly.
        return await ExtractFromBuiltAssemblyAsync(projectPath, contextTypeName, config, cancellationToken);
    }

    private static async Task<IReadOnlyList<EntityDescriptor>> ExtractFromBuiltAssemblyAsync(
        string projectPath,
        string contextTypeName,
        DataGuardConfiguration? config,
        CancellationToken cancellationToken)
    {
        var outputDir = Path.Combine(projectPath, "bin");
        if (!Directory.Exists(outputDir))
        {
            return new List<EntityDescriptor>();
        }

        foreach (var dll in Directory.GetFiles(outputDir, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var contextType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == contextTypeName && typeof(DbContext).IsAssignableFrom(t));
                if (contextType == null)
                {
                    continue;
                }

                var context = (DbContext?)Activator.CreateInstance(contextType);
                if (context == null)
                {
                    continue;
                }

                using (context)
                {
                    var source = new EfModelSource(context, config ?? new DataGuardConfiguration());
                    var contracts = await source.ExtractContractsAsync(cancellationToken);
                    return contracts.OfType<EntityDescriptor>().ToList();
                }
            }
            catch
            {
                // Skip assemblies that fail to load or instantiate.
            }
        }

        return new List<EntityDescriptor>();
    }

    private static string? FindModelSnapshot(string projectPath, string contextTypeName)
    {
        var migrationsDir = Path.Combine(projectPath, "Migrations");
        if (!Directory.Exists(migrationsDir))
        {
            return null;
        }

        var snapshotFiles = Directory.GetFiles(migrationsDir, "*ModelSnapshot.cs");

        // Find the one matching our context
        foreach (var file in snapshotFiles)
        {
            var content = File.ReadAllText(file);
            if (content.Contains(contextTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return snapshotFiles.FirstOrDefault();
    }
}