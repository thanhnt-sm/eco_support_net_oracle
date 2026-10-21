using System.Text;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DataGuard.Core.Sources;

/// <summary>Conservative provenance for extracted SQL diagnostic positions.</summary>
public sealed partial class ProjectCSharpSqlSource
{
    private static IReadOnlyList<SqlSourceSegment> BuildSourceMap(
        ExpressionSyntax? expression, SemanticModel model, string sql, CancellationToken cancellationToken, LocalValueStability stability)
    {
        if (expression is null)
        {
            return Array.Empty<SqlSourceSegment>();
        }

        var map = new SqlSourceMapBuilder();
        var resolved = ResolveSqlValue(expression, model, new List<SqlHole>(), cancellationToken, stability, map).Text;

        // Constant folding or unsupported resolution must never produce a plausible but incorrect token location.
        return resolved == sql && map.Text.ToString() == sql ? map.Segments : Array.Empty<SqlSourceSegment>();
    }

    private sealed class SqlSourceMapBuilder
    {
        public StringBuilder Text { get; } = new();

        public List<SqlSourceSegment> Segments { get; } = new();

        public void AppendMapped(string value, Location location)
        {
            Segments.Add(new SqlSourceSegment(Text.Length, value.Length, location, false));
            Text.Append(value);
        }

        public void AppendToken(SyntaxToken token, int prefix, int suffix)
        {
            var raw = token.Text;
            var value = token.ValueText;

            // Exact contiguous text (including LF/CRLF and indentation) is by far the common case.
            if (raw.Length >= prefix + suffix && raw.Substring(prefix, raw.Length - prefix - suffix) == value)
            {
                var location = Location.Create(token.SyntaxTree!, new TextSpan(token.SpanStart + prefix, value.Length));
                Segments.Add(new SqlSourceSegment(Text.Length, value.Length, location, true));
                Text.Append(value);
                return;
            }

            // Escaped and raw literals retain a truthful containing-token location until their decoding has a map.
            AppendMapped(value, token.GetLocation());
        }
    }
}
