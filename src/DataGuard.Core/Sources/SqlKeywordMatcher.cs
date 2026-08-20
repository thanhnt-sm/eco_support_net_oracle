namespace DataGuard.Core.Sources;

/// <summary>
/// Shared substring matching for dialect keyword lists (used by the MySQL,
/// PostgreSQL and Oracle dialect checkers so the check logic lives in one place).
/// </summary>
public static class SqlKeywordMatcher
{
    public static bool ContainsAny(string sqlText, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (sqlText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
