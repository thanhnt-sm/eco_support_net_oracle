using System.Text;
using DataGuard.Core.Rules.Sql;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DataGuard.SqlServer.Adapter;

/// <summary>
/// T-SQL phantom table/column analysis on the ScriptDOM AST (<see cref="TSql160Parser"/>), used by DG015/DG016 for the
/// <c>sqlserver</c> provider. Each query specification gets its own scope of FROM sources; column references resolve
/// through the scope chain (innermost first). Only base tables are checked: CTEs, derived tables, <c>VALUES</c> tables,
/// table-valued functions, <c>OPENJSON</c>/<c>OPENROWSET</c>, <c>#temp</c> tables, <c>@table</c> variables,
/// three/four-part (cross-database, linked-server) names and <c>sys.*</c>/<c>INFORMATION_SCHEMA.*</c> objects are
/// opaque sources that are never reported and make unqualified columns in their scope unresolvable. When the SQL does
/// not parse the result is <see cref="PhantomAnalysis.ParseFailed"/> with no findings (DG019 reports parse errors).
/// </summary>
public sealed class TSqlPhantomAnalyzer : IPhantomReferenceAnalyzer
{
    /// <inheritdoc />
    public PhantomAnalysis Analyze(string sql, SchemaTableIndex schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(sql) || schema.IsEmpty)
        {
            return PhantomAnalysis.Empty;
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;
        using (var reader = new StringReader(NormalizePlaceholders(sql)))
        {
            fragment = parser.Parse(reader, out errors);
        }

        if (errors.Count > 0 || fragment is null)
        {
            var first = errors.Count > 0 ? errors[0] : null;
            return PhantomAnalysis.Failed(first is null
                ? "T-SQL parser returned no fragment"
                : $"Line {first.Line}, column {first.Column}: {first.Message}");
        }

        var visitor = new TSqlPhantomScopeVisitor(schema);
        fragment.Accept(visitor);
        return new PhantomAnalysis(visitor.PhantomTables, visitor.PhantomColumns);
    }

    /// <summary>
    /// Rewrites client-side parameter placeholders that are not T-SQL (<c>:name</c>, <c>{0}</c>, <c>?</c>) to
    /// <c>@</c> variables so raw SQL written for ADO.NET/Dapper placeholders still parses. Text inside string
    /// literals, quoted identifiers and comments is left untouched.
    /// </summary>
    /// <param name="sql">SQL text.</param>
    /// <returns>The SQL with placeholders rewritten.</returns>
    internal static string NormalizePlaceholders(string sql)
    {
        if (sql.IndexOfAny([':', '{', '?']) < 0)
        {
            return sql;
        }

        var sb = new StringBuilder(sql.Length + 8);
        var i = 0;
        while (i < sql.Length)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            switch (ch)
            {
                case '\'':
                    i = CopyQuoted(sql, i, '\'', sb);
                    continue;
                case '"':
                    i = CopyQuoted(sql, i, '"', sb);
                    continue;
                case '[':
                    i = CopyQuoted(sql, i, ']', sb);
                    continue;
                case '-' when next == '-':
                    i = CopyUntil(sql, i, "\n", sb);
                    continue;
                case '/' when next == '*':
                    i = CopyUntil(sql, i, "*/", sb);
                    continue;
                case ':' when next == ':':
                    sb.Append("::");
                    i += 2;
                    continue;
                case ':' when IsIdentifierStart(next) && (i == 0 || !IsIdentifierPart(sql[i - 1])):
                    sb.Append('@');
                    i++;
                    continue;
                case '?':
                    sb.Append("@p__");
                    i++;
                    continue;
                case '{' when char.IsAsciiDigit(next):
                    var close = sql.IndexOf('}', i + 1);
                    if (close > 0 && sql.AsSpan(i + 1, close - i - 1).IndexOfAnyExceptInRange('0', '9') < 0)
                    {
                        sb.Append("@p__").Append(sql, i + 1, close - i - 1);
                        i = close + 1;
                        continue;
                    }

                    break;
            }

            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '@' or '#' or '$' or ':' or ']' or '"' or '\'';

    private static int CopyQuoted(string sql, int start, char closer, StringBuilder sb)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == closer)
            {
                if (i + 1 < sql.Length && sql[i + 1] == closer)
                {
                    i += 2;
                    continue;
                }

                i++;
                break;
            }

            i++;
        }

        sb.Append(sql, start, i - start);
        return i;
    }

    private static int CopyUntil(string sql, int start, string terminator, StringBuilder sb)
    {
        var end = sql.IndexOf(terminator, start + 2, StringComparison.Ordinal);
        var stop = end < 0 ? sql.Length : end + terminator.Length;
        sb.Append(sql, start, stop - start);
        return stop;
    }
}
