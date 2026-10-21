using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DataGuard.Core.Sources;

/// <summary>Bounded SQL-slot recognition for command text, without evaluating command instances.</summary>
public sealed partial class ProjectCSharpSqlSource
{
    private readonly record struct CommandSqlArgument(ArgumentSyntax? Argument, bool KnownSlot, bool Unsupported = false);

    private static CommandSqlArgument FindCommandSqlArgument(
        BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken)
    {
        var arguments = creation.ArgumentList?.Arguments ?? default;
        if (arguments.Count == 0)
        {
            return default;
        }

        if (!IsCommandTextCarrier(model.GetTypeInfo(creation, cancellationToken).Type))
        {
            return new(arguments[0], false);
        }

        if (model.GetOperation(creation, cancellationToken) is IObjectCreationOperation { Constructor: { } constructor } operation)
        {
            var parameters = constructor.Parameters.Where(p => p.Type.SpecialType == SpecialType.System_String && IsCommandSqlParameter(p)).ToArray();
            if (parameters.Length > 1)
            {
                return new(null, true, true);
            }

            if (parameters.Length == 1)
            {
                foreach (var argument in operation.Arguments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (argument.ArgumentKind != ArgumentKind.Explicit || !SymbolEqualityComparer.Default.Equals(argument.Parameter, parameters[0]))
                    {
                        continue;
                    }

                    foreach (var syntax in arguments)
                    {
                        if (syntax.Span.Contains(argument.Syntax.Span))
                        {
                            return new(syntax, true);
                        }
                    }
                }

                return new(null, true, true);
            }

            // A bound constructor with no SQL role may carry a connection string or another label.
            return default;
        }

        // Keep legacy SQL-shaped discovery, but do not invent a parameter role from an argument's value.
        return new(arguments[0], false);
    }

    private static bool IsKnownCommandTextAssignment(AssignmentExpressionSyntax assignment, SemanticModel model, CancellationToken cancellationToken)
    {
        var symbol = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        return symbol switch
        {
            IPropertySymbol { Name: "CommandText", Type.SpecialType: SpecialType.System_String } property => IsCommandTextCarrier(property.ContainingType),
            IFieldSymbol { Name: "CommandText", Type.SpecialType: SpecialType.System_String } field => IsCommandTextCarrier(field.ContainingType),
            _ => false,
        };
    }

    // Preserve the existing Oracle source convention's text alias without treating arbitrary labels as SQL.
    private static bool IsCommandSqlParameter(IParameterSymbol parameter) =>
        parameter.Name is "cmdText" or "commandText" or "sql" ||
        (parameter.Name == "text" && parameter.ContainingSymbol is IMethodSymbol { ContainingType.Name: "OracleCommand" });

    private static bool IsKnownCommandTypeAssignment(AssignmentExpressionSyntax assignment, SemanticModel model, CancellationToken cancellationToken)
    {
        var symbol = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        return symbol?.Name == "CommandType" && IsCommandTextCarrier(symbol.ContainingType);
    }

    private static bool IsCommandTextCarrier(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "DbCommand" && current.ContainingNamespace.ToDisplayString() == "System.Data.Common")
            {
                return true;
            }

            // Match the existing source-only provider conventions when provider assemblies are absent from a fixture.
            if (current.Name is "SqlCommand" or "OracleCommand" or "NpgsqlCommand" or "MySqlCommand")
            {
                return true;
            }
        }

        return false;
    }
}
