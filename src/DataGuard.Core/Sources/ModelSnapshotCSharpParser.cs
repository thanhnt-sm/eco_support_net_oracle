using System.Globalization;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.TypeCompatibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>Options limiting source-only EF snapshot extraction.</summary>
public sealed record ModelSnapshotParseOptions
{
    public int MaximumSourceBytes { get; init; } = 1_048_576;

    public int MaximumSyntaxNodes { get; init; } = 50_000;

    public bool Strict { get; init; } = true;
}

/// <summary>One non-secret parsing diagnostic for an unsupported snapshot construct.</summary>
public sealed record ModelSnapshotExtractionDiagnostic(string Code, string Message, int? Line = null);

/// <summary>Source-only snapshot extraction result; diagnostics make unsupported input visible.</summary>
public sealed record DesignTimeExtractionResult(
    IReadOnlyList<EntityDescriptor> Entities,
    IReadOnlyList<ModelSnapshotExtractionDiagnostic> Diagnostics);

/// <summary>Parses a bounded, fluent-API subset of generated EF Core C# snapshots without loading an assembly.</summary>
/// <remarks>
/// Properties are read from <c>b.Property&lt;T&gt;("Name")</c> and <c>b.Property(x =&gt; x.Name)</c> chains. The CLR type is
/// the generic argument (canonicalized through <see cref="ClrTypeNames"/>, nullable kept as <c>T?</c>); the lambda form has no
/// syntactic type, so it becomes <c>string</c> only when a string-only facet is configured and <c>object</c> otherwise.
/// <c>.IsUnicode(bool)</c> is emitted as <c>Annotations["IsUnicode"]</c> (the key <c>EfModelSource</c> uses) and
/// <c>.IsRequired(bool)</c> / nullable type syntax drive <see cref="PropertyDescriptor.IsNullable"/>.
/// </remarks>
public static class ModelSnapshotCSharpParser
{
    /// <summary>Annotation key for the EF unicode facet, shared with <see cref="EfModelSource"/> and the length rules.</summary>
    private const string IsUnicodeAnnotation = "IsUnicode";

    private const string ObjectTypeName = "object";

    private static readonly string[] StringOnlyFacets = { "HasMaxLength", "IsUnicode", "IsFixedLength" };

    public static DesignTimeExtractionResult Parse(string source, ModelSnapshotParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ModelSnapshotParseOptions();
        if (source.Length > options.MaximumSourceBytes)
        {
            return Failure("DG1301", "ModelSnapshot source exceeds the configured size limit.");
        }

        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        if (root.DescendantNodesAndSelf().Take(options.MaximumSyntaxNodes + 1).Count() > options.MaximumSyntaxNodes)
        {
            return Failure("DG1302", "ModelSnapshot source exceeds the configured syntax-node limit.");
        }

        var diagnostics = new List<ModelSnapshotExtractionDiagnostic>();
        if (tree.GetDiagnostics(cancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(new ModelSnapshotExtractionDiagnostic("DG1303", "ModelSnapshot source contains C# syntax errors."));
            return new DesignTimeExtractionResult(Array.Empty<EntityDescriptor>(), diagnostics);
        }

        var entities = new List<EntityDescriptor>();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsEntityInvocation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } || generic.TypeArgumentList.Arguments.Count != 1)
            {
                diagnostics.Add(Diagnostic(invocation, "DG1304", "Entity configuration has no concrete generic entity type."));
                continue;
            }

            if (invocation.ArgumentList.Arguments.Count != 1 || invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda || lambda.Body is not BlockSyntax block)
            {
                diagnostics.Add(Diagnostic(invocation, "DG1305", "Entity configuration must use a block lambda."));
                continue;
            }

            var entityName = generic.TypeArgumentList.Arguments[0].ToString();
            var parameter = lambda switch { SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText, ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 1 => parenthesized.ParameterList.Parameters[0].Identifier.ValueText, _ => string.Empty };
            if (string.IsNullOrEmpty(parameter))
            {
                diagnostics.Add(Diagnostic(invocation, "DG1305", "Entity configuration requires one named lambda parameter."));
                continue;
            }

            entities.Add(ParseEntity(entityName, parameter, block));
        }

        if (entities.Count == 0)
        {
            diagnostics.Add(new ModelSnapshotExtractionDiagnostic("DG1306", "No supported modelBuilder.Entity<T> configuration was found."));
        }

        return new DesignTimeExtractionResult(entities.OrderBy(entity => entity.Name, StringComparer.Ordinal).ToArray(), diagnostics);
    }

    private static EntityDescriptor ParseEntity(string entityName, string parameter, BlockSyntax block)
    {
        var table = entityName;
        var properties = new Dictionary<string, PropertyDescriptor>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invocation in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var chain = invocation.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
                .Select(MethodName)
                .Where(name => name is not null).Cast<string>().ToArray();
            if (chain.Contains("ToTable", StringComparer.Ordinal) && TryFirstString(invocation, out var tableName))
            {
                table = tableName;
            }

            if (MethodName(invocation) == "HasKey" && IsReceiver(invocation, parameter))
            {
                keys.UnionWith(KeyNames(invocation));
            }

            if (ParseProperty(invocation, parameter) is { } property)
            {
                properties[property.Name] = property;
            }
        }

        return new EntityDescriptor($"snapshot:{entityName}", entityName, entityName, table,
            properties.Values.Select(property => property with { IsPrimaryKey = keys.Contains(property.Name) }).OrderBy(property => property.Name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Parses one <c>b.Property&lt;T&gt;("Name")</c> or <c>b.Property(x =&gt; x.Name)</c> fluent chain.</summary>
    private static PropertyDescriptor? ParseProperty(InvocationExpressionSyntax invocation, string parameter)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: { } name }
            || name.Identifier.ValueText != "Property"
            || !IsReceiver(invocation, parameter)
            || PropertyName(invocation) is not { Length: > 0 } propertyName)
        {
            return null;
        }

        var calls = FluentCalls(invocation);
        var declaredType = name is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic
            ? generic.TypeArgumentList.Arguments[0]
            : null;
        bool? isUnicode = calls.TryGetValue("IsUnicode", out var unicodeCall) ? BooleanArgument(unicodeCall, defaultValue: true) : null;
        var clrType = declaredType is not null
            ? NormalizeTypeText(declaredType)
            : StringOnlyFacets.Any(calls.ContainsKey) ? ClrTypeNames.String : ObjectTypeName;

        // Explicit IsRequired wins. Otherwise T?/Nullable<T> and reference types are optional and a known value type is
        // required; the lambda form (no syntactic type) keeps EF's "optional unless IsRequired" reading.
        var isNullable = calls.TryGetValue("IsRequired", out var requiredCall)
            ? !BooleanArgument(requiredCall, defaultValue: true)
            : declaredType is null || IsNullableTypeSyntax(declaredType) || !IsKnownValueType(clrType);

        var annotations = isUnicode is { } unicode
            ? new Dictionary<string, object?>(StringComparer.Ordinal) { [IsUnicodeAnnotation] = unicode }
            : null;
        return new PropertyDescriptor(
            propertyName,
            clrType,
            StringArgument(calls, "HasColumnName") ?? propertyName,
            StringArgument(calls, "HasColumnType"),
            isNullable,
            IntegerArgument(calls, "HasMaxLength"),
            IsPrimaryKey: false,
            IsForeignKey: false,
            Annotations: annotations);
    }

    private static string? PropertyName(InvocationExpressionSyntax invocation) => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
    {
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
        LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax, Name: IdentifierNameSyntax member } } => member.Identifier.ValueText,
        InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } nameOf
            when nameOf.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } target => LastIdentifier(target),
        _ => null,
    };

    /// <summary>Collects the methods chained after <paramref name="root"/> (<c>root.A().B()</c>); the first call of a name wins.</summary>
    private static Dictionary<string, InvocationExpressionSyntax> FluentCalls(InvocationExpressionSyntax root)
    {
        var calls = new Dictionary<string, InvocationExpressionSyntax>(StringComparer.Ordinal);
        SyntaxNode current = root;
        while (current.Parent is MemberAccessExpressionSyntax member && member.Expression == current && member.Parent is InvocationExpressionSyntax next)
        {
            calls.TryAdd(member.Name.Identifier.ValueText, next);
            current = next;
        }

        return calls;
    }

    private static string? MethodName(InvocationExpressionSyntax invocation) => (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText;

    private static bool IsReceiver(InvocationExpressionSyntax invocation, string parameter)
        => invocation.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } && receiver.Identifier.ValueText == parameter;

    /// <summary>Key names from <c>HasKey(x =&gt; x.Id)</c>, <c>HasKey(x =&gt; new { x.A, x.B })</c> or <c>HasKey("A", "B")</c>.</summary>
    private static IEnumerable<string> KeyNames(InvocationExpressionSyntax invocation)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            switch (argument.Expression)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    yield return literal.Token.ValueText;
                    break;
                case LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax body } when body.Name is IdentifierNameSyntax member:
                    yield return member.Identifier.ValueText;
                    break;
                case LambdaExpressionSyntax lambda when lambda.Body is AnonymousObjectCreationExpressionSyntax anonymous:
                    foreach (var keyPart in anonymous.Initializers.Select(initializer => LastIdentifier(initializer.Expression)).OfType<string>())
                    {
                        yield return keyPart;
                    }

                    break;
            }
        }
    }

    private static string? LastIdentifier(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax { Name: IdentifierNameSyntax member } => member.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null,
    };

    /// <summary>
    /// Canonicalizes the generic argument through <see cref="ClrTypeNames.Normalize"/> (<c>System.Int32</c> ⇒ <c>int</c>,
    /// <c>Nullable&lt;DateTime&gt;</c> ⇒ <c>DateTime?</c>, <c>string?</c> ⇒ <c>string</c>); a type the tables do not know keeps
    /// its source spelling.
    /// </summary>
    private static string NormalizeTypeText(TypeSyntax type)
    {
        var text = type.ToString().Replace(" ", string.Empty, StringComparison.Ordinal);
        var nullable = IsNullableTypeSyntax(type);
        var canonical = ClrTypeNames.Normalize(text);
        if (canonical is null)
        {
            return text;
        }

        // Reference types carry nullability in IsNullable only, so the length rules keep matching "string".
        return nullable && IsKnownValueType(canonical) ? canonical + "?" : canonical;
    }

    private static bool IsNullableTypeSyntax(TypeSyntax type)
    {
        if (type is NullableTypeSyntax)
        {
            return true;
        }

        var text = type.ToString().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (text.StartsWith("global::", StringComparison.Ordinal))
        {
            text = text["global::".Length..];
        }

        return text.StartsWith("Nullable<", StringComparison.Ordinal) || text.StartsWith("System.Nullable<", StringComparison.Ordinal);
    }

    private static bool IsKnownValueType(string clrType)
        => ClrTypeNames.Normalize(clrType) is { } canonical && canonical is not ClrTypeNames.String and not ClrTypeNames.ByteArray;

    private static bool BooleanArgument(InvocationExpressionSyntax invocation, bool defaultValue) => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
    {
        null => defaultValue,
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression) => false,
        LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => true,
        _ => defaultValue,
    };

    private static string? StringArgument(IReadOnlyDictionary<string, InvocationExpressionSyntax> calls, string method)
        => calls.TryGetValue(method, out var call) && TryFirstString(call, out var value) ? value : null;

    private static int? IntegerArgument(IReadOnlyDictionary<string, InvocationExpressionSyntax> calls, string method)
        => calls.TryGetValue(method, out var call)
            && call.ArgumentList.Arguments.FirstOrDefault() is { Expression: { } expression }
            && int.TryParse(expression.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool IsEntityInvocation(InvocationExpressionSyntax invocation) => InvocationChain(invocation).FirstOrDefault() == "Entity";

    private static IEnumerable<string> InvocationChain(SyntaxNode node) => node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
        .Select(MethodName).Where(name => name is not null).Cast<string>();

    private static bool TryFirstString(InvocationExpressionSyntax invocation, out string value) => (value = invocation.ArgumentList.Arguments.Select(argument => argument.Expression).OfType<LiteralExpressionSyntax>().FirstOrDefault()?.Token.ValueText ?? string.Empty).Length > 0;

    private static ModelSnapshotExtractionDiagnostic Diagnostic(SyntaxNode node, string code, string message) => new(code, message, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

    private static DesignTimeExtractionResult Failure(string code, string message) => new(Array.Empty<EntityDescriptor>(), new[] { new ModelSnapshotExtractionDiagnostic(code, message) });
}
