using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>
/// Extracts raw SQL query contracts and their expected C# target shapes directly from C# source projects.
/// Scans for Dapper (Query, Execute, etc.) and EF Core (FromSqlRaw, ExecuteSqlRaw, etc.) invocations.
/// </summary>
public sealed class ProjectCSharpSqlSource : IContractSource
{
    private static readonly HashSet<string> TargetMethodNames = new(StringComparer.Ordinal)
    {
        "Query", "QueryAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "QueryMultiple", "QueryMultipleAsync",
        "Execute", "ExecuteAsync", "ExecuteScalar", "ExecuteScalarAsync",
        "ExecuteReader", "ExecuteReaderAsync",
        "ExecuteNonQuery", "ExecuteNonQueryAsync",
        "FromSqlRaw", "FromSqlInterpolated", "FromSql",
        "ExecuteSqlRaw", "ExecuteSqlRawAsync", "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync",
    };

    private static readonly Regex SqlKeywordRegex = new(
        @"\b(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM|EXEC|EXECUTE|MERGE|WITH|BEGIN)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ParameterRegex = new(
        @"(?:@([A-Za-z_][\w]*)|:([A-Za-z_][\w]*)|\$(\d+))",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex SqlStringLiteralRegex = new(
        @"'(''|[^'])*'",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex SelectOpRegex = new(@"\bSELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex InsertOpRegex = new(@"\bINSERT\s+INTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex UpdateOpRegex = new(@"\bUPDATE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex DeleteOpRegex = new(@"\bDELETE\s+FROM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex MergeOpRegex = new(@"\bMERGE\s+INTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex JoinOpRegex = new(@"\b(INNER|LEFT|RIGHT|FULL|CROSS)?\s*JOIN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex[] TablePatterns = new[]
    {
        new Regex(@"\bFROM\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bJOIN\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bINTO\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bUPDATE\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bTABLE\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
    };

    private static readonly string SpacePadding = new(' ', 512);

    private readonly string _projectOrPath;
    private readonly ProgressEmitter? _progress;

    public ProjectCSharpSqlSource(string projectOrPath, ProgressEmitter? progress = null)
    {
        _projectOrPath = projectOrPath ?? throw new ArgumentNullException(nameof(projectOrPath));
        _progress = progress;
    }

    public string SourceId => "project-csharp-sql";

    public string DisplayName => "C# Project SQL Source";

    public Task<IReadOnlyList<ContractDescriptor>> ExtractContractsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var csFiles = DiscoverSourceFiles(_projectOrPath);
        if (csFiles.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ContractDescriptor>>(Array.Empty<ContractDescriptor>());
        }

        var syntaxTrees = new List<SyntaxTree>();
        foreach (var file in csFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = File.ReadAllText(file);
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(text, path: file, cancellationToken: cancellationToken));
            }
            catch (Exception)
            {
                // Non-fatal if a single file cannot be read
            }
        }

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location) && File.Exists(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "DataGuard_SourceAnalysis",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Index all declared types across the project's syntax trees for syntax-fallback extraction
        var syntaxTypes = IndexSyntaxTypes(syntaxTrees, cancellationToken);

        var descriptors = new List<ContractDescriptor>();
        var seenSql = new HashSet<string>(StringComparer.Ordinal);

        void AddDescriptor(
            string sqlText,
            Location location,
            string? targetTypeName,
            IReadOnlyList<PropertyDescriptor> expectedProperties,
            string? providerHint)
        {
            var lineSpan = location.GetLineSpan();
            var filePath = lineSpan.Path;
            var lineNumber = lineSpan.StartLinePosition.Line + 1;
            var fileName = Path.GetFileName(filePath);

            var key = $"{filePath}:{lineNumber}:{sqlText.Trim()}";
            if (!seenSql.Add(key))
            {
                return;
            }

            var opType = ClassifySqlOperation(sqlText);
            var tables = ExtractReferencedTables(sqlText);
            var targetDisplay = string.IsNullOrEmpty(targetTypeName) ? "untyped" : targetTypeName;
            var detail = $"Found SQL in {fileName}:{lineNumber} targeting {targetDisplay}";

            _progress?.Emit(new ProgressEvent(
                ProgressEventKind.ContractDiscovered,
                "Acquiring contracts",
                detail,
                new Dictionary<string, object?>
                {
                    ["Operation"] = opType.ToString(),
                    ["Tables"] = tables,
                    ["ProviderHint"] = providerHint,
                }));

            Console.WriteLine($"[INFO] {detail}");

            var parameters = ExtractParameters(sqlText);

            var descriptor = new RawSqlDescriptor(
                Id: $"project-sql:{fileName}:{lineNumber}",
                SqlText: sqlText,
                Parameters: parameters,
                ResultColumns: Array.Empty<ColumnDescriptor>(),
                Location: location,
                ExpectedProperties: expectedProperties,
                TargetTypeName: targetTypeName,
                OperationType: opType,
                ReferencedTables: tables,
                ConnectionProviderHint: providerHint);

            descriptors.Add(descriptor);
        }

        foreach (var tree in syntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

            // 1. Invocations (Query<T>, Execute, FromSqlRaw, ExecuteReader, ExecuteNonQuery, etc.)
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCandidateInvocation(invocation, out var methodName))
                {
                    continue;
                }

                var sqlText = ExtractSqlText(invocation, methodName, semanticModel, cancellationToken);
                if (string.IsNullOrWhiteSpace(sqlText))
                {
                    continue;
                }

                var (targetTypeName, typeSymbol) = ResolveTargetType(invocation, semanticModel, cancellationToken);

                IReadOnlyList<PropertyDescriptor> expectedProperties = Array.Empty<PropertyDescriptor>();
                if (typeSymbol != null)
                {
                    expectedProperties = ExtractPropertiesFromSymbol(typeSymbol);
                }

                if (expectedProperties.Count == 0 && !string.IsNullOrEmpty(targetTypeName) && syntaxTypes.TryGetValue(targetTypeName, out var typeDecl))
                {
                    expectedProperties = ExtractPropertiesFromSyntax(typeDecl);
                }

                string? providerHint = null;
                if (invocation.Expression is MemberAccessExpressionSyntax ma)
                {
                    var receiverType = semanticModel.GetTypeInfo(ma.Expression, cancellationToken).Type?.Name
                        ?? (ma.Expression as IdentifierNameSyntax)?.Identifier.ValueText;
                    providerHint = InferProviderHint(receiverType);
                }

                AddDescriptor(sqlText, invocation.GetLocation(), targetTypeName, expectedProperties, providerHint);
            }

            // 2. CommandText assignments (cmd.CommandText = "SELECT ...")
            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var leftName = assignment.Left switch
                {
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                    IdentifierNameSyntax id => id.Identifier.ValueText,
                    _ => null,
                };

                if (leftName is not "CommandText")
                {
                    continue;
                }

                var sqlText = TryResolveString(assignment.Right, semanticModel, cancellationToken);
                if (string.IsNullOrWhiteSpace(sqlText) || !IsSqlString(sqlText))
                {
                    continue;
                }

                string? providerHint = null;
                if (assignment.Left is MemberAccessExpressionSyntax maExpr)
                {
                    var receiverType = semanticModel.GetTypeInfo(maExpr.Expression, cancellationToken).Type?.Name
                        ?? (maExpr.Expression as IdentifierNameSyntax)?.Identifier.ValueText;
                    providerHint = InferProviderHint(receiverType);
                }

                AddDescriptor(sqlText, assignment.GetLocation(), null, Array.Empty<PropertyDescriptor>(), providerHint);
            }

            // 3. Object creations (new SqlCommand("SELECT ...", conn) / new OracleCommand(...) / new NpgsqlCommand(...))
            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var typeName = creation.Type.ToString();
                if (!typeName.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (creation.ArgumentList == null || creation.ArgumentList.Arguments.Count == 0)
                {
                    continue;
                }

                var firstArg = creation.ArgumentList.Arguments[0].Expression;
                var sqlText = TryResolveString(firstArg, semanticModel, cancellationToken);
                if (string.IsNullOrWhiteSpace(sqlText) || !IsSqlString(sqlText))
                {
                    continue;
                }

                var providerHint = InferProviderHint(typeName);
                AddDescriptor(sqlText, creation.GetLocation(), null, Array.Empty<PropertyDescriptor>(), providerHint);
            }

            // 4. Base repository constructor calls (base("CUSTOMERS", ...))
            foreach (var init in root.DescendantNodes().OfType<ConstructorInitializerSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!init.IsKind(SyntaxKind.BaseConstructorInitializer) || init.ArgumentList == null || init.ArgumentList.Arguments.Count == 0)
                {
                    continue;
                }

                var arg0 = init.ArgumentList.Arguments[0].Expression;
                var resolved = TryResolveString(arg0, semanticModel, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolved) && !resolved.Contains(' ') && resolved.Length > 1 && !resolved.StartsWith("sp_", StringComparison.OrdinalIgnoreCase))
                {
                    var sqlText = $"SELECT * FROM {resolved}";
                    AddDescriptor(sqlText, init.GetLocation(), null, Array.Empty<PropertyDescriptor>(), null);
                }
            }
        }
        return Task.FromResult<IReadOnlyList<ContractDescriptor>>(descriptors);
    }

    private static List<string> DiscoverSourceFiles(string path)
    {
        var files = new List<string>();

        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".cs")
            {
                files.Add(path);
                return files;
            }

            if (ext is ".csproj" or ".sln")
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    ScanDirectory(dir, files);
                }
                return files;
            }
        }

        if (Directory.Exists(path))
        {
            ScanDirectory(path, files);
        }

        return files;
    }

    private static void ScanDirectory(string rootDir, List<string> files)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(rootDir, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/bin/") ||
                    normalized.Contains("/obj/") ||
                    normalized.Contains("/.git/") ||
                    normalized.Contains("/.vs/"))
                {
                    continue;
                }

                files.Add(file);
            }
        }
        catch (Exception)
        {
            // Ignore restricted access
        }
    }

    private static Dictionary<string, TypeDeclarationSyntax> IndexSyntaxTypes(
        IEnumerable<SyntaxTree> trees,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, TypeDeclarationSyntax>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            var root = tree.GetRoot(cancellationToken);
            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var name = typeDecl.Identifier.ValueText;
                result[name] = typeDecl;
            }
        }

        return result;
    }

    private static bool IsCandidateInvocation(InvocationExpressionSyntax invocation, out string methodName)
    {
        methodName = string.Empty;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name switch
            {
                GenericNameSyntax g => g.Identifier.ValueText,
                SimpleNameSyntax s => s.Identifier.ValueText,
                _ => string.Empty,
            };
        }
        else if (invocation.Expression is GenericNameSyntax generic)
        {
            methodName = generic.Identifier.ValueText;
        }
        else if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            methodName = identifier.Identifier.ValueText;
        }

        return TargetMethodNames.Contains(methodName);
    }

    private static string ExtractSqlText(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var isEfFromSql = methodName.StartsWith("FromSql", StringComparison.Ordinal) ||
                          methodName.StartsWith("ExecuteSql", StringComparison.Ordinal);

        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            var resolved = TryResolveString(arg.Expression, semanticModel, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                if (isEfFromSql || IsSqlString(resolved))
                {
                    return resolved;
                }
            }
        }

        return string.Empty;
    }

    private static string? TryResolveString(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // 1. Semantic constant evaluation (handles const string, string concatenation)
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue && constant.Value is string constString && !string.IsNullOrWhiteSpace(constString))
        {
            return constString;
        }

        // 2. String literal syntax
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        // 3. Interpolated string syntax
        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            return ConvertInterpolatedStringToSql(interpolated);
        }

        // 4. Identifier reference (e.g. var sql = "..."; Query<T>(sql);)
        if (expression is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (symbol is ILocalSymbol or IFieldSymbol)
            {
                foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
                {
                    if (syntaxRef.GetSyntax(cancellationToken) is VariableDeclaratorSyntax decl && decl.Initializer != null)
                    {
                        var resolved = TryResolveString(decl.Initializer.Value, semanticModel, cancellationToken);
                        if (!string.IsNullOrEmpty(resolved))
                        {
                            return resolved;
                        }
                    }
                }
            }

            // Syntactic fallback: look up enclosing method or type
            var enclosing = identifier.Ancestors().FirstOrDefault(a => a is MethodDeclarationSyntax or TypeDeclarationSyntax);
            if (enclosing != null)
            {
                foreach (var decl in enclosing.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (decl.Identifier.ValueText == identifier.Identifier.ValueText && decl.Initializer != null)
                    {
                        var resolved = TryResolveString(decl.Initializer.Value, semanticModel, cancellationToken);
                        if (!string.IsNullOrEmpty(resolved))
                        {
                            return resolved;
                        }
                    }
                }
            }
        }

        // 5. Binary add expression ("SELECT ... " + "FROM ...")
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            var left = TryResolveString(binary.Left, semanticModel, cancellationToken);
            var right = TryResolveString(binary.Right, semanticModel, cancellationToken);
            if (left != null && right != null)
            {
                return left + right;
            }
        }

        return null;
    }

    private static bool IsSqlString(string text)
    {
        var trimmed = text.Trim();
        if (SqlKeywordRegex.IsMatch(trimmed))
        {
            return true;
        }

        // Stored procedure invocation convention
        if (trimmed.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("usp_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Package.procedure call pattern (Oracle) e.g. "CUSTOMER_PKG.GET_CUSTOMERS"
        if (trimmed.Contains('.') && !trimmed.Contains(' ') && Regex.IsMatch(trimmed, @"^[A-Za-z_][\w]*\.[A-Za-z_][\w]*$"))
        {
            return true;
        }

        return false;
    }

    private static string ConvertInterpolatedStringToSql(InterpolatedStringExpressionSyntax interpolated)
    {
        var parts = new List<string>();
        var paramIndex = 0;

        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax textSyntax)
            {
                parts.Add(textSyntax.TextToken.ValueText);
            }
            else if (content is InterpolationSyntax interpolation)
            {
                if (interpolation.Expression is IdentifierNameSyntax id)
                {
                    parts.Add("@" + id.Identifier.ValueText);
                }
                else
                {
                    parts.Add($"@p{paramIndex++}");
                }
            }
        }

        return string.Concat(parts);
    }

    private static (string? TypeName, ITypeSymbol? TypeSymbol) ResolveTargetType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // 1. Generic type argument in invocation: Query<Customer>(sql) or FromSqlRaw<Customer>(sql)
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count > 0)
            {
                var typeSyntax = generic.TypeArgumentList.Arguments[0];
                var typeSymbol = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
                return (typeSyntax.ToString(), typeSymbol);
            }

            // 2. EF Core DbSet<Customer>.FromSqlRaw: check instance expression type
            var instanceType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            if (instanceType is INamedTypeSymbol named && named.TypeArguments.Length > 0)
            {
                var entityType = named.TypeArguments[0];
                return (entityType.Name, entityType);
            }
        }
        else if (invocation.Expression is GenericNameSyntax directGeneric && directGeneric.TypeArgumentList.Arguments.Count > 0)
        {
            var typeSyntax = directGeneric.TypeArgumentList.Arguments[0];
            var typeSymbol = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
            return (typeSyntax.ToString(), typeSymbol);
        }

        // 3. Fallback to semantic invocation operation
        var operation = semanticModel.GetOperation(invocation, cancellationToken);
        if (operation is Microsoft.CodeAnalysis.Operations.IInvocationOperation invocationOp)
        {
            if (invocationOp.TargetMethod.IsGenericMethod && invocationOp.TargetMethod.TypeArguments.Length > 0)
            {
                var target = invocationOp.TargetMethod.TypeArguments[0];
                return (target.Name, target);
            }

            if (invocationOp.Instance?.Type is INamedTypeSymbol instanceNamed && instanceNamed.TypeArguments.Length > 0)
            {
                var target = instanceNamed.TypeArguments[0];
                return (target.Name, target);
            }
        }

        return (null, null);
    }

    private static IReadOnlyList<PropertyDescriptor> ExtractPropertiesFromSymbol(ITypeSymbol typeSymbol)
    {
        var properties = new List<PropertyDescriptor>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var typesToScan = new List<ITypeSymbol>();
        for (var current = typeSymbol; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            typesToScan.Add(current);
        }
        if (typeSymbol.TypeKind == TypeKind.Interface || (!typeSymbol.AllInterfaces.IsDefaultOrEmpty && typeSymbol.AllInterfaces.Length > 0))
        {
            if (!typeSymbol.AllInterfaces.IsDefaultOrEmpty)
            {
                foreach (var iface in typeSymbol.AllInterfaces)
                {
                    if (!typesToScan.Contains(iface))
                    {
                        typesToScan.Add(iface);
                    }
                }
            }
        }

        foreach (var current in typesToScan)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop || prop.IsStatic || prop.IsIndexer)
                {
                    continue;
                }

                if (prop.GetAttributes().Any(a => a.AttributeClass?.Name is "NotMappedAttribute" or "NotMapped"))
                {
                    continue;
                }

                if (!seenNames.Add(prop.Name))
                {
                    continue;
                }

                string? columnName = null;
                var colAttr = prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is "ColumnAttribute" or "Column" or "ExpectedColumnAttribute" or "ExpectedColumn");
                if (colAttr != null && !colAttr.ConstructorArguments.IsDefaultOrEmpty && colAttr.ConstructorArguments.Length > 0 && colAttr.ConstructorArguments[0].Value is string colName && !string.IsNullOrWhiteSpace(colName))
                {
                    columnName = colName;
                }
                else
                {
                    foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
                    {
                        if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax propSyntax)
                        {
                            columnName = ExtractAttributeStringArgument(propSyntax.AttributeLists, "Column") ??
                                         ExtractAttributeStringArgument(propSyntax.AttributeLists, "ExpectedColumn");
                            if (!string.IsNullOrEmpty(columnName))
                            {
                                break;
                            }
                        }
                    }
                }

                var isPrimaryKey = prop.GetAttributes().Any(a => a.AttributeClass?.Name is "KeyAttribute" or "Key") ||
                                   prop.DeclaringSyntaxReferences.Any(s => s.GetSyntax() is PropertyDeclarationSyntax ps && HasAttribute(ps.AttributeLists, "Key")) ||
                                   string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(prop.Name, $"{typeSymbol.Name}Id", StringComparison.OrdinalIgnoreCase);

                var isNullable = prop.NullableAnnotation == NullableAnnotation.Annotated ||
                                 (prop.Type is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

                int? maxLength = null;
                var maxLenAttr = prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is "MaxLengthAttribute" or "MaxLength" or "StringLengthAttribute" or "StringLength");
                if (maxLenAttr != null && maxLenAttr.ConstructorArguments.Length > 0 && maxLenAttr.ConstructorArguments[0].Value is int maxLen)
                {
                    maxLength = maxLen;
                }
                else
                {
                    foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
                    {
                        if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax propSyntax)
                        {
                            if (ExtractAttributeIntArgument(propSyntax.AttributeLists, "MaxLength", out var ml) ||
                                ExtractAttributeIntArgument(propSyntax.AttributeLists, "StringLength", out ml))
                            {
                                maxLength = ml;
                                break;
                            }
                        }
                    }
                }

                properties.Add(new PropertyDescriptor(
                    Name: prop.Name,
                    ClrTypeName: prop.Type.ToDisplayString(),
                    ColumnName: columnName,
                    ColumnType: null,
                    IsNullable: isNullable,
                    MaxLength: maxLength,
                    IsPrimaryKey: isPrimaryKey,
                    IsForeignKey: false));
            }
        }

        return properties;
    }

    private static IReadOnlyList<PropertyDescriptor> ExtractPropertiesFromSyntax(TypeDeclarationSyntax typeDecl)
    {
        var properties = new List<PropertyDescriptor>();

        foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            // Skip non-public or static properties
            if (prop.Modifiers.Any(SyntaxKind.StaticKeyword) || !prop.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                continue;
            }

            var propName = prop.Identifier.ValueText;
            var clrType = prop.Type.ToString();

            if (HasAttribute(prop.AttributeLists, "NotMapped"))
            {
                continue;
            }

            var columnName = ExtractAttributeStringArgument(prop.AttributeLists, "Column") ??
                             ExtractAttributeStringArgument(prop.AttributeLists, "ExpectedColumn");
            var isPrimaryKey = HasAttribute(prop.AttributeLists, "Key") ||
                               string.Equals(propName, "Id", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(propName, $"{typeDecl.Identifier.ValueText}Id", StringComparison.OrdinalIgnoreCase);

            var isNullable = clrType.EndsWith("?", StringComparison.Ordinal) ||
                             clrType.StartsWith("Nullable<", StringComparison.Ordinal);

            int? maxLength = null;
            if (ExtractAttributeIntArgument(prop.AttributeLists, "MaxLength", out var ml) ||
                ExtractAttributeIntArgument(prop.AttributeLists, "StringLength", out ml))
            {
                maxLength = ml;
            }

            properties.Add(new PropertyDescriptor(
                Name: propName,
                ClrTypeName: clrType,
                ColumnName: columnName,
                ColumnType: null,
                IsNullable: isNullable,
                MaxLength: maxLength,
                IsPrimaryKey: isPrimaryKey,
                IsForeignKey: false));
        }

        return properties;
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return string.Equals(name, attributeName, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(name, attributeName + "Attribute", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static string? ExtractAttributeStringArgument(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        var attr = attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(a =>
            {
                var name = a.Name.ToString();
                return string.Equals(name, attributeName, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(name, attributeName + "Attribute", StringComparison.OrdinalIgnoreCase);
            });

        if (attr?.ArgumentList?.Arguments.Count > 0)
        {
            var firstArg = attr.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return lit.Token.ValueText;
            }
        }

        return null;
    }

    private static bool ExtractAttributeIntArgument(SyntaxList<AttributeListSyntax> attributeLists, string attributeName, out int value)
    {
        value = 0;
        var attr = attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(a =>
            {
                var name = a.Name.ToString();
                return string.Equals(name, attributeName, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(name, attributeName + "Attribute", StringComparison.OrdinalIgnoreCase);
            });

        if (attr?.ArgumentList?.Arguments.Count > 0)
        {
            var firstArg = attr.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression) && lit.Token.Value is int i)
            {
                value = i;
                return true;
            }
        }

        return false;
    }

    public static string MaskSqlStringLiterals(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }

        return SqlStringLiteralRegex.Replace(sql, m => m.Length <= SpacePadding.Length
            ? SpacePadding.Substring(0, m.Length)
            : new string(' ', m.Length));
    }

    public static SqlOperationType ClassifySqlOperation(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlOperationType.Unknown;
        }

        var masked = MaskSqlStringLiterals(sql);
        var hasSelect = SelectOpRegex.IsMatch(masked);
        var hasInsert = InsertOpRegex.IsMatch(masked);
        var hasUpdate = UpdateOpRegex.IsMatch(masked);
        var hasDelete = DeleteOpRegex.IsMatch(masked);
        var hasMerge = MergeOpRegex.IsMatch(masked);
        var hasJoin = JoinOpRegex.IsMatch(masked);

        var writeCount = (hasInsert ? 1 : 0) + (hasUpdate ? 1 : 0) + (hasDelete ? 1 : 0) + (hasMerge ? 1 : 0);
        if (hasSelect && writeCount > 0)
        {
            return SqlOperationType.Mixed;
        }

        if (writeCount > 1)
        {
            return SqlOperationType.Mixed;
        }

        if (writeCount == 1)
        {
            return SqlOperationType.Write;
        }

        if (hasSelect && hasJoin)
        {
            return SqlOperationType.Join;
        }

        if (hasSelect)
        {
            return SqlOperationType.Read;
        }

        return SqlOperationType.Reference;
    }

    public static IReadOnlyList<string> ExtractReferencedTables(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<string>();
        }

        var masked = MaskSqlStringLiterals(sql);
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var regex in TablePatterns)
        {
            var matches = regex.Matches(masked);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var raw = match.Groups[1].Value.Trim();
                    var dot = raw.LastIndexOf('.');
                    var tableName = dot >= 0 ? raw.Substring(dot + 1) : raw;
                    tableName = tableName.Trim('[', ']', '`', '"');
                    if (!string.IsNullOrEmpty(tableName) && !IsSqlKeywordToken(tableName))
                    {
                        tables.Add(tableName);
                    }
                }
            }
        }

        return tables.ToList();
    }

    private static bool IsSqlKeywordToken(string token)
    {
        return token.ToUpperInvariant() is "SELECT" or "FROM" or "WHERE" or "AS" or
            "JOIN" or "ON" or "INTO" or "SET" or "VALUES" or "AND" or "OR" or "NULL";
    }

    public static string? InferProviderHint(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        if (typeName.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "oracle";
        }

        if (typeName.IndexOf("Npgsql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Postgre", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "postgresql";
        }

        if (typeName.IndexOf("Sql", StringComparison.OrdinalIgnoreCase) >= 0 &&
            typeName.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) < 0 &&
            typeName.IndexOf("Postgresql", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return "sqlserver";
        }

        if (typeName.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "mysql";
        }
        return null;
    }
    private static IReadOnlyList<ParameterDescriptor> ExtractParameters(string sqlText)
    {
        var parameters = new List<ParameterDescriptor>();
        var masked = MaskSqlStringLiterals(sqlText);
        var matches = ParameterRegex.Matches(masked);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 1;

        foreach (Match match in matches)
        {
            var name = match.Value; // e.g. @Id, :id, $1
            if (seen.Add(name))
            {
                parameters.Add(new ParameterDescriptor(
                    Name: name,
                    DataType: "unknown",
                    Direction: ParameterDirection.Input,
                    MaxLength: null,
                    Precision: null,
                    Scale: null,
                    IsNullable: true,
                    OrdinalPosition: ordinal++));
            }
        }

        return parameters;
    }
}
