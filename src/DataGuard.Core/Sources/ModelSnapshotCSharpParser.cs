using DataGuard.Core.Abstractions;
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
public static class ModelSnapshotCSharpParser
{
    public static DesignTimeExtractionResult Parse(string source, ModelSnapshotParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ModelSnapshotParseOptions();
        if (source.Length > options.MaximumSourceBytes)
        {
            return Failure(options, "DG1301", "ModelSnapshot source exceeds the configured size limit.");
        }

        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        if (root.DescendantNodesAndSelf().Take(options.MaximumSyntaxNodes + 1).Count() > options.MaximumSyntaxNodes)
        {
            return Failure(options, "DG1302", "ModelSnapshot source exceeds the configured syntax-node limit.");
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

            entities.Add(ParseEntity(entityName, parameter, block, diagnostics));
        }

        if (entities.Count == 0)
        {
            diagnostics.Add(new ModelSnapshotExtractionDiagnostic("DG1306", "No supported modelBuilder.Entity<T> configuration was found."));
        }

        return new DesignTimeExtractionResult(entities.OrderBy(entity => entity.Name, StringComparer.Ordinal).ToArray(), diagnostics);
    }

    private static EntityDescriptor ParseEntity(string entityName, string parameter, BlockSyntax block, ICollection<ModelSnapshotExtractionDiagnostic> diagnostics)
    {
        var table = entityName;
        var properties = new Dictionary<string, PropertyDescriptor>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invocation in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var chain = invocation.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
                .Select(candidate => (candidate.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText)
                .Where(name => name is not null).Cast<string>().ToArray();
            if (chain.Contains("ToTable", StringComparer.Ordinal) && TryFirstString(invocation, out var tableName))
            {
                table = tableName;
            }

            if (chain.Contains("HasKey", StringComparer.Ordinal) && TryLambdaProperty(invocation, parameter, out var key))
            {
                keys.Add(key);
            }

            if (!chain.Contains("Property", StringComparer.Ordinal) || !TryLambdaProperty(invocation, parameter, out var propertyName))
            {
                continue;
            }

            var column = FindStringArgument(invocation, "HasColumnName") ?? propertyName;
            var columnType = FindStringArgument(invocation, "HasColumnType");
            var maxLength = FindIntegerArgument(invocation, "HasMaxLength");
            var nullable = !chain.Contains("IsRequired", StringComparer.Ordinal);
            properties[propertyName] = new PropertyDescriptor(propertyName, "object", column, columnType, nullable, maxLength, false, false);
        }

        return new EntityDescriptor($"snapshot:{entityName}", entityName, entityName, table,
            properties.Values.Select(property => property with { IsPrimaryKey = keys.Contains(property.Name) }).OrderBy(property => property.Name, StringComparer.Ordinal).ToArray());
    }

    private static bool IsEntityInvocation(InvocationExpressionSyntax invocation) => InvocationChain(invocation).FirstOrDefault() == "Entity";

    private static IEnumerable<string> InvocationChain(SyntaxNode node) => node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
        .Select(invocation => (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText).Where(name => name is not null).Cast<string>();

    private static bool TryLambdaProperty(InvocationExpressionSyntax invocation, string parameter, out string property)
    {
        property = string.Empty;
        var lambda = invocation.DescendantNodes().OfType<LambdaExpressionSyntax>().FirstOrDefault();
        if (lambda?.Body is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax, Name: IdentifierNameSyntax name })
        {
            property = name.Identifier.ValueText;
            return true;
        }

        return false;
    }

    private static bool TryFirstString(InvocationExpressionSyntax invocation, out string value) => (value = invocation.ArgumentList.Arguments.Select(argument => argument.Expression).OfType<LiteralExpressionSyntax>().FirstOrDefault()?.Token.ValueText ?? string.Empty).Length > 0;

    private static string? FindStringArgument(InvocationExpressionSyntax invocation, string method) => invocation.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault(candidate => (candidate.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText == method) is { } match && TryFirstString(match, out var value) ? value : null;

    private static int? FindIntegerArgument(InvocationExpressionSyntax invocation, string method)
    {
        var match = invocation.AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(candidate => (candidate.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText == method);
        return match?.ArgumentList.Arguments.FirstOrDefault() is { Expression: { } expression }
            && int.TryParse(expression.ToString(), out var value) ? value : null;
    }

    private static ModelSnapshotExtractionDiagnostic Diagnostic(SyntaxNode node, string code, string message) => new(code, message, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

    private static DesignTimeExtractionResult Failure(ModelSnapshotParseOptions options, string code, string message) => new(Array.Empty<EntityDescriptor>(), new[] { new ModelSnapshotExtractionDiagnostic(code, message) });
}
