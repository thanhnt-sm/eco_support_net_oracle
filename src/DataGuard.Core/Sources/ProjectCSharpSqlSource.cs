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
        "FromSqlRaw", "FromSqlInterpolated", "FromSql",
        "ExecuteSqlRaw", "ExecuteSqlRawAsync", "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync",
    };

    private static readonly Regex SqlKeywordRegex = new(
        @"\b(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM|EXEC|EXECUTE|MERGE|WITH)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ParameterRegex = new(
        @"@([A-Za-z_][\w]*)",
        RegexOptions.Compiled);

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

        foreach (var tree in syntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

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

                var location = invocation.GetLocation();
                var lineSpan = location.GetLineSpan();
                var filePath = lineSpan.Path;
                var lineNumber = lineSpan.StartLinePosition.Line + 1;
                var fileName = Path.GetFileName(filePath);

                var targetDisplay = string.IsNullOrEmpty(targetTypeName) ? "untyped" : targetTypeName;
                var detail = $"Found SQL in {fileName}:{lineNumber} targeting {targetDisplay}";

                _progress?.Emit(new ProgressEvent(
                    ProgressEventKind.ContractDiscovered,
                    "Acquiring contracts",
                    detail));

                Console.WriteLine($"[INFO] {detail}");

                var parameters = ExtractParameters(sqlText);

                var descriptor = new RawSqlDescriptor(
                    Id: $"project-sql:{fileName}:{lineNumber}",
                    SqlText: sqlText,
                    Parameters: parameters,
                    ResultColumns: Array.Empty<ColumnDescriptor>(),
                    Location: location,
                    ExpectedProperties: expectedProperties,
                    TargetTypeName: targetTypeName);

                descriptors.Add(descriptor);
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
        return trimmed.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("usp_", StringComparison.OrdinalIgnoreCase);
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

        for (var current = typeSymbol; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
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
                if (colAttr != null && colAttr.ConstructorArguments.Length > 0 && colAttr.ConstructorArguments[0].Value is string colName && !string.IsNullOrWhiteSpace(colName))
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

    private static IReadOnlyList<ParameterDescriptor> ExtractParameters(string sqlText)
    {
        var parameters = new List<ParameterDescriptor>();
        var matches = ParameterRegex.Matches(sqlText);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 1;

        foreach (Match match in matches)
        {
            var name = match.Value; // e.g. @Id
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
