using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DataGuard.Core.Sources;

/// <summary>Bounded compiler proofs for string members; never executes getters or application code.</summary>
public sealed partial class ProjectCSharpSqlSource
{
    private static bool TryResolveConstantProperty(
        IPropertySymbol property, SemanticModel model, CancellationToken cancellationToken,
        SqlSourceMapBuilder? sourceMap, out string? text)
    {
        text = null;
        if (property.IsVirtual || property.IsOverride || property.IsAbstract || property.IsIndexer ||
            property.RefKind != RefKind.None || property.SetMethod is not null ||
            property.GetMethod is not { IsVirtual: false, IsOverride: false, IsAbstract: false } ||
            property.DeclaringSyntaxReferences.Length != 1 ||
            property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not PropertyDeclarationSyntax { ExpressionBody.Expression: { } body } declaration)
        {
            return false;
        }

        var declarationModel = MemberDeclarationModel(model, declaration.SyntaxTree);
        if (declarationModel is null)
        {
            return false;
        }

        var constant = declarationModel.GetConstantValue(body, cancellationToken);
        if (!constant.HasValue || constant.Value is not string value)
        {
            return false;
        }

        text = value;
        if (sourceMap is not null)
        {
            if (body is LiteralExpressionSyntax literal)
            {
                sourceMap.AppendToken(literal.Token, literal.Token.Text.StartsWith("@\"", StringComparison.Ordinal) ? 2 : 1, 1);
            }
            else
            {
                // Compiler folding is proved, but character provenance of the folded expression is not exact.
                sourceMap.AppendMapped(value, body.GetLocation());
            }
        }

        return true;
    }

    private static void RejectUnprovedMember(
        ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken, StringResolutionState state)
    {
        var symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is not (IFieldSymbol { IsConst: false, Type.SpecialType: SpecialType.System_String } or
            IPropertySymbol { Type.SpecialType: SpecialType.System_String }))
        {
            return;
        }

        state.Rejection = SqlValueRejection.UnknownMember;
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            var evidenceExpression = syntax switch
            {
                VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                PropertyDeclarationSyntax property => property.Initializer?.Value ?? property.ExpressionBody?.Expression,
                _ => null,
            };
            var declarationModel = MemberDeclarationModel(model, syntax.SyntaxTree);
            if (evidenceExpression is null || declarationModel is null)
            {
                continue;
            }

            // Only a boolean crosses the rejection boundary; initializer SQL is never a publishable value.
            var constant = declarationModel.GetConstantValue(evidenceExpression, cancellationToken);
            state.SqlEvidence |= constant.HasValue && constant.Value is string value && IsSqlString(value);
        }
    }

    private static SemanticModel? MemberDeclarationModel(SemanticModel model, SyntaxTree declarationTree) =>
        model.SyntaxTree == declarationTree ? model :
        model.Compilation.ContainsSyntaxTree(declarationTree) ? model.Compilation.GetSemanticModel(declarationTree) : null;

    private static void ProbeConcatOperand(
        IOperation operand, SemanticModel model, CancellationToken cancellationToken, int depth,
        HashSet<SyntaxNode> visited, StringResolutionState state)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 10 || state.Rejection != SqlValueRejection.None)
        {
            return;
        }

        // Expanded params arrays can have the entire invocation as Syntax. Inspect bound elements so the
        // invocation's visited guard cannot conceal a rejected member inside that implicit array.
        if (operand is IArrayCreationOperation { Initializer: { } initializer })
        {
            foreach (var element in initializer.ElementValues)
            {
                ProbeConcatOperand(element, model, cancellationToken, depth + 1, visited, state);
            }

            return;
        }

        // Newer frameworks bind expanded Concat params to a ReadOnlySpan collection expression instead.
        if (operand is ICollectionExpressionOperation { ConstructMethod: null } collection)
        {
            foreach (var element in collection.Elements)
            {
                ProbeConcatOperand(element, model, cancellationToken, depth + 1, visited, state);
            }

            return;
        }

        if (operand is IConversionOperation { OperatorMethod: null, Operand: IArrayCreationOperation or ICollectionExpressionOperation } conversion)
        {
            ProbeConcatOperand(conversion.Operand, model, cancellationToken, depth + 1, visited, state);
            return;
        }

        if (operand.Syntax is ExpressionSyntax expression)
        {
            TryResolveStringCore(expression, model, cancellationToken, depth + 1, visited, null, state);
        }
    }
}
