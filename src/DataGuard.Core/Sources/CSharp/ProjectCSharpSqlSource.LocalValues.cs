using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DataGuard.Core.Sources;

/// <summary>Conservative, request-owned local initializer stability checks.</summary>
public sealed partial class ProjectCSharpSqlSource
{
    /// <summary>
    /// Request-owned proof of a single initializer for a local's entire declaring scope, not reaching definitions.
    /// Any write or reference escape, even after the use, rejects that proof. Never caches a use-site SQL value.
    /// </summary>
    private sealed class LocalValueStability
    {
        private readonly Dictionary<SyntaxNode, HashSet<ISymbol>> writesByScope = new();

        public bool IsStable(ILocalSymbol local, SemanticModel model, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (local.RefKind != RefKind.None || local.DeclaringSyntaxReferences.Length != 1 ||
                local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax { Initializer: not null } declaration)
            {
                return false;
            }

            var scope = GetScope(declaration);
            if (!writesByScope.TryGetValue(scope, out var writes))
            {
                writes = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                foreach (var node in scope.DescendantNodes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (node)
                    {
                        case AssignmentExpressionSyntax assignment:
                            AddTarget(assignment.Left, model, writes, cancellationToken);
                            break;
                        case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                            AddTarget(argument.Expression, model, writes, cancellationToken);
                            break;
                        case ArgumentSyntax argument when argument.Expression is IdentifierNameSyntax or ParenthesizedExpressionSyntax or PostfixUnaryExpressionSyntax:
                            // `in` can be omitted at the call site; the bound parameter still permits a ref escape.
                            if (model.GetOperation(argument, cancellationToken) is IArgumentOperation { Parameter.RefKind: not RefKind.None })
                            {
                                AddTarget(argument.Expression, model, writes, cancellationToken);
                            }
                            break;
                        case RefExpressionSyntax reference:
                            AddTarget(reference.Expression, model, writes, cancellationToken);
                            break;
                        case MakeRefExpressionSyntax reference:
                            AddTarget(reference.Expression, model, writes, cancellationToken);
                            break;
                        case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                            AddTarget(prefix.Operand, model, writes, cancellationToken);
                            break;
                        case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression):
                            AddTarget(postfix.Operand, model, writes, cancellationToken);
                            break;
                    }
                }

                // A canceled traversal never publishes a partial proof. The cache dies with this extraction run.
                cancellationToken.ThrowIfCancellationRequested();
                writesByScope.Add(scope, writes);
            }

            return !writes.Contains(local);
        }

        private static void AddTarget(ExpressionSyntax target, SemanticModel model, HashSet<ISymbol> writes, CancellationToken cancellationToken)
        {
            Stack<ExpressionSyntax>? pending = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (target)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        target = parenthesized.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        target = suppression.Operand;
                        continue;
                    case IdentifierNameSyntax identifier when model.GetSymbolInfo(identifier, cancellationToken).Symbol is ILocalSymbol local:
                        writes.Add(local);
                        break;
                    case TupleExpressionSyntax tuple:
                        pending ??= new Stack<ExpressionSyntax>();
                        foreach (var argument in tuple.Arguments)
                        {
                            pending.Push(argument.Expression);
                        }
                        break;
                }

                if (pending is not { Count: > 0 })
                {
                    return;
                }

                target = pending.Pop();
            }
        }
    }
}
