// <copyright file="TypeShapeIndex.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.Analyzers;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>One mapped column of a type declared in the compilation.</summary>
internal readonly struct MappedColumn
{
    public MappedColumn(string propertyName, string? columnName)
    {
        PropertyName = propertyName;
        ColumnName = columnName;
    }

    /// <summary>Gets the C# property name.</summary>
    public string PropertyName { get; }

    /// <summary>Gets the explicit <c>[Column("name")]</c> value, if any.</summary>
    public string? ColumnName { get; }

    /// <summary>Gets the column name the property maps to by default (explicit column name, else property name).</summary>
    public string EffectiveName => ColumnName ?? PropertyName;
}

/// <summary>
/// Syntax-only index of the class/record/struct declarations of one compilation, used for the DG004 shape check and
/// the DG017 explicit-column fix. It is built lazily, at most once per compilation, the first time a SQL call needs a
/// target type: the walk descends only into namespace and type declarations (never into member bodies), so its cost
/// is proportional to the number of type and member declarations, not to the size of the code. Types are matched by
/// simple name; a name declared in two different containers is ambiguous and yields no shape (no diagnostic).
/// </summary>
internal sealed class TypeShapeIndex
{
    private static readonly HashSet<string> ScalarTypeNames = new(StringComparer.Ordinal)
    {
        "String", "Boolean", "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double",
        "Decimal", "Char", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Guid", "Half", "BigInteger",
    };

    private readonly Dictionary<string, List<TypeDeclarationSyntax>> types = new(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguousTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> valueTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> dbSetEntities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<MappedColumn>> columns = new(StringComparer.Ordinal);

    private TypeShapeIndex()
    {
    }

    /// <summary>Builds the index from the syntax trees of a compilation.</summary>
    /// <param name="trees">Syntax trees.</param>
    /// <returns>The index.</returns>
    public static TypeShapeIndex Build(IEnumerable<SyntaxTree> trees)
    {
        var index = new TypeShapeIndex();
        var containers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodes(static node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax))
            {
                switch (node)
                {
                    case EnumDeclarationSyntax enumeration:
                        index.valueTypes.Add(enumeration.Identifier.ValueText);
                        break;
                    case TypeDeclarationSyntax type:
                        index.AddType(type, containers);
                        break;
                }
            }
        }

        return index;
    }

    /// <summary>Returns the entity type name of a <c>DbSet&lt;T&gt;</c> property declared in the compilation.</summary>
    /// <param name="propertyName">Property name (for example <c>Orders</c>).</param>
    /// <returns>The entity simple name, or null when unknown or ambiguous.</returns>
    public string? GetDbSetEntity(string propertyName) =>
        dbSetEntities.TryGetValue(propertyName, out var entity) ? entity : null;

    /// <summary>Returns the mapped scalar columns of a type declared in the compilation.</summary>
    /// <param name="typeName">Simple type name.</param>
    /// <returns>The columns (base types first), or an empty list when the type is unknown or ambiguous.</returns>
    public IReadOnlyList<MappedColumn> GetColumns(string typeName) =>
        columns.GetOrAdd(typeName, name => ComputeColumns(name, new HashSet<string>(StringComparer.Ordinal)));

    private void AddType(TypeDeclarationSyntax type, Dictionary<string, string> containers)
    {
        var name = type.Identifier.ValueText;
        if (type is StructDeclarationSyntax || (type is RecordDeclarationSyntax record && record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)))
        {
            valueTypes.Add(name);
        }

        if (type is InterfaceDeclarationSyntax)
        {
            return;
        }

        var container = GetContainer(type);
        if (containers.TryGetValue(name, out var existing) && existing != container)
        {
            ambiguousTypes.Add(name);
        }

        containers[name] = container;
        if (!types.TryGetValue(name, out var declarations))
        {
            declarations = new List<TypeDeclarationSyntax>();
            types.Add(name, declarations);
        }

        declarations.Add(type);
        foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (property.Type is GenericNameSyntax { Identifier.ValueText: "DbSet", TypeArgumentList.Arguments.Count: 1 } dbSet)
            {
                var entity = SqlCallSyntax.GetSimpleTypeName(dbSet.TypeArgumentList.Arguments[0]);
                var propertyName = property.Identifier.ValueText;
                dbSetEntities[propertyName] = dbSetEntities.TryGetValue(propertyName, out var previous) && previous != entity ? null : entity;
            }
        }
    }

    private IReadOnlyList<MappedColumn> ComputeColumns(string typeName, HashSet<string> visited)
    {
        if (ambiguousTypes.Contains(typeName) || !types.TryGetValue(typeName, out var declarations) || !visited.Add(typeName))
        {
            return Array.Empty<MappedColumn>();
        }

        var result = new List<MappedColumn>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            var baseName = declaration.BaseList?.Types.Count > 0
                ? SqlCallSyntax.GetSimpleTypeName(declaration.BaseList.Types[0].Type)
                : null;
            if (baseName != null && types.ContainsKey(baseName))
            {
                foreach (var column in ComputeColumns(baseName, visited))
                {
                    if (seen.Add(column.PropertyName))
                    {
                        result.Add(column);
                    }
                }
            }
        }

        foreach (var declaration in declarations)
        {
            if (declaration is RecordDeclarationSyntax { ParameterList: { } parameters })
            {
                foreach (var parameter in parameters.Parameters)
                {
                    if (IsScalar(parameter.Type) && seen.Add(parameter.Identifier.ValueText))
                    {
                        result.Add(new MappedColumn(parameter.Identifier.ValueText, GetColumnName(parameter.AttributeLists)));
                    }
                }
            }

            foreach (var property in declaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                if (property.Modifiers.Any(SyntaxKind.StaticKeyword)
                    || HasAttribute(property.AttributeLists, "NotMapped")
                    || !IsScalar(property.Type)
                    || !seen.Add(property.Identifier.ValueText))
                {
                    continue;
                }

                result.Add(new MappedColumn(property.Identifier.ValueText, GetColumnName(property.AttributeLists)));
            }
        }

        return result;
    }

    private bool IsScalar(TypeSyntax? type)
    {
        switch (type)
        {
            case PredefinedTypeSyntax predefined:
                return !predefined.Keyword.IsKind(SyntaxKind.ObjectKeyword) && !predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);
            case NullableTypeSyntax nullable:
                return IsScalar(nullable.ElementType);
            case ArrayTypeSyntax array:
                return array.ElementType is PredefinedTypeSyntax element && element.Keyword.IsKind(SyntaxKind.ByteKeyword);
            case GenericNameSyntax { Identifier.ValueText: "Nullable", TypeArgumentList.Arguments.Count: 1 } generic:
                return IsScalar(generic.TypeArgumentList.Arguments[0]);
            default:
                var name = SqlCallSyntax.GetSimpleTypeName(type);
                return name != null && (ScalarTypeNames.Contains(name) || valueTypes.Contains(name));
        }
    }

    private static string GetContainer(SyntaxNode node)
    {
        var parts = new List<string>();
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    parts.Add(ns.Name.ToString());
                    break;
                case BaseTypeDeclarationSyntax type:
                    parts.Add(type.Identifier.ValueText);
                    break;
            }
        }

        parts.Reverse();
        return string.Join(".", parts);
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name)
    {
        foreach (var list in lists)
        {
            foreach (var attribute in list.Attributes)
            {
                var simple = SqlCallSyntax.GetSimpleTypeName(attribute.Name);
                if (simple == name || simple == name + "Attribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? GetColumnName(SyntaxList<AttributeListSyntax> lists)
    {
        foreach (var list in lists)
        {
            foreach (var attribute in list.Attributes)
            {
                var simple = SqlCallSyntax.GetSimpleTypeName(attribute.Name);
                if ((simple == "Column" || simple == "ColumnAttribute")
                    && attribute.ArgumentList?.Arguments.FirstOrDefault() is { NameEquals: null, NameColon: null } argument
                    && argument.Expression is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    && !string.IsNullOrWhiteSpace(literal.Token.ValueText))
                {
                    return literal.Token.ValueText;
                }
            }
        }

        return null;
    }
}
