using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>
/// Call-site argument bindings: what the C# code passes to a SQL statement or stored procedure (Dapper parameter
/// objects and <c>DynamicParameters</c>, ADO.NET <c>Parameters.Add*</c>, EF Core extra arguments, interpolation holes),
/// with the CLR type from the semantic model and the direction written at the call site.
/// </summary>
public sealed partial class ProjectCSharpSqlSource
{
    private const int MaxBindingDepth = 4;

    /// <summary>
    /// Normalizes a CLR type for contract matching: <c>Nullable&lt;T&gt;</c> and nullable reference annotations are
    /// unwrapped (<c>int?</c> ⇒ <c>int</c>, <c>string?</c> ⇒ <c>string</c>), enums become <c>enum:&lt;underlying&gt;</c>
    /// (<c>enum:int</c>), and anything that cannot be matched (error types, <c>object</c>, <c>dynamic</c>, type parameters,
    /// anonymous types, <c>DBNull</c>) is null. Other types use <c>ToDisplayString()</c>
    /// (keywords for special types, e.g. <c>int</c>, <c>decimal</c>, <c>byte[]</c>; namespace-qualified otherwise, e.g.
    /// <c>System.DateTime</c>, <c>System.Guid</c>).
    /// </summary>
    internal static string? NormalizeClrType(ITypeSymbol? type)
    {
        if (type is null || type.TypeKind is TypeKind.Error or TypeKind.Dynamic or TypeKind.TypeParameter || type.IsAnonymousType)
        {
            return null;
        }

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            return null;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } underlying })
        {
            return "enum:" + underlying.ToDisplayString();
        }

        var display = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        if (display is "System.DBNull")
        {
            return null;
        }

        return display.EndsWith('?') ? display.Substring(0, display.Length - 1) : display;
    }

    private static string? ClrTypeOfValue(ExpressionSyntax? value, SemanticModel model, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return null;
        }

        var unwrapped = UnwrapValueExpression(value);
        if (unwrapped is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return null;
        }

        var typeInfo = model.GetTypeInfo(unwrapped, cancellationToken);
        return NormalizeClrType(typeInfo.Type ?? typeInfo.ConvertedType);
    }

    /// <summary>
    /// Strips wrappers that hide the real value type: parentheses, <c>(object)x</c>, <c>x ?? DBNull.Value</c>,
    /// <c>x == null ? DBNull.Value : x</c>, <c>x!</c>.
    /// </summary>
    private static ExpressionSyntax UnwrapValueExpression(ExpressionSyntax expression)
    {
        var current = expression;
        for (var guard = 0; guard < 8; guard++)
        {
            switch (current)
            {
                case ParenthesizedExpressionSyntax p:
                    current = p.Expression;
                    continue;
                case CastExpressionSyntax cast when cast.Type.ToString() is "object" or "Object" or "System.Object":
                    current = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax bang when bang.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    current = bang.Operand;
                    continue;
                case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                    current = coalesce.Left;
                    continue;
                case BinaryExpressionSyntax asObject when asObject.IsKind(SyntaxKind.AsExpression) && asObject.Right.ToString() is "object":
                    current = asObject.Left;
                    continue;
                case ConditionalExpressionSyntax conditional:
                    if (IsNullLike(conditional.WhenTrue))
                    {
                        current = conditional.WhenFalse;
                        continue;
                    }

                    if (IsNullLike(conditional.WhenFalse))
                    {
                        current = conditional.WhenTrue;
                        continue;
                    }

                    return current;
            }

            return current;
        }

        return current;
    }

    private static bool IsNullLike(ExpressionSyntax expression)
    {
        var inner = expression;
        while (inner is ParenthesizedExpressionSyntax p)
        {
            inner = p.Expression;
        }

        if (inner is CastExpressionSyntax cast)
        {
            inner = cast.Expression;
        }

        return inner is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } ||
               inner.ToString().EndsWith("DBNull.Value", StringComparison.Ordinal);
    }

    private static ParameterDirection? ParseDirection(ExpressionSyntax? expression)
    {
        if (expression is null)
        {
            return null;
        }

        var inner = UnwrapValueExpression(expression);
        if (inner is not MemberAccessExpressionSyntax member ||
            !member.Expression.ToString().EndsWith("ParameterDirection", StringComparison.Ordinal))
        {
            return null;
        }

        return member.Name.Identifier.ValueText switch
        {
            "Input" => ParameterDirection.Input,
            "Output" => ParameterDirection.Output,
            "InputOutput" => ParameterDirection.InputOutput,
            "ReturnValue" => ParameterDirection.ReturnValue,
            _ => null,
        };
    }

    /// <summary>Returns the member name of a provider type enum (<c>SqlDbType.Int</c> ⇒ <c>Int</c>, <c>OracleDbType.RefCursor</c> ⇒ <c>RefCursor</c>).</summary>
    private static string? ParseDbType(ExpressionSyntax? expression)
    {
        if (expression is null)
        {
            return null;
        }

        var inner = UnwrapValueExpression(expression);
        if (inner is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var owner = member.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => string.Empty,
        };

        return owner.EndsWith("DbType", StringComparison.Ordinal) || owner is "MySqlDbType" or "NpgsqlDbType"
            ? member.Name.Identifier.ValueText
            : null;
    }

    private static string? GetCreatedTypeName(BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (creation is ObjectCreationExpressionSyntax explicitCreation)
        {
            var typeText = explicitCreation.Type.ToString();
            var lt = typeText.IndexOf('<', StringComparison.Ordinal);
            return lt >= 0 ? typeText.Substring(0, lt) : typeText;
        }

        var semanticName = model.GetTypeInfo(creation, cancellationToken).Type?.Name;
        if (!string.IsNullOrEmpty(semanticName))
        {
            return semanticName;
        }

        // Target-typed new(): fall back to the declared type of the variable, field or property it initializes.
        var declaredType = creation.Parent?.Parent switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } when !declaration.Type.IsVar => declaration.Type.ToString(),
            PropertyDeclarationSyntax property => property.Type.ToString(),
            _ => null,
        };

        return declaredType;
    }

    private static bool IsParameterCreation(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken, out BaseObjectCreationExpressionSyntax creation)
    {
        creation = null!;
        if (UnwrapValueExpression(expression) is not BaseObjectCreationExpressionSyntax candidate)
        {
            return false;
        }

        var typeName = GetCreatedTypeName(candidate, model, cancellationToken);
        if (typeName is null || !typeName.EndsWith("Parameter", StringComparison.Ordinal))
        {
            return false;
        }

        creation = candidate;
        return true;
    }

    /// <summary>Builds a binding from <c>new SqlParameter(...) { ... }</c> (or any <c>*Parameter</c> type).</summary>
    private static CallBinding ParseParameterCreation(BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken)
    {
        var binding = new CallBinding(model);
        if (creation.ArgumentList is { Arguments.Count: > 0 } args)
        {
            ApplyParameterArguments(binding, args.Arguments, ArgumentLayout.Ado, model, cancellationToken);
        }

        if (creation.Initializer is { } initializer)
        {
            foreach (var assignment in initializer.Expressions.OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is IdentifierNameSyntax property)
                {
                    ApplyPropertyAssignment(binding, property.Identifier.ValueText, assignment.Right, model, cancellationToken);
                }
            }
        }

        return binding;
    }

    private static void ApplyPropertyAssignment(CallBinding binding, string propertyName, ExpressionSyntax right, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (propertyName)
        {
            case "Direction":
                binding.Direction = ParseDirection(right) ?? binding.Direction;
                break;
            case "Value":
                binding.SetValue(right, model);
                break;
            case "ParameterName":
                binding.Name = TryResolveString(right, model, cancellationToken) ?? binding.Name;
                break;
            case "DbType":
            case "SqlDbType":
            case "OracleDbType":
            case "NpgsqlDbType":
            case "MySqlDbType":
                binding.DbType = ParseDbType(right) ?? binding.DbType;
                break;
        }
    }

    /// <summary>
    /// Reads the name, value, provider type and direction from a parameter argument list. Dapper
    /// <c>DynamicParameters.Add(name, value, dbType, direction, size)</c> is positional; ADO.NET overloads vary per
    /// provider, so the ADO layout finds the direction and provider type by shape and takes the value as the argument
    /// before the direction (<c>Add(name, OracleDbType, value, ParameterDirection)</c>) or the second argument when no
    /// provider type is written (<c>new SqlParameter(name, value)</c>). An integer after a provider type is a size.
    /// </summary>
    private static void ApplyParameterArguments(
        CallBinding binding,
        SeparatedSyntaxList<ArgumentSyntax> args,
        ArgumentLayout layout,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var positional = new List<(int Index, ExpressionSyntax Expression)>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var argName = arg.NameColon?.Name.Identifier.ValueText;
            if (argName is null)
            {
                positional.Add((i, arg.Expression));
                continue;
            }

            switch (argName)
            {
                case "name":
                case "parameterName":
                    binding.Name = TryResolveString(arg.Expression, model, cancellationToken) ?? binding.Name;
                    break;
                case "value":
                case "val":
                case "obj":
                    binding.SetValue(arg.Expression, model);
                    break;
                case "dbType":
                case "type":
                case "sqlDbType":
                case "oracleDbType":
                case "parameterType":
                    binding.DbType = ParseDbType(arg.Expression) ?? binding.DbType;
                    break;
                case "direction":
                    binding.Direction = ParseDirection(arg.Expression) ?? binding.Direction;
                    break;
            }
        }

        if (positional.Count == 0)
        {
            return;
        }

        var nameSlot = -1;
        if (positional[0].Index == 0 && binding.Name is null)
        {
            var name = TryResolveString(positional[0].Expression, model, cancellationToken);
            if (name is not null)
            {
                binding.Name = name;
                nameSlot = 0;
            }
        }

        if (layout == ArgumentLayout.Dapper)
        {
            foreach (var (index, expression) in positional)
            {
                switch (index)
                {
                    case 1:
                        binding.SetValue(expression, model);
                        break;
                    case 2:
                        binding.DbType = ParseDbType(expression) ?? binding.DbType;
                        break;
                    case 3:
                        binding.Direction = ParseDirection(expression) ?? binding.Direction;
                        break;
                }
            }

            return;
        }

        if (layout == ArgumentLayout.AddWithValue)
        {
            if (positional.Count > nameSlot + 1)
            {
                binding.SetValue(positional[nameSlot + 1].Expression, model);
            }

            return;
        }

        var directionSlot = -1;
        var dbTypeSlot = -1;
        for (var k = nameSlot + 1; k < positional.Count; k++)
        {
            if (directionSlot < 0 && ParseDirection(positional[k].Expression) is { } direction)
            {
                binding.Direction = direction;
                directionSlot = k;
            }
            else if (dbTypeSlot < 0 && ParseDbType(positional[k].Expression) is { } dbType)
            {
                binding.DbType = dbType;
                dbTypeSlot = k;
            }
        }

        if (directionSlot > 0)
        {
            var valueSlot = directionSlot - 1;
            if (valueSlot > nameSlot && valueSlot != dbTypeSlot && valueSlot > dbTypeSlot)
            {
                binding.SetValue(positional[valueSlot].Expression, model);
            }
        }
        else if (dbTypeSlot < 0 && positional.Count > nameSlot + 1)
        {
            binding.SetValue(positional[nameSlot + 1].Expression, model);
        }
    }

    /// <summary>
    /// Expands a Dapper <c>param</c> argument into named bindings: an anonymous object (<c>new { Id = id }</c>),
    /// <c>DynamicParameters</c> (constructor template plus every <c>Add</c>/<c>AddDynamicParams</c> before the call), a
    /// local initialized with either, or any other object (its public readable instance properties, as Dapper sends them).
    /// </summary>
    private static List<CallBinding> ExpandDapperParameter(
        ExpressionSyntax expression,
        SemanticModel model,
        SyntaxNode scope,
        int anchor,
        int depth,
        CancellationToken cancellationToken)
    {
        var result = new List<CallBinding>();
        if (depth > MaxBindingDepth)
        {
            return result;
        }

        var inner = UnwrapValueExpression(expression);
        if (inner is LiteralExpressionSyntax || inner.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            return result;
        }

        if (inner is AnonymousObjectCreationExpressionSyntax anonymous)
        {
            foreach (var member in anonymous.Initializers)
            {
                var name = member.NameEquals?.Name.Identifier.ValueText ?? InferAnonymousMemberName(member.Expression);
                var binding = new CallBinding(model) { Name = name };
                binding.SetValue(member.Expression, model);
                result.Add(binding);
            }

            return result;
        }

        if (inner is BaseObjectCreationExpressionSyntax creation && GetCreatedTypeName(creation, model, cancellationToken) == "DynamicParameters")
        {
            if (creation.ArgumentList is { Arguments.Count: > 0 } templateArgs)
            {
                result.AddRange(ExpandDapperParameter(templateArgs.Arguments[0].Expression, model, scope, anchor, depth + 1, cancellationToken));
            }

            return result;
        }

        var type = model.GetTypeInfo(inner, cancellationToken).Type;
        if (inner is IdentifierNameSyntax identifier)
        {
            var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
            var declarator = symbol?.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax(cancellationToken))
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault(d => d.SyntaxTree == model.SyntaxTree);
            var initializer = declarator?.Initializer?.Value;
            var isDynamic = type?.Name == "DynamicParameters" ||
                            (initializer is BaseObjectCreationExpressionSyntax init && GetCreatedTypeName(init, model, cancellationToken) == "DynamicParameters");
            if (isDynamic)
            {
                if (initializer is BaseObjectCreationExpressionSyntax dynCreation && dynCreation.ArgumentList is { Arguments.Count: > 0 } ctorArgs)
                {
                    result.AddRange(ExpandDapperParameter(ctorArgs.Arguments[0].Expression, model, scope, anchor, depth + 1, cancellationToken));
                }

                result.AddRange(CollectDynamicParameterAdds(identifier.Identifier.ValueText, model, scope, anchor, depth, cancellationToken));
                return result;
            }

            if (initializer is not null && (type is null || type.IsAnonymousType))
            {
                return ExpandDapperParameter(initializer, model, scope, anchor, depth + 1, cancellationToken);
            }
        }

        if (type is null || type.TypeKind is TypeKind.Error || IsScalarTargetType(type, null) || type.SpecialType == SpecialType.System_Object)
        {
            return result;
        }

        foreach (var property in EnumerateReadableProperties(type))
        {
            result.Add(new CallBinding(model)
            {
                Name = property.Name,
                ClrTypeOverride = NormalizeClrType(property.Type),
                HasValue = true,
            });
        }

        return result;
    }

    private static IEnumerable<IPropertySymbol> EnumerateReadableProperties(ITypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer || property.GetMethod is null ||
                    property.DeclaredAccessibility != Accessibility.Public ||
                    property.Name == "EqualityContract" || !seen.Add(property.Name))
                {
                    continue;
                }

                yield return property;
            }
        }
    }

    private static string? InferAnonymousMemberName(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax mb } => mb.Name.Identifier.ValueText,
            _ => null,
        };
    }

    private static List<CallBinding> CollectDynamicParameterAdds(
        string variableName,
        SemanticModel model,
        SyntaxNode scope,
        int anchor,
        int depth,
        CancellationToken cancellationToken)
    {
        var result = new List<CallBinding>();
        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.SpanStart >= anchor ||
                invocation.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } member ||
                receiver.Identifier.ValueText != variableName)
            {
                continue;
            }

            switch (member.Name.Identifier.ValueText)
            {
                case "Add":
                    var binding = new CallBinding(model);
                    ApplyParameterArguments(binding, invocation.ArgumentList.Arguments, ArgumentLayout.Dapper, model, cancellationToken);
                    result.Add(binding);
                    break;
                case "AddDynamicParams" when invocation.ArgumentList.Arguments.Count > 0:
                    result.AddRange(ExpandDapperParameter(invocation.ArgumentList.Arguments[0].Expression, model, scope, anchor, depth + 1, cancellationToken));
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Replays the ADO.NET parameter collection of command variable <paramref name="receiverName"/> in source order
    /// up to <paramref name="anchor"/>: <c>Parameters.Add(new XParameter(...){...})</c>, <c>Parameters.Add(p)</c> for a
    /// local parameter built earlier (<c>new</c> or <c>CreateParameter()</c> plus property assignments),
    /// <c>Parameters.Add(name, dbType[, size])</c> with a chained <c>.Direction</c>/<c>.Value</c> assignment,
    /// <c>AddWithValue</c>, <c>AddRange</c>, <c>Parameters["x"].Direction = ...</c> and <c>Parameters.Clear()</c>.
    /// </summary>
    private static List<CallBinding> CollectCommandBindings(
        string receiverName,
        SyntaxNode scope,
        int anchor,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var added = new List<CallBinding>();
        var locals = new Dictionary<string, CallBinding>(StringComparer.Ordinal);

        foreach (var node in scope.DescendantNodes())
        {
            if (node.SpanStart >= anchor)
            {
                break;
            }

            switch (node)
            {
                case VariableDeclaratorSyntax { Initializer.Value: { } initValue } declarator:
                    if (IsParameterCreation(initValue, model, cancellationToken, out var localCreation))
                    {
                        locals[declarator.Identifier.ValueText] = ParseParameterCreation(localCreation, model, cancellationToken);
                    }
                    else if (initValue is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CreateParameter" } })
                    {
                        locals[declarator.Identifier.ValueText] = new CallBinding(model);
                    }

                    break;

                case InvocationExpressionSyntax invocation when IsParametersCall(invocation, receiverName, out var method):
                    HandleParametersCall(invocation, method, added, locals, model, cancellationToken);
                    break;

                case AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax target } assignment:
                    var propertyName = target.Name.Identifier.ValueText;
                    if (target.Expression is IdentifierNameSyntax local && locals.TryGetValue(local.Identifier.ValueText, out var localBinding))
                    {
                        ApplyPropertyAssignment(localBinding, propertyName, assignment.Right, model, cancellationToken);
                    }
                    else if (target.Expression is ElementAccessExpressionSyntax element &&
                             element.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Parameters", Expression: IdentifierNameSyntax owner } &&
                             owner.Identifier.ValueText == receiverName &&
                             element.ArgumentList.Arguments.Count == 1)
                    {
                        var key = element.ArgumentList.Arguments[0].Expression;
                        var keyName = TryResolveString(key, model, cancellationToken);
                        var indexed = keyName is not null
                            ? added.LastOrDefault(b => NamesMatch(b.Name, keyName))
                            : model.GetConstantValue(key, cancellationToken).Value is int index && index >= 0 && index < added.Count ? added[index] : null;
                        if (indexed is not null)
                        {
                            ApplyPropertyAssignment(indexed, propertyName, assignment.Right, model, cancellationToken);
                        }
                    }

                    break;
            }
        }

        return added;
    }

    /// <summary>
    /// True when <paramref name="scope"/> uses <c>receiverName.Parameters</c> and never hands the collection to another
    /// method (<c>AddParameters(cmd.Parameters)</c>), i.e. every parameter is added where the extractor can see it.
    /// </summary>
    private static bool UsesParametersCollection(string receiverName, SyntaxNode scope)
    {
        var accesses = scope.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(member =>
                member.Name.Identifier.ValueText == "Parameters" &&
                member.Expression is IdentifierNameSyntax owner &&
                owner.Identifier.ValueText == receiverName)
            .ToList();
        return accesses.Count > 0 && !accesses.Any(member => member.Parent is ArgumentSyntax);
    }

    private static bool IsParametersCall(InvocationExpressionSyntax invocation, string receiverName, out string method)
    {
        method = string.Empty;
        if (invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Parameters", Expression: IdentifierNameSyntax owner },
            } call &&
            owner.Identifier.ValueText == receiverName)
        {
            method = call.Name.Identifier.ValueText;
            return true;
        }

        return false;
    }

    private static void HandleParametersCall(
        InvocationExpressionSyntax invocation,
        string method,
        List<CallBinding> added,
        Dictionary<string, CallBinding> locals,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var args = invocation.ArgumentList.Arguments;
        CallBinding? binding = null;
        switch (method)
        {
            case "Clear":
                added.Clear();
                return;
            case "AddRange" when args.Count == 1:
                foreach (var element in EnumerateCollectionElements(args[0].Expression))
                {
                    added.Add(BindingFromParameterExpression(element, locals, model, cancellationToken));
                }

                return;
            case "AddWithValue":
                binding = new CallBinding(model);
                ApplyParameterArguments(binding, args, ArgumentLayout.AddWithValue, model, cancellationToken);
                break;
            case "Add" when args.Count == 1:
                binding = BindingFromParameterExpression(args[0].Expression, locals, model, cancellationToken);
                break;
            case "Add":
                binding = new CallBinding(model);
                ApplyParameterArguments(binding, args, ArgumentLayout.Ado, model, cancellationToken);
                break;
            default:
                return;
        }

        added.Add(binding);

        // cmd.Parameters.Add("@x", SqlDbType.Int).Direction = ParameterDirection.Output;
        if (invocation.Parent is MemberAccessExpressionSyntax chained &&
            chained.Parent is AssignmentExpressionSyntax chainedAssignment &&
            chainedAssignment.Left == chained)
        {
            ApplyPropertyAssignment(binding, chained.Name.Identifier.ValueText, chainedAssignment.Right, model, cancellationToken);
        }

        // var p = cmd.Parameters.Add(...); p.Direction = ...;
        if (invocation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator })
        {
            locals[declarator.Identifier.ValueText] = binding;
        }
    }

    private static CallBinding BindingFromParameterExpression(
        ExpressionSyntax expression,
        Dictionary<string, CallBinding> locals,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (IsParameterCreation(expression, model, cancellationToken, out var creation))
        {
            return ParseParameterCreation(creation, model, cancellationToken);
        }

        if (UnwrapValueExpression(expression) is IdentifierNameSyntax local && locals.TryGetValue(local.Identifier.ValueText, out var known))
        {
            return known;
        }

        // A parameter built elsewhere (helper method, field): it still occupies a slot, but nothing about it is known.
        return new CallBinding(model);
    }

    private static IEnumerable<ExpressionSyntax> EnumerateCollectionElements(ExpressionSyntax expression)
    {
        return UnwrapValueExpression(expression) switch
        {
            ArrayCreationExpressionSyntax { Initializer: { } init } => init.Expressions,
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer.Expressions,
            CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>().Select(e => e.Expression),
            InitializerExpressionSyntax initializer => initializer.Expressions,
            _ => Array.Empty<ExpressionSyntax>(),
        };
    }

    /// <summary>
    /// EF Core extra arguments after the SQL (<c>FromSqlRaw(sql, a, b)</c>, <c>ExecuteSqlRaw(sql, new SqlParameter(...))</c>,
    /// or a single <c>new object[] { ... }</c>): positional values bind <c>{n}</c>; <c>*Parameter</c> objects bind by name.
    /// </summary>
    private static List<CallBinding> CollectExtraArgumentBindings(
        InvocationExpressionSyntax invocation,
        int sqlArgIndex,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var result = new List<CallBinding>();
        var args = invocation.ArgumentList.Arguments;
        var extra = new List<(ExpressionSyntax Expression, ParameterDirection? RefDirection)>();
        for (var i = sqlArgIndex + 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.NameColon is not null && arg.NameColon.Name.Identifier.ValueText != "parameters")
            {
                continue;
            }

            var argType = model.GetTypeInfo(arg.Expression, cancellationToken).Type;
            if (argType?.Name == "CancellationToken")
            {
                continue;
            }

            extra.Add((arg.Expression, RefKindDirection(arg)));
        }

        if (extra.Count == 1 && UnwrapValueExpression(extra[0].Expression) is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax)
        {
            extra = EnumerateCollectionElements(extra[0].Expression).Select(e => (e, (ParameterDirection?)null)).ToList();
        }

        foreach (var (expression, refDirection) in extra)
        {
            if (IsParameterCreation(expression, model, cancellationToken, out var creation))
            {
                result.Add(ParseParameterCreation(creation, model, cancellationToken));
                continue;
            }

            if (UnwrapValueExpression(expression) is IdentifierNameSyntax id &&
                model.GetSymbolInfo(id, cancellationToken).Symbol is ILocalSymbol local &&
                local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer.Value: { } init } &&
                IsParameterCreation(init, model, cancellationToken, out var localCreation))
            {
                result.Add(ParseParameterCreation(localCreation, model, cancellationToken));
                continue;
            }

            var positional = new CallBinding(model) { Direction = refDirection };
            positional.SetValue(expression, model);
            result.Add(positional);
        }

        return result;
    }

    /// <summary>An <c>out</c> argument is an Output parameter, a <c>ref</c> argument InputOutput.</summary>
    private static ParameterDirection? RefKindDirection(ArgumentSyntax argument)
    {
        return argument.RefKindKeyword.Kind() switch
        {
            SyntaxKind.OutKeyword => ParameterDirection.Output,
            SyntaxKind.RefKeyword => ParameterDirection.InputOutput,
            _ => null,
        };
    }

    private static List<CallBinding> BindingsFromHoles(IEnumerable<SqlHole> holes)
    {
        var result = new List<CallBinding>();
        foreach (var hole in holes)
        {
            var binding = new CallBinding(hole.Model) { Name = hole.Name, IsHole = true };
            binding.SetValue(hole.Expression, hole.Model);
            result.Add(binding);
        }

        return result;
    }

    private static string StripBindPrefix(string name)
    {
        return name.TrimStart('@', ':', '?', '$');
    }

    private static bool NamesMatch(string? bindingName, string placeholderName)
    {
        return bindingName is not null &&
               string.Equals(StripBindPrefix(bindingName), StripBindPrefix(placeholderName), StringComparison.OrdinalIgnoreCase);
    }

    private enum ArgumentLayout
    {
        Ado,
        AddWithValue,
        Dapper,
    }

    /// <summary>One argument the call site passes, as far as it can be read from source.</summary>
    internal sealed class CallBinding
    {
        public CallBinding(SemanticModel model)
        {
            Model = model;
        }

        /// <summary>Gets or sets the parameter name as written, or null for a positional value.</summary>
        public string? Name { get; set; }

        /// <summary>Gets or sets the provider type enum member written at the call site (<c>Int</c>, <c>Varchar2</c>), if any.</summary>
        public string? DbType { get; set; }

        /// <summary>Gets or sets the direction written at the call site, if any.</summary>
        public ParameterDirection? Direction { get; set; }

        /// <summary>Gets or sets a value indicating whether a value expression is bound (even <c>null</c>).</summary>
        public bool HasValue { get; set; }

        /// <summary>Gets or sets a value indicating whether the binding is a non-constant value spliced into the SQL text.</summary>
        public bool IsHole { get; set; }

        /// <summary>Gets or sets the CLR type when it is known without a value expression (object properties).</summary>
        public string? ClrTypeOverride { get; set; }

        public ExpressionSyntax? Value { get; private set; }

        public SemanticModel Model { get; private set; }

        /// <summary>Gets the call-site direction: the written one, else Input when a value is bound, else unknown (null).</summary>
        public ParameterDirection? EffectiveDirection => Direction ?? (HasValue ? ParameterDirection.Input : null);

        public void SetValue(ExpressionSyntax value, SemanticModel model)
        {
            Value = value;
            Model = model;
            HasValue = true;
        }

        public string? ResolveClrType(CancellationToken cancellationToken)
        {
            return ClrTypeOverride ?? ClrTypeOfValue(Value, Model, cancellationToken);
        }
    }
}
