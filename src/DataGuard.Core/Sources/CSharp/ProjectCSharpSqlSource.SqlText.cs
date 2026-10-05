using System.Text;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataGuard.Core.Sources;

/// <summary>
/// SQL text helpers: statement recognition, C# string resolution (with interpolation holes), placeholder scanning,
/// masking, classification and provider hints.
/// </summary>
public sealed partial class ProjectCSharpSqlSource
{
    private static readonly Regex StatementPrefixRegex = new(
        @"^\s*(?<kw>SELECT|INSERT|UPDATE|DELETE|MERGE|WITH|EXEC|EXECUTE|CALL|BEGIN|DECLARE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ClauseKeywordRegex = new(
        @"\b(FROM|INTO|SET|VALUES|JOIN)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CteBodyRegex = new(
        @"\bAS\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ExecTargetRegex = new(
        @"^\s*EXEC(UTE)?\s*[\w\[""@(#]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CallTargetRegex = new(
        @"^\s*CALL\s+[\w\.\[\]""`$#]+\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex BlockBodyRegex = new(
        @"\bEND\b|\b(SELECT|INSERT|UPDATE|DELETE|MERGE)\b[\s\S]*?\b(FROM|INTO|SET|VALUES|JOIN)\b|\bEXEC(UTE)?\s+\w",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ProcedureNameRegex = new(
        @"^(\[[\w\s$#]+\]|""[\w\s$#]+""|[A-Za-z0-9_#$]+)(\.(\[[\w\s$#]+\]|""[\w\s$#]+""|[A-Za-z0-9_#$]+)){0,3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex StandaloneSqlStatementRegex = new(
        @"^\s*(SELECT\s+.*?\s+FROM\s+\S+|SELECT\s+[\d@:]|INSERT\s+INTO\s+\S+|UPDATE\s+.*?\s+SET\s+\S+|DELETE\s+FROM\s+\S+|MERGE\s+INTO\s+\S+|WITH\s+.*?\bSELECT\b|BEGIN\s+.+\s+END;?|EXEC\s+\w+|EXECUTE\s+\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex SqlStringLiteralRegex = new(
        @"'(''|[^'])*'",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex SqlSingleLineCommentRegex = new(@"--[^\r\n]*", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex SqlBlockCommentRegex = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex SelectOpRegex = new(@"\bSELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex InsertOpRegex = new(@"\bINSERT\s+INTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex UpdateOpRegex = new(@"\bUPDATE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex DeleteOpRegex = new(@"\bDELETE(?:\s+FROM)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex MergeOpRegex = new(@"\bMERGE\s+INTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex JoinOpRegex = new(@"\b(INNER|LEFT|RIGHT|FULL|CROSS)?\s*JOIN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex[] TablePatterns = new[]
    {
        new Regex(@"\bFROM\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bJOIN\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bINTO\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bUPDATE\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"\bTABLE\s+([A-Za-z0-9_.\""\[\]\`]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
    };

    private static readonly HashSet<string> PlaceholderKeywordsBeforeQuestionMark = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN", "LIKE", "AND", "OR", "NOT", "IS", "THEN", "ELSE", "WHEN", "VALUES", "SET", "LIMIT", "OFFSET",
        "BETWEEN", "SELECT", "RETURN", "WHERE", "ON", "BY", "TOP", "FETCH",
    };

    private static readonly string SpacePadding = new(' ', 512);

    /// <summary>
    /// Returns true when <paramref name="text"/> looks like an executable SQL statement: it starts (after comments)
    /// with SELECT, INSERT, UPDATE, DELETE, MERGE, WITH, EXEC, EXECUTE, CALL, BEGIN or DECLARE, and the rest of the text
    /// has the shape that keyword needs (a FROM/INTO/SET/VALUES/JOIN clause for the DML keywords, <c>AS (</c> for a CTE,
    /// a target for EXEC, <c>name(</c> for CALL, an END or an inner statement for BEGIN/DECLARE). Bare procedure names are
    /// not SQL statements; see <see cref="IsProcedureName"/>.
    /// </summary>
    internal static bool IsSqlString(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxSqlLiteralLength)
        {
            return false;
        }

        var masked = MaskForScan(text);
        var match = StatementPrefixRegex.Match(masked);
        if (!match.Success)
        {
            return false;
        }

        var rest = masked.Substring(match.Index + match.Length);
        switch (match.Groups["kw"].Value.ToUpperInvariant())
        {
            case "SELECT":
            case "INSERT":
            case "UPDATE":
            case "DELETE":
            case "MERGE":
                return ClauseKeywordRegex.IsMatch(rest);
            case "WITH":
                return CteBodyRegex.IsMatch(rest);
            case "EXEC":
            case "EXECUTE":
                return ExecTargetRegex.IsMatch(masked);
            case "CALL":
                return CallTargetRegex.IsMatch(masked);
            default:
                return BlockBodyRegex.IsMatch(rest);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="text"/> is a plain (optionally bracket- or quote-delimited, up to four-part)
    /// procedure name such as <c>usp_GetUser</c>, <c>dbo.usp_GetUser</c>, <c>[dbo].[Get User]</c> or <c>HR.PKG.PROC</c>.
    /// Used for <c>CommandType.StoredProcedure</c> command texts, which are names rather than statements.
    /// </summary>
    internal static bool IsProcedureName(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 512 && ProcedureNameRegex.IsMatch(text.Trim());
    }

    internal static bool IsStandaloneSqlConstant(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsSqlString(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (!trimmed.Contains(' ') && !trimmed.Contains('\n') && !trimmed.Contains('\r'))
        {
            return false;
        }

        return StandaloneSqlStatementRegex.IsMatch(trimmed);
    }

    public static string? TryResolveString(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        return TryResolveStringCore(expression, semanticModel, cancellationToken, 0, null, null);
    }

    /// <summary>
    /// Resolves <paramref name="expression"/> to SQL text, turning every non-constant interpolation hole or unresolved
    /// string-concatenation operand into an <c>@name</c> placeholder recorded in <paramref name="holes"/>.
    /// </summary>
    internal static string? TryResolveSql(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        List<SqlHole>? holes,
        CancellationToken cancellationToken)
    {
        return TryResolveStringCore(expression, semanticModel, cancellationToken, 0, null, holes);
    }

    private static string? TryResolveStringCore(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        int depth,
        HashSet<SyntaxNode>? visited,
        List<SqlHole>? holes)
    {
        if (depth > 10)
        {
            return null;
        }

        visited ??= new HashSet<SyntaxNode>();
        if (!visited.Add(expression))
        {
            return null;
        }

        try
        {
            // 1. Semantic constant evaluation (handles const string, string concatenation of constants)
            var constant = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constant.HasValue && constant.Value is string constString && !string.IsNullOrWhiteSpace(constString))
            {
                return constString;
            }

            // 2. String literal syntax
            if (expression is LiteralExpressionSyntax literal && (literal.Token.Value is string || literal.IsKind(SyntaxKind.StringLiteralExpression)))
            {
                return literal.Token.ValueText;
            }

            // 3. Interpolated string syntax
            if (expression is InterpolatedStringExpressionSyntax interpolated)
            {
                return ConvertInterpolatedStringToSql(interpolated, semanticModel, cancellationToken, holes);
            }

            // 4. Identifier reference (e.g. var sql = "..."; Query<T>(sql);)
            if (expression is IdentifierNameSyntax identifier)
            {
                var resolved = ResolveIdentifierString(identifier, semanticModel, cancellationToken, depth, visited, holes);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            // Unwrap parenthesized expressions first (e.g. "A" + ("B" + "C"))
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                return TryResolveStringCore(parenthesized.Expression, semanticModel, cancellationToken, depth + 1, visited, holes);
            }

            // 5. Binary add expression ("SELECT ... " + "FROM ...") - iteratively flattened to prevent StackOverflowException
            if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
            {
                return ResolveConcatenation(binary, semanticModel, cancellationToken, depth, visited, holes);
            }

            return null;
        }
        finally
        {
            visited.Remove(expression);
        }
    }

    private static string? ResolveIdentifierString(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        int depth,
        HashSet<SyntaxNode> visited,
        List<SqlHole>? holes)
    {
        var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
        if (symbol is ILocalSymbol or IFieldSymbol or IPropertySymbol)
        {
            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                var syntax = syntaxRef.GetSyntax(cancellationToken);
                ExpressionSyntax? initExpr = syntax switch
                {
                    VariableDeclaratorSyntax decl => decl.Initializer?.Value,
                    PropertyDeclarationSyntax prop => prop.Initializer?.Value ?? prop.ExpressionBody?.Expression,
                    _ => null,
                };

                if (initExpr == null)
                {
                    continue;
                }

                SemanticModel? targetModel = null;
                if (semanticModel.SyntaxTree == syntax.SyntaxTree)
                {
                    targetModel = semanticModel;
                }
                else if (semanticModel.Compilation.ContainsSyntaxTree(syntax.SyntaxTree))
                {
                    targetModel = semanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);
                }

                if (targetModel != null)
                {
                    var resolved = TryResolveStringCore(initExpr, targetModel, cancellationToken, depth + 1, visited, holes);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        return resolved;
                    }
                }
                else if (initExpr is LiteralExpressionSyntax lit && (lit.Token.Value is string || lit.IsKind(SyntaxKind.StringLiteralExpression)))
                {
                    return lit.Token.ValueText;
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
                    var resolved = TryResolveStringCore(decl.Initializer.Value, semanticModel, cancellationToken, depth + 1, visited, holes);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        return resolved;
                    }
                }
            }
        }

        return null;
    }

    private static string? ResolveConcatenation(
        BinaryExpressionSyntax binary,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        int depth,
        HashSet<SyntaxNode> visited,
        List<SqlHole>? holes)
    {
        var parts = new List<ExpressionSyntax>();
        var stack = new Stack<ExpressionSyntax>();
        stack.Push(binary);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression))
            {
                stack.Push(b.Right);
                stack.Push(b.Left);
            }
            else
            {
                parts.Add(current);
            }
        }

        var isStringConcat = semanticModel.GetTypeInfo(binary, cancellationToken).Type?.SpecialType == SpecialType.System_String;
        var sb = new StringBuilder();
        var resolvedTextParts = 0;
        var pendingHoles = new List<SqlHole>();
        foreach (var part in parts)
        {
            var resolvedPart = TryResolveStringCore(part, semanticModel, cancellationToken, depth + 1, visited, holes);
            if (resolvedPart != null)
            {
                resolvedTextParts++;
                sb.Append(resolvedPart);
                continue;
            }

            // A non-constant operand of a string concatenation is dynamic SQL (the injection-prone shape); keep the
            // statement and represent the operand as a named placeholder instead of dropping the whole literal.
            if (!isStringConcat)
            {
                return null;
            }

            var hole = CreateHole(part, semanticModel, (holes?.Count ?? 0) + pendingHoles.Count);
            pendingHoles.Add(hole);
            sb.Append(hole.Name);
        }

        if (resolvedTextParts == 0)
        {
            return null;
        }

        holes?.AddRange(pendingHoles);
        return sb.ToString();
    }

    private static string ConvertInterpolatedStringToSql(
        InterpolatedStringExpressionSyntax interpolated,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        List<SqlHole>? holes)
    {
        var sb = new StringBuilder();
        var localHoles = 0;
        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax textSyntax)
            {
                sb.Append(textSyntax.TextToken.ValueText);
                continue;
            }

            if (content is not InterpolationSyntax interpolation)
            {
                continue;
            }

            // Only compile-time string constants are inlined; locals, fields, parameters and calls never are, so the
            // descriptor shows where caller-controlled values enter the statement.
            var constant = semanticModel.GetConstantValue(interpolation.Expression, cancellationToken);
            if (constant.HasValue && constant.Value is string constString)
            {
                sb.Append(constString);
                continue;
            }

            var hole = CreateHole(interpolation.Expression, semanticModel, (holes?.Count ?? 0) + localHoles);
            localHoles++;
            holes?.Add(hole);
            sb.Append(hole.Name);
        }

        return sb.ToString();
    }

    private static SqlHole CreateHole(ExpressionSyntax expression, SemanticModel semanticModel, int index)
    {
        var inner = expression;
        while (inner is ParenthesizedExpressionSyntax p)
        {
            inner = p.Expression;
        }

        var name = inner switch
        {
            IdentifierNameSyntax id => "@" + id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => "@" + ma.Name.Identifier.ValueText,
            _ => "@p" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        return new SqlHole(name, expression, semanticModel);
    }

    /// <summary>
    /// Collapses whitespace runs to one space and trims, so formatting-only edits keep the same SQL hash.
    /// </summary>
    internal static string NormalizeWhitespace(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var pendingSpace = false;
        foreach (var ch in sql)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns a copy of <paramref name="sql"/> of the same length in which string literals (<c>'..'</c>, <c>N'..'</c>,
    /// <c>E'..'</c>), quoted identifiers (<c>"..."</c>, <c>`...`</c>), PostgreSQL dollar-quoted bodies and
    /// comments (<c>--</c>, <c>/* */</c>) are replaced by spaces. Unlike regex masking this is a single left-to-right scan,
    /// so a quote inside a comment or a comment marker inside a literal is handled correctly.
    /// </summary>
    internal static string MaskForScan(string sql)
    {
        var chars = sql.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            var ch = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';
            if (ch == '-' && next == '-')
            {
                var end = i;
                while (end < chars.Length && chars[end] != '\n' && chars[end] != '\r')
                {
                    end++;
                }

                Blank(chars, i, end);
                i = end;
            }
            else if (ch == '/' && next == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? chars.Length : end + 2;
                Blank(chars, i, end);
                i = end;
            }
            else if (ch is '\'' or '"' or '`')
            {
                var closer = ch;
                var end = i + 1;
                while (end < chars.Length)
                {
                    if (chars[end] == closer)
                    {
                        if (end + 1 < chars.Length && chars[end + 1] == closer)
                        {
                            end += 2;
                            continue;
                        }

                        end++;
                        break;
                    }

                    end++;
                }

                Blank(chars, i, end);
                i = end;
            }
            else if (ch == '$' && TryMatchDollarQuote(sql, i, out var tagLength))
            {
                var tag = sql.Substring(i, tagLength);
                var close = sql.IndexOf(tag, i + tagLength, StringComparison.Ordinal);
                var end = close < 0 ? chars.Length : close + tagLength;
                Blank(chars, i, end);
                i = end;
            }
            else
            {
                i++;
            }
        }

        return new string(chars);
    }

    private static void Blank(char[] chars, int start, int end)
    {
        for (var k = start; k < end && k < chars.Length; k++)
        {
            if (chars[k] != '\n' && chars[k] != '\r')
            {
                chars[k] = ' ';
            }
        }
    }

    private static bool TryMatchDollarQuote(string sql, int start, out int tagLength)
    {
        tagLength = 0;
        if (start > 0 && (char.IsLetterOrDigit(sql[start - 1]) || sql[start - 1] == '_'))
        {
            return false;
        }

        var end = start + 1;
        while (end < sql.Length && (char.IsLetter(sql[end]) || sql[end] == '_'))
        {
            end++;
        }

        if (end < sql.Length && sql[end] == '$')
        {
            tagLength = end - start + 1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds bind placeholders in SQL text: <c>@name</c>, <c>:name</c>, <c>:1</c>, <c>$1</c> keep their written form;
    /// <c>{0}</c> (EF <c>FromSqlRaw</c>) and <c>?</c> (ODBC/OLE DB/MySQL) are positional and named <c>#n</c> (zero-based).
    /// Comments and literals are masked first; <c>::type</c> casts, <c>:=</c>, <c>@@SYSTEM_VARIABLES</c>, PostgreSQL
    /// JSON <c>?|</c>/<c>?&amp;</c> operators and <c>?</c> used as an operator are ignored.
    /// </summary>
    internal static IReadOnlyList<SqlPlaceholder> ScanPlaceholders(string sqlText)
    {
        var result = new List<SqlPlaceholder>();
        if (string.IsNullOrEmpty(sqlText))
        {
            return result;
        }

        var masked = MaskForScan(sqlText);
        var questionIndex = 0;
        var i = 0;
        while (i < masked.Length)
        {
            var ch = masked[i];
            var prev = i > 0 ? masked[i - 1] : '\0';
            var next = i + 1 < masked.Length ? masked[i + 1] : '\0';
            switch (ch)
            {
                case '@' when next == '@':
                    i = SkipIdentifier(masked, i + 2);
                    continue;
                case '@' when IsIdentifierStart(next) && !IsWordChar(prev):
                    {
                        var end = SkipIdentifier(masked, i + 1);
                        result.Add(new SqlPlaceholder(masked.Substring(i, end - i), false, -1, i));
                        i = end;
                        continue;
                    }

                case ':' when next == ':':
                    i = SkipIdentifier(masked, i + 2);
                    continue;
                case ':' when prev != ':' && !IsWordChar(prev) && prev != ']' && prev != ')' && (IsIdentifierStart(next) || char.IsDigit(next)):
                    {
                        var end = SkipIdentifier(masked, i + 1);
                        result.Add(new SqlPlaceholder(masked.Substring(i, end - i), false, -1, i));
                        i = end;
                        continue;
                    }

                case '$' when char.IsDigit(next) && !IsWordChar(prev):
                    {
                        var end = i + 1;
                        while (end < masked.Length && char.IsDigit(masked[end]))
                        {
                            end++;
                        }

                        var text = masked.Substring(i, end - i);
                        var position = int.Parse(text.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture) - 1;
                        result.Add(new SqlPlaceholder(text, true, Math.Max(position, 0), i));
                        i = end;
                        continue;
                    }

                case '{' when char.IsDigit(next):
                    {
                        var end = i + 1;
                        while (end < masked.Length && char.IsDigit(masked[end]))
                        {
                            end++;
                        }

                        if (end < masked.Length && masked[end] == '}' && end - i - 1 <= 4)
                        {
                            var position = int.Parse(masked.AsSpan(i + 1, end - i - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
                            result.Add(new SqlPlaceholder("#" + position.ToString(System.Globalization.CultureInfo.InvariantCulture), true, position, i));
                            i = end + 1;
                            continue;
                        }

                        break;
                    }

                case '?' when next is not ('|' or '&' or '?') && IsPositionalQuestionMark(masked, i):
                    result.Add(new SqlPlaceholder("#" + questionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), true, questionIndex, i));
                    questionIndex++;
                    i++;
                    continue;
            }

            i++;
        }

        return result;
    }

    private static bool IsPositionalQuestionMark(string masked, int index)
    {
        var k = index - 1;
        while (k >= 0 && char.IsWhiteSpace(masked[k]))
        {
            k--;
        }

        if (k < 0)
        {
            return true;
        }

        var prev = masked[k];
        if ("=<>(,+-*/%{".Contains(prev, StringComparison.Ordinal))
        {
            return true;
        }

        if (!IsWordChar(prev))
        {
            return false;
        }

        var end = k + 1;
        while (k >= 0 && IsWordChar(masked[k]))
        {
            k--;
        }

        var word = masked.Substring(k + 1, end - k - 1);
        return PlaceholderKeywordsBeforeQuestionMark.Contains(word);
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static int SkipIdentifier(string text, int start)
    {
        var end = start;
        while (end < text.Length && (IsWordChar(text[end]) || text[end] == '$' || text[end] == '#'))
        {
            end++;
        }

        return end;
    }

    private static IReadOnlyList<ParameterDescriptor> ExtractParameters(string sqlText)
    {
        var parameters = new List<ParameterDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 1;
        foreach (var placeholder in ScanPlaceholders(sqlText))
        {
            if (seen.Add(placeholder.Name))
            {
                parameters.Add(new ParameterDescriptor(
                    Name: placeholder.Name,
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

    public static string MaskSqlStringLiterals(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }

        return SqlStringLiteralRegex.Replace(sql, m => m.Length <= SpacePadding.Length
            ? SpacePadding.Substring(0, m.Length)
            : new string(' ', m.Length));
    }

    public static string MaskSqlComments(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }

        var maskedStrings = MaskSqlStringLiterals(sql);
        var noSingle = SqlSingleLineCommentRegex.Replace(maskedStrings, m => m.Length <= SpacePadding.Length
            ? SpacePadding.Substring(0, m.Length)
            : new string(' ', m.Length));
        return SqlBlockCommentRegex.Replace(noSingle, m => m.Length <= SpacePadding.Length
            ? SpacePadding.Substring(0, m.Length)
            : new string(' ', m.Length));
    }

    public static string MaskSqlCommentsAndStrings(string sql)
    {
        return DataGuard.Core.Rules.ColumnShapeMatchRule.StripCommentsAndLiterals(sql);
    }

    public static SqlOperationType ClassifySqlOperation(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlOperationType.Unknown;
        }

        var masked = MaskSqlCommentsAndStrings(sql);
        var hasSelect = SelectOpRegex.IsMatch(masked);
        var hasInsert = InsertOpRegex.IsMatch(masked);
        var hasUpdate = UpdateOpRegex.IsMatch(masked);
        var hasDelete = DeleteOpRegex.IsMatch(masked);
        var hasMerge = MergeOpRegex.IsMatch(masked);
        var hasJoin = JoinOpRegex.IsMatch(masked);

        var writeCount = (hasInsert ? 1 : 0) + (hasUpdate ? 1 : 0) + (hasDelete ? 1 : 0) + (hasMerge ? 1 : 0);
        if (hasSelect && writeCount > 0)
        {
            return SqlOperationType.Mixed;
        }

        if (writeCount > 1)
        {
            return SqlOperationType.Mixed;
        }

        if (writeCount == 1)
        {
            return SqlOperationType.Write;
        }

        if (hasSelect && hasJoin)
        {
            return SqlOperationType.Join;
        }

        if (hasSelect)
        {
            return SqlOperationType.Read;
        }

        return SqlOperationType.Reference;
    }

    public static IReadOnlyList<string> ExtractReferencedTables(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<string>();
        }

        var masked = MaskSqlCommentsAndStrings(sql);
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var regex in TablePatterns)
        {
            var matches = regex.Matches(masked);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var raw = match.Groups[1].Value.Trim();
                    var dot = raw.LastIndexOf('.');
                    var tableName = dot >= 0 ? raw.Substring(dot + 1) : raw;
                    tableName = tableName.Trim('[', ']', '`', '"');
                    if (!string.IsNullOrEmpty(tableName) && !IsSqlKeywordToken(tableName))
                    {
                        tables.Add(tableName);
                    }
                }
            }
        }

        return tables.ToList();
    }

    private static bool IsSqlKeywordToken(string token)
    {
        return token.ToUpperInvariant() is "SELECT" or "FROM" or "WHERE" or "AS" or
            "JOIN" or "ON" or "INTO" or "SET" or "VALUES" or "AND" or "OR" or "NULL";
    }

    public static string? InferProviderHint(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        if (typeName.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "oracle";
        }

        if (typeName.IndexOf("Npgsql", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Postgre", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "postgresql";
        }

        if (typeName.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "mysql";
        }

        if (typeName.IndexOf("Sql", StringComparison.OrdinalIgnoreCase) >= 0 &&
            typeName.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) < 0 &&
            typeName.IndexOf("Postgresql", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return "sqlserver";
        }

        return null;
    }

    /// <summary>A non-constant value spliced into SQL text (interpolation hole or concatenation operand).</summary>
    internal sealed record SqlHole(string Name, ExpressionSyntax Expression, SemanticModel Model);

    /// <summary>A bind placeholder found in SQL text.</summary>
    /// <param name="Name">The name as written (<c>@id</c>, <c>:id</c>, <c>$1</c>) or <c>#n</c> for <c>{n}</c> and <c>?</c>.</param>
    /// <param name="IsPositional">True for <c>$n</c>, <c>{n}</c> and <c>?</c>.</param>
    /// <param name="Position">Zero-based position for positional placeholders, otherwise -1.</param>
    /// <param name="Offset">Character offset in the SQL text.</param>
    internal sealed record SqlPlaceholder(string Name, bool IsPositional, int Position, int Offset);
}
