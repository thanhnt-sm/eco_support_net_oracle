using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>
/// Target (result) type resolution and the expected-property list derived from it.
/// </summary>
public sealed partial class ProjectCSharpSqlSource
{
    private static readonly HashSet<string> ScalarTypeNames = new(StringComparer.Ordinal)
    {
        "string", "String", "System.String", "bool", "Boolean", "byte", "Byte", "sbyte", "SByte", "short", "Int16",
        "ushort", "UInt16", "int", "Int32", "uint", "UInt32", "long", "Int64", "ulong", "UInt64", "float", "Single",
        "double", "Double", "decimal", "Decimal", "char", "Char", "object", "Object", "dynamic", "DateTime",
        "DateTimeOffset", "Guid", "TimeSpan", "DateOnly", "TimeOnly", "byte[]", "Byte[]",
    };

    private static (string? TypeName, ITypeSymbol? TypeSymbol) ResolveTargetType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var nameSyntax = invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name,
            MemberBindingExpressionSyntax mb => mb.Name,
            SimpleNameSyntax simple => simple,
            _ => null,
        };

        // 1. Generic type argument in invocation: Query<Customer>(sql), conn?.Query<Customer>(sql) or FromSqlRaw<Customer>(sql)
        if (nameSyntax is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count > 0)
        {
            var typeSyntax = generic.TypeArgumentList.Arguments[0];
            var typeSymbol = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
            return (typeSyntax.ToString(), typeSymbol);
        }

        // 2. EF Core DbSet<Customer>.FromSqlRaw: check instance expression type
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var instanceType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            if (instanceType is INamedTypeSymbol named && named.TypeArguments.Length > 0)
            {
                var entityType = named.TypeArguments[0];
                return (entityType.Name, entityType);
            }
        }

        // 3. Fallback to semantic invocation operation
        var operation = semanticModel.GetOperation(invocation, cancellationToken);
        if (operation is Microsoft.CodeAnalysis.Operations.IInvocationOperation invocationOp)
        {
            if (invocationOp.TargetMethod.IsGenericMethod && invocationOp.TargetMethod.TypeArguments.Length > 0)
            {
                var target = invocationOp.TargetMethod.TypeArguments.FirstOrDefault(t => t.TypeKind == TypeKind.Class || (t.TypeKind == TypeKind.Struct && t.SpecialType == SpecialType.None))
                    ?? invocationOp.TargetMethod.TypeArguments[0];
                return (target.Name, target);
            }

            if (invocationOp.Instance?.Type is INamedTypeSymbol instanceNamed && instanceNamed.TypeArguments.Length > 0)
            {
                var target = instanceNamed.TypeArguments.FirstOrDefault(t => t.TypeKind == TypeKind.Class || (t.TypeKind == TypeKind.Struct && t.SpecialType == SpecialType.None))
                    ?? instanceNamed.TypeArguments[0];
                return (target.Name, target);
            }
        }

        // 4. Fallback to typeof(T) arguments in invocation: conn.Query(typeof(Customer), sql)
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is TypeOfExpressionSyntax typeOfExpr)
            {
                var typeSymbol = semanticModel.GetTypeInfo(typeOfExpr.Type, cancellationToken).Type;
                if (typeSymbol != null)
                {
                    return (typeSymbol.Name, typeSymbol);
                }

                return (typeOfExpr.Type.ToString(), null);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// True when a query result of this type is a single scalar column (string, primitives, date/time types, Guid,
    /// decimal, enums, byte[] and their nullable forms), so the call has no expected property list.
    /// </summary>
    internal static bool IsScalarTargetType(ITypeSymbol? type, string? typeName)
    {
        if (type is not null && type.TypeKind != TypeKind.Error)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            if (type.TypeKind == TypeKind.Enum || type.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            {
                return true;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Object:
                case SpecialType.System_String:
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_DateTime:
                    return true;
            }

            var full = type.ToDisplayString();
            return full is "System.DateTimeOffset" or "System.Guid" or "System.TimeSpan" or "System.DateOnly" or "System.TimeOnly";
        }

        if (string.IsNullOrEmpty(typeName))
        {
            return false;
        }

        var bare = typeName.TrimEnd('?');
        var lastDot = bare.LastIndexOf('.');
        var simple = lastDot >= 0 && !bare.EndsWith("[]", StringComparison.Ordinal) ? bare.Substring(lastDot + 1) : bare;
        return ScalarTypeNames.Contains(bare) || ScalarTypeNames.Contains(simple);
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

        if (!typeSymbol.AllInterfaces.IsDefaultOrEmpty)
        {
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                if (!typesToScan.Contains(iface, SymbolEqualityComparer.Default))
                {
                    typesToScan.Add(iface);
                }
            }
        }

        foreach (var current in typesToScan)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop || prop.IsStatic || prop.IsIndexer || string.Equals(prop.Name, "EqualityContract", StringComparison.Ordinal))
                {
                    continue;
                }

                // Only public instance properties a materializer can write (setter or init accessor).
                if (prop.DeclaredAccessibility != Accessibility.Public || prop.SetMethod is null)
                {
                    continue;
                }

                if (prop.GetAttributes().Any(a => a.AttributeClass?.Name is "NotMappedAttribute" or "NotMapped") ||
                    prop.DeclaringSyntaxReferences.Any(s => HasAttribute(GetSyntaxAttributeLists(s), "NotMapped")))
                {
                    continue;
                }

                if (!seenNames.Add(prop.Name))
                {
                    continue;
                }

                string? columnName = null;
                var colAttr = prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name is "ColumnAttribute" or "Column" or "ExpectedColumnAttribute" or "ExpectedColumn");
                if (colAttr != null && !colAttr.ConstructorArguments.IsDefaultOrEmpty && colAttr.ConstructorArguments[0].Value is string colName && !string.IsNullOrWhiteSpace(colName))
                {
                    columnName = colName;
                }
                else
                {
                    foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
                    {
                        var attrs = GetSyntaxAttributeLists(syntaxRef);
                        if (attrs.Count > 0)
                        {
                            columnName = ExtractAttributeStringArgument(attrs, "Column") ??
                                         ExtractAttributeStringArgument(attrs, "ExpectedColumn");
                            if (!string.IsNullOrEmpty(columnName))
                            {
                                break;
                            }
                        }
                    }
                }

                var isPrimaryKey = prop.GetAttributes().Any(a => a.AttributeClass?.Name is "KeyAttribute" or "Key") ||
                                   prop.DeclaringSyntaxReferences.Any(s => HasAttribute(GetSyntaxAttributeLists(s), "Key")) ||
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
                        var attrs = GetSyntaxAttributeLists(syntaxRef);
                        if (attrs.Count > 0 &&
                            (ExtractAttributeIntArgument(attrs, "MaxLength", out var ml) ||
                             ExtractAttributeIntArgument(attrs, "StringLength", out ml)))
                        {
                            maxLength = ml;
                            break;
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

    private static IReadOnlyList<PropertyDescriptor> ExtractPropertiesFromSyntax(IEnumerable<TypeDeclarationSyntax> typeDecls)
    {
        var properties = new List<PropertyDescriptor>();

        foreach (var typeDecl in typeDecls)
        {
            if (typeDecl is RecordDeclarationSyntax && typeDecl.ParameterList != null)
            {
                foreach (var param in typeDecl.ParameterList.Parameters)
                {
                    var propName = param.Identifier.ValueText;
                    if (string.IsNullOrEmpty(propName) ||
                        string.Equals(propName, "EqualityContract", StringComparison.Ordinal) ||
                        properties.Any(p => string.Equals(p.Name, propName, StringComparison.Ordinal)) ||
                        HasAttribute(param.AttributeLists, "NotMapped"))
                    {
                        continue;
                    }

                    var clrType = param.Type?.ToString() ?? "object";
                    properties.Add(BuildSyntaxProperty(propName, clrType, param.AttributeLists, typeDecl));
                }
            }

            foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (properties.Any(p => string.Equals(p.Name, prop.Identifier.ValueText, StringComparison.Ordinal)))
                {
                    continue;
                }

                // Skip non-public or static properties
                var isPublic = prop.Modifiers.Any(SyntaxKind.PublicKeyword) ||
                               (typeDecl is InterfaceDeclarationSyntax && !prop.Modifiers.Any(SyntaxKind.PrivateKeyword) && !prop.Modifiers.Any(SyntaxKind.InternalKeyword));
                if (prop.Modifiers.Any(SyntaxKind.StaticKeyword) || !isPublic)
                {
                    continue;
                }

                // Skip get-only and expression-bodied properties: a materializer cannot write them.
                var hasWriter = prop.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration)) == true;
                if (!hasWriter || HasAttribute(prop.AttributeLists, "NotMapped"))
                {
                    continue;
                }

                properties.Add(BuildSyntaxProperty(prop.Identifier.ValueText, prop.Type.ToString(), prop.AttributeLists, typeDecl));
            }
        }

        return properties;
    }

    private static PropertyDescriptor BuildSyntaxProperty(
        string propName,
        string clrType,
        SyntaxList<AttributeListSyntax> attributeLists,
        TypeDeclarationSyntax typeDecl)
    {
        var columnName = ExtractAttributeStringArgument(attributeLists, "Column") ??
                         ExtractAttributeStringArgument(attributeLists, "ExpectedColumn");
        var isPrimaryKey = HasAttribute(attributeLists, "Key") ||
                           string.Equals(propName, "Id", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(propName, $"{typeDecl.Identifier.ValueText}Id", StringComparison.OrdinalIgnoreCase);
        var isNullable = clrType.EndsWith("?", StringComparison.Ordinal) ||
                         clrType.StartsWith("Nullable<", StringComparison.Ordinal);

        int? maxLength = null;
        if (ExtractAttributeIntArgument(attributeLists, "MaxLength", out var ml) ||
            ExtractAttributeIntArgument(attributeLists, "StringLength", out ml))
        {
            maxLength = ml;
        }

        return new PropertyDescriptor(
            Name: propName,
            ClrTypeName: clrType,
            ColumnName: columnName,
            ColumnType: null,
            IsNullable: isNullable,
            MaxLength: maxLength,
            IsPrimaryKey: isPrimaryKey,
            IsForeignKey: false);
    }

    private static SyntaxList<AttributeListSyntax> GetSyntaxAttributeLists(SyntaxReference syntaxRef)
    {
        var node = syntaxRef.GetSyntax();
        if (node is PropertyDeclarationSyntax p)
        {
            return p.AttributeLists;
        }

        if (node is ParameterSyntax param)
        {
            return param.AttributeLists;
        }

        return default;
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Any(attr => AttributeNameMatches(attr, attributeName));
    }

    private static bool AttributeNameMatches(AttributeSyntax attr, string attributeName)
    {
        var name = attr.Name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => attr.Name.ToString(),
        };

        return string.Equals(name, attributeName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, attributeName + "Attribute", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractAttributeStringArgument(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        var attr = attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(a => AttributeNameMatches(a, attributeName));

        if (attr?.ArgumentList?.Arguments.Count > 0)
        {
            var firstArg = attr.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax lit && (lit.Token.Value is string || lit.IsKind(SyntaxKind.StringLiteralExpression)))
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
            .FirstOrDefault(a => AttributeNameMatches(a, attributeName));

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
}
