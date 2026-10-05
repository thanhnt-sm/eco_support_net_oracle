using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>
/// The per-file extraction passes.
/// </summary>
public sealed partial class ProjectCSharpSqlSource
{
    private static readonly HashSet<string> TargetMethodNames = new(StringComparer.Ordinal)
    {
        "Query", "QueryAsync", "QueryFirst", "QueryFirstAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "QueryMultiple", "QueryMultipleAsync", "QueryUnbufferedAsync",
        "Execute", "ExecuteAsync", "ExecuteScalar", "ExecuteScalarAsync",
        "ExecuteReader", "ExecuteReaderAsync",
        "ExecuteNonQuery", "ExecuteNonQueryAsync",
        "FromSqlRaw", "FromSqlInterpolated", "FromSql",
        "ExecuteSqlRaw", "ExecuteSqlRawAsync", "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync",
        "ExecuteSql", "ExecuteSqlAsync", "SqlQuery", "SqlQueryRaw",
    };

    private static bool IsEfSqlMethod(string methodName)
    {
        return methodName.StartsWith("FromSql", StringComparison.Ordinal) ||
               methodName.StartsWith("ExecuteSql", StringComparison.Ordinal) ||
               methodName.StartsWith("SqlQuery", StringComparison.Ordinal);
    }

    private static bool IsCandidateInvocation(InvocationExpressionSyntax invocation, out string methodName)
    {
        methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => string.Empty,
        };

        return TargetMethodNames.Contains(methodName);
    }

    /// <summary>Returns the receiver of <c>x.M()</c> or of <c>x?.M()</c> (the conditional-access target), else null.</summary>
    private static ExpressionSyntax? GetInvocationReceiver(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Expression;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax)
        {
            for (SyntaxNode? node = invocation; node?.Parent is not null; node = node.Parent)
            {
                if (node.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == node)
                {
                    return conditional.Expression;
                }
            }
        }

        return null;
    }

    private static bool IsStoredProcedureCommandTypeArgument(ArgumentSyntax arg)
    {
        var argText = arg.Expression.ToString().Trim();
        return argText == "StoredProcedure" || argText.EndsWith(".StoredProcedure", StringComparison.Ordinal);
    }

    /// <summary>
    /// The member body that bounds statement-order analysis of a node: method, constructor, accessor, local function or
    /// lambda; the compilation unit for top-level statements.
    /// </summary>
    private static SyntaxNode GetScope(SyntaxNode node)
    {
        return node.Ancestors().FirstOrDefault(a => a is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax or AnonymousFunctionExpressionSyntax or PropertyDeclarationSyntax)
            ?? node.SyntaxTree.GetRoot();
    }

    /// <summary>
    /// True when the node's member (method, constructor, accessor, local function, property, field) or any containing
    /// type has <c>[SkipContractCheck]</c>, matched by name on the syntax and, for partial or aliased declarations, on the symbol.
    /// </summary>
    private static bool HasSkipContractCheck(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var attributeLists = ancestor switch
            {
                MemberDeclarationSyntax member => member.AttributeLists,
                LocalFunctionStatementSyntax localFunction => localFunction.AttributeLists,
                AccessorDeclarationSyntax accessor => accessor.AttributeLists,
                LambdaExpressionSyntax lambda => lambda.AttributeLists,
                _ => default,
            };

            if (attributeLists.Count > 0 && HasAttribute(attributeLists, "SkipContractCheck"))
            {
                return true;
            }

            if (ancestor is BaseTypeDeclarationSyntax or BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or BasePropertyDeclarationSyntax)
            {
                var symbol = model.GetDeclaredSymbol(ancestor, cancellationToken);
                if (symbol is not null && symbol.GetAttributes().Any(a => a.AttributeClass?.Name is "SkipContractCheckAttribute" or "SkipContractCheck"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>One file's passes. Candidates are returned in pass order and emitted by <see cref="ExtractionRun"/>.</summary>
    private sealed class TreeScan
    {
        private readonly ExtractionRun _run;
        private readonly SemanticModel _model;
        private readonly SyntaxNode _root;
        private readonly CancellationToken _ct;

        public TreeScan(ExtractionRun run, SyntaxTree tree, SemanticModel model)
        {
            _run = run;
            _model = model;
            _ct = run.CancellationToken;
            _root = tree.GetRoot(_ct);
        }

        public IReadOnlyList<SqlCandidate> ScanCallSites()
        {
            var candidates = new List<SqlCandidate>();
            ScanInvocations(candidates);
            ScanCommandTextAssignments(candidates);
            ScanStoredProcedureCommands(candidates);
            ScanCommandCreations(candidates);
            ScanBaseConstructorCalls(candidates);
            return candidates;
        }

        /// <summary>Unreferenced SQL constants/static readonly fields whose text no call site has used.</summary>
        public IReadOnlyList<SqlCandidate> ScanUnreferencedConstants()
        {
            var candidates = new List<SqlCandidate>();
            foreach (var field in _root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                _ct.ThrowIfCancellationRequested();
                if (!field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword) || m.IsKind(SyntaxKind.ReadOnlyKeyword)))
                {
                    continue;
                }

                var classDecl = field.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classDecl != null && IsEntityClass(classDecl))
                {
                    continue;
                }

                foreach (var variable in field.Declaration.Variables)
                {
                    if (variable.Initializer == null)
                    {
                        continue;
                    }

                    var sqlText = ResolveSql(variable.Initializer.Value, variable.GetLocation(), null);
                    if (string.IsNullOrWhiteSpace(sqlText) || !IsStandaloneSqlConstant(sqlText) || _run.SeenSqlTexts.Contains(sqlText.Trim()))
                    {
                        continue;
                    }

                    candidates.Add(new SqlCandidate
                    {
                        SqlText = sqlText,
                        Location = variable.GetLocation(),
                        AnchorNode = variable,
                        Model = _model,
                    });
                }
            }

            return candidates;
        }

        /// <summary>Resolves SQL text; a literal over the size cap is reported once and treated as absent.</summary>
        private string? ResolveSql(ExpressionSyntax expression, Location location, List<SqlHole>? holes)
        {
            var text = TryResolveSql(expression, _model, holes, _ct);
            if (text is not null && text.Length > MaxSqlLiteralLength)
            {
                _run.ReportOversized(location, text.Length);
                return null;
            }

            return text;
        }

        private string? ProviderHintOf(ExpressionSyntax? receiver)
        {
            if (receiver is null)
            {
                return null;
            }

            var typeName = _model.GetTypeInfo(receiver, _ct).Type?.Name;
            if (string.IsNullOrEmpty(typeName) || typeName == "?")
            {
                typeName = (receiver as IdentifierNameSyntax)?.Identifier.ValueText;
            }

            return InferProviderHint(typeName);
        }

        // 1. Invocations (Query<T>, Execute, FromSqlRaw, SqlQuery, conn?.Query<T>, ...).
        private void ScanInvocations(List<SqlCandidate> candidates)
        {
            foreach (var invocation in _root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                _ct.ThrowIfCancellationRequested();
                if (!IsCandidateInvocation(invocation, out var methodName))
                {
                    continue;
                }

                var args = invocation.ArgumentList.Arguments;
                var isStoredProcedure = args.Any(IsStoredProcedureCommandTypeArgument);
                var holes = new List<SqlHole>();
                string sqlText;
                string? procedureName = null;
                var sqlArgIndex = -1;
                if (isStoredProcedure)
                {
                    // commandType: CommandType.StoredProcedure (named or positional): the first string argument is the
                    // procedure name, which is intentionally not statement-shaped.
                    string? name = null;
                    for (var i = 0; i < args.Count; i++)
                    {
                        if (args[i].NameColon?.Name.Identifier.ValueText == "commandType" || IsStoredProcedureCommandTypeArgument(args[i]))
                        {
                            continue;
                        }

                        name = ResolveSql(args[i].Expression, invocation.GetLocation(), null);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            sqlArgIndex = i;
                            break;
                        }
                    }

                    if (name is null || !IsProcedureName(name))
                    {
                        continue;
                    }

                    procedureName = name.Trim();
                    sqlText = $"EXEC {procedureName}";
                }
                else
                {
                    var isEf = IsEfSqlMethod(methodName);
                    sqlText = string.Empty;
                    for (var i = 0; i < args.Count; i++)
                    {
                        var argHoles = new List<SqlHole>();
                        var resolved = ResolveSql(args[i].Expression, invocation.GetLocation(), argHoles);
                        if (!string.IsNullOrWhiteSpace(resolved) && (isEf || IsSqlString(resolved)))
                        {
                            sqlText = resolved;
                            sqlArgIndex = i;
                            holes = argHoles;
                            break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(sqlText))
                    {
                        continue;
                    }
                }

                var (targetTypeName, typeSymbol) = ResolveTargetType(invocation, _model, _ct);
                IReadOnlyList<PropertyDescriptor> expectedProperties = Array.Empty<PropertyDescriptor>();
                if (!IsScalarTargetType(typeSymbol, targetTypeName))
                {
                    if (typeSymbol != null && typeSymbol.TypeKind != TypeKind.Error)
                    {
                        expectedProperties = ExtractPropertiesFromSymbol(typeSymbol);
                    }

                    if (expectedProperties.Count == 0 && !string.IsNullOrEmpty(targetTypeName) && _run.SyntaxTypes.TryGetValue(targetTypeName, out var typeDecls))
                    {
                        expectedProperties = ExtractPropertiesFromSyntax(typeDecls);
                    }
                }

                var bindings = new List<CallBinding>();
                var argumentsKnown = true;
                if (IsEfSqlMethod(methodName))
                {
                    bindings.AddRange(CollectExtraArgumentBindings(invocation, sqlArgIndex, _model, _ct));
                }
                else if (FindDapperParamArgument(args, sqlArgIndex) is { } paramArg)
                {
                    var expanded = ExpandDapperParameter(paramArg, _model, GetScope(invocation), invocation.SpanStart, 0, _ct);
                    bindings.AddRange(expanded);

                    // A synthesized "EXEC name" call whose param object yields nothing (a method result, an object-typed
                    // value, DynamicParameters filled elsewhere) has an argument list the extractor cannot see.
                    argumentsKnown = !isStoredProcedure || expanded.Count > 0 || IsVisiblyEmptyDapperParameter(paramArg);
                }

                bindings.AddRange(BindingsFromHoles(holes));

                candidates.Add(new SqlCandidate
                {
                    SqlText = sqlText,
                    Location = invocation.GetLocation(),
                    AnchorNode = invocation,
                    Model = _model,
                    SourceKey = invocation.Span,
                    TargetTypeName = targetTypeName,
                    ExpectedProperties = expectedProperties,
                    ProviderHint = ProviderHintOf(GetInvocationReceiver(invocation)),
                    IsStoredProcedure = isStoredProcedure,
                    ProcedureRawName = procedureName,
                    Bindings = bindings,
                    ArgumentsKnown = argumentsKnown,
                });
            }
        }

        // null/default or an anonymous object without members: Dapper sends no parameters, and that is visible.
        private static bool IsVisiblyEmptyDapperParameter(ExpressionSyntax paramArg)
        {
            var inner = UnwrapValueExpression(paramArg);
            return inner is LiteralExpressionSyntax || inner.IsKind(SyntaxKind.DefaultLiteralExpression) || inner is AnonymousObjectCreationExpressionSyntax;
        }

        private static ExpressionSyntax? FindDapperParamArgument(SeparatedSyntaxList<ArgumentSyntax> args, int sqlArgIndex)
        {
            var named = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "param");
            if (named is not null)
            {
                return named.Expression;
            }

            var next = sqlArgIndex + 1;
            return sqlArgIndex >= 0 && next < args.Count && args[next].NameColon is null && !IsStoredProcedureCommandTypeArgument(args[next])
                ? args[next].Expression
                : null;
        }

        // 2. CommandText assignments (cmd.CommandText = "SELECT ...", new XCommand { CommandText = "..." }).
        private void ScanCommandTextAssignments(List<SqlCandidate> candidates)
        {
            foreach (var assignment in _root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                _ct.ThrowIfCancellationRequested();
                if (GetAssignedMemberName(assignment) is not "CommandText")
                {
                    continue;
                }

                var holes = new List<SqlHole>();
                var sqlText = ResolveSql(assignment.Right, assignment.GetLocation(), holes);
                if (string.IsNullOrWhiteSpace(sqlText) || !IsSqlString(sqlText))
                {
                    continue;
                }

                var (receiverName, providerHint) = DescribeCommandTarget(assignment);
                var scope = GetScope(assignment);
                var bindings = new List<CallBinding>();
                if (receiverName is not null)
                {
                    var anchor = FindExecuteAnchor(receiverName, scope, assignment.SpanStart) ?? int.MaxValue;
                    bindings.AddRange(CollectCommandBindings(receiverName, scope, anchor, _model, _ct));
                }

                bindings.AddRange(BindingsFromHoles(holes));
                candidates.Add(new SqlCandidate
                {
                    SqlText = sqlText,
                    Location = assignment.GetLocation(),
                    AnchorNode = assignment,
                    Model = _model,
                    SourceKey = assignment.Span,
                    ProviderHint = providerHint,
                    Bindings = bindings,
                });
            }
        }

        // 2b. CommandType = CommandType.StoredProcedure: ADO.NET procedure calls. The procedure name is the last
        //     CommandText (assignment, initializer or constructor argument) written before the command executes.
        private void ScanStoredProcedureCommands(List<SqlCandidate> candidates)
        {
            foreach (var assignment in _root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                _ct.ThrowIfCancellationRequested();
                var rightText = assignment.Right.ToString().Trim();
                if ((rightText != "StoredProcedure" && !rightText.EndsWith(".StoredProcedure", StringComparison.Ordinal)) ||
                    GetAssignedMemberName(assignment) is not "CommandType")
                {
                    continue;
                }

                var (receiverName, providerHint) = DescribeCommandTarget(assignment);
                var scope = GetScope(assignment);
                var executeAnchor = receiverName is null ? null : FindExecuteAnchor(receiverName, scope, assignment.SpanStart);
                var limit = executeAnchor ?? assignment.SpanStart;

                var sources = CollectCommandTextSources(assignment, receiverName, scope);
                var chosen = sources.LastOrDefault(s => s.Position < limit);
                if (chosen.Node is null)
                {
                    chosen = sources.FirstOrDefault(s => s.Position >= limit);
                }

                if (chosen.Node is null)
                {
                    continue;
                }

                var procedureName = ResolveSql(chosen.Value, chosen.Node.GetLocation(), null)?.Trim();
                if (string.IsNullOrWhiteSpace(procedureName) || !IsProcedureName(procedureName))
                {
                    continue;
                }

                var bindings = receiverName is null
                    ? new List<CallBinding>()
                    : CollectCommandBindings(receiverName, scope, executeAnchor ?? int.MaxValue, _model, _ct);

                // Without a command variable, or with no use of its Parameters collection in scope (parameters added by a
                // helper, or none at all), the argument list is not visible: DG101 must not report missing arguments.
                var argumentsKnown = receiverName is not null && UsesParametersCollection(receiverName, scope);

                candidates.Add(new SqlCandidate
                {
                    // Synthesized text for the engine; IsStoredProcedure tells dialect rules it is not literal SQL.
                    SqlText = $"EXEC {procedureName}",
                    Location = chosen.Node.GetLocation(),
                    AnchorNode = assignment,
                    Model = _model,
                    SourceKey = chosen.Node.Span,
                    ProviderHint = chosen.ProviderHint ?? providerHint,
                    IsStoredProcedure = true,
                    ProcedureRawName = procedureName,
                    Bindings = bindings,
                    ArgumentsKnown = argumentsKnown,
                });
            }
        }

        // 3. Object creations (new SqlCommand("SELECT ...", conn), SqlCommand cmd = new("SELECT ...", conn)).
        private void ScanCommandCreations(List<SqlCandidate> candidates)
        {
            foreach (var creation in _root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
            {
                _ct.ThrowIfCancellationRequested();
                var typeName = GetCreatedTypeName(creation, _model, _ct);
                if (typeName is null || !typeName.EndsWith("Command", StringComparison.OrdinalIgnoreCase) ||
                    creation.ArgumentList is not { Arguments.Count: > 0 } argumentList)
                {
                    continue;
                }

                var holes = new List<SqlHole>();
                var sqlText = ResolveSql(argumentList.Arguments[0].Expression, creation.GetLocation(), holes);
                if (string.IsNullOrWhiteSpace(sqlText) || !IsSqlString(sqlText))
                {
                    continue;
                }

                var bindings = new List<CallBinding>();
                var receiverName = GetAssignedVariableName(creation);
                if (receiverName is not null)
                {
                    var scope = GetScope(creation);
                    var anchor = FindExecuteAnchor(receiverName, scope, creation.SpanStart) ?? int.MaxValue;
                    bindings.AddRange(CollectCommandBindings(receiverName, scope, anchor, _model, _ct));
                }

                bindings.AddRange(BindingsFromHoles(holes));
                candidates.Add(new SqlCandidate
                {
                    SqlText = sqlText,
                    Location = creation.GetLocation(),
                    AnchorNode = creation,
                    Model = _model,
                    SourceKey = creation.Span,
                    ProviderHint = InferProviderHint(typeName),
                    Bindings = bindings,
                });
            }
        }

        // 4. Base repository constructor calls: only a statement-shaped argument is SQL; table or connection names
        //    (base("CUSTOMERS"), base("DefaultConnection")) are not turned into synthetic SELECTs.
        private void ScanBaseConstructorCalls(List<SqlCandidate> candidates)
        {
            foreach (var init in _root.DescendantNodes().OfType<ConstructorInitializerSyntax>())
            {
                _ct.ThrowIfCancellationRequested();
                if (!init.IsKind(SyntaxKind.BaseConstructorInitializer) || init.ArgumentList.Arguments.Count == 0)
                {
                    continue;
                }

                var classDecl = init.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classDecl == null || !IsRepositoryClass(classDecl))
                {
                    continue;
                }

                var holes = new List<SqlHole>();
                var sqlText = ResolveSql(init.ArgumentList.Arguments[0].Expression, init.GetLocation(), holes);
                if (string.IsNullOrWhiteSpace(sqlText) || !IsSqlString(sqlText))
                {
                    continue;
                }

                candidates.Add(new SqlCandidate
                {
                    SqlText = sqlText,
                    Location = init.GetLocation(),
                    AnchorNode = init,
                    Model = _model,
                    SourceKey = init.Span,
                    Bindings = BindingsFromHoles(holes),
                });
            }
        }

        private static string? GetAssignedMemberName(AssignmentExpressionSyntax assignment)
        {
            return assignment.Left switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                IdentifierNameSyntax id => id.Identifier.ValueText,
                _ => null,
            };
        }

        /// <summary>
        /// For <c>cmd.X = ...</c> returns <c>cmd</c>; for an initializer member (<c>new SqlCommand { X = ... }</c>) returns
        /// the variable the creation is assigned to. Also returns the provider hint from the command type.
        /// </summary>
        private (string? ReceiverName, string? ProviderHint) DescribeCommandTarget(AssignmentExpressionSyntax assignment)
        {
            if (assignment.Left is MemberAccessExpressionSyntax member)
            {
                var receiver = (member.Expression as IdentifierNameSyntax)?.Identifier.ValueText;
                return (receiver, ProviderHintOf(member.Expression));
            }

            if (assignment.Parent is InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax creation })
            {
                return (GetAssignedVariableName(creation), InferProviderHint(GetCreatedTypeName(creation, _model, _ct)));
            }

            return (null, null);
        }

        private static string? GetAssignedVariableName(ExpressionSyntax creation)
        {
            return creation.Parent switch
            {
                EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => declarator.Identifier.ValueText,
                AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment when assignment.Right == creation => target.Identifier.ValueText,
                _ => null,
            };
        }

        /// <summary>Position of the first <c>receiver.Execute*()</c> call after <paramref name="after"/> in the scope.</summary>
        private static int? FindExecuteAnchor(string receiverName, SyntaxNode scope, int after)
        {
            foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.SpanStart > after &&
                    invocation.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } member &&
                    receiver.Identifier.ValueText == receiverName &&
                    member.Name.Identifier.ValueText.StartsWith("Execute", StringComparison.Ordinal))
                {
                    return invocation.SpanStart;
                }
            }

            return null;
        }

        /// <summary>
        /// Every place the command text of the command in <paramref name="commandTypeAssignment"/> is written, in source
        /// order: constructor first argument (explicit or target-typed <c>new</c>), initializer <c>CommandText = ...</c>
        /// and <c>receiver.CommandText = ...</c> assignments in the scope.
        /// </summary>
        private List<(int Position, SyntaxNode Node, ExpressionSyntax Value, string? ProviderHint)> CollectCommandTextSources(
            AssignmentExpressionSyntax commandTypeAssignment,
            string? receiverName,
            SyntaxNode scope)
        {
            var sources = new List<(int Position, SyntaxNode Node, ExpressionSyntax Value, string? ProviderHint)>();
            var creations = new List<BaseObjectCreationExpressionSyntax>();
            if (commandTypeAssignment.Parent is InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax own })
            {
                creations.Add(own);
            }

            if (receiverName is not null)
            {
                foreach (var node in scope.DescendantNodes())
                {
                    switch (node)
                    {
                        case VariableDeclaratorSyntax { Initializer.Value: BaseObjectCreationExpressionSyntax declared } declarator
                            when declarator.Identifier.ValueText == receiverName && !creations.Contains(declared):
                            creations.Add(declared);
                            break;
                        case AssignmentExpressionSyntax { Left: IdentifierNameSyntax target, Right: BaseObjectCreationExpressionSyntax assigned }
                            when target.Identifier.ValueText == receiverName && !creations.Contains(assigned):
                            creations.Add(assigned);
                            break;
                        case AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner, Name.Identifier.ValueText: "CommandText" } } textAssignment
                            when owner.Identifier.ValueText == receiverName:
                            sources.Add((textAssignment.SpanStart, textAssignment, textAssignment.Right, ProviderHintOf(owner)));
                            break;
                    }
                }
            }

            foreach (var creation in creations)
            {
                var typeName = GetCreatedTypeName(creation, _model, _ct);
                if (typeName is null || !typeName.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hint = InferProviderHint(typeName);
                if (creation.ArgumentList is { Arguments.Count: > 0 } ctorArgs)
                {
                    sources.Add((creation.SpanStart, creation, ctorArgs.Arguments[0].Expression, hint));
                }

                if (creation.Initializer is { } initializer)
                {
                    foreach (var member in initializer.Expressions.OfType<AssignmentExpressionSyntax>())
                    {
                        if (member.Left is IdentifierNameSyntax { Identifier.ValueText: "CommandText" })
                        {
                            sources.Add((member.SpanStart, member, member.Right, hint));
                        }
                    }
                }
            }

            sources.Sort((a, b) => a.Position.CompareTo(b.Position));
            return sources;
        }
    }

    private static bool IsRepositoryClass(ClassDeclarationSyntax classDecl)
    {
        var name = classDecl.Identifier.ValueText;
        if (name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Repo", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Store", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Dao", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (classDecl.BaseList != null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                var typeName = baseType.Type.ToString();
                if (typeName.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
                    typeName.EndsWith("Repo", StringComparison.OrdinalIgnoreCase) ||
                    typeName.EndsWith("Store", StringComparison.OrdinalIgnoreCase) ||
                    typeName.EndsWith("Dao", StringComparison.OrdinalIgnoreCase) ||
                    typeName.StartsWith("IRepository", StringComparison.OrdinalIgnoreCase) ||
                    typeName.StartsWith("IRepo", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEntityClass(ClassDeclarationSyntax classDecl)
    {
        if (IsRepositoryClass(classDecl))
        {
            return false;
        }

        var name = classDecl.Identifier.ValueText;
        if (name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Model", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Status", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Record", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (classDecl.BaseList != null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                var typeName = baseType.Type.ToString();
                if (typeName.EndsWith("Entity", StringComparison.OrdinalIgnoreCase) ||
                    typeName.EndsWith("Model", StringComparison.OrdinalIgnoreCase) ||
                    typeName.EndsWith("Dto", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
