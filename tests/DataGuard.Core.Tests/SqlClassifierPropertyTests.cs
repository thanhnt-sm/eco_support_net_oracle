using DataGuard.SqlClassification;
using FsCheck;
using FsCheck.Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Property-based (generative) robustness tests for <see cref="SqlClassifier"/>.
/// FsCheck generates hundreds of arbitrary inputs per property; any crash or
/// invariant violation shrinks to a minimal reproducer. This is the .NET
/// counterpart of fuzzer coverage and satisfies OSSF Scorecard Fuzzing
/// detection for C# (FsCheck usage).
/// </summary>
public sealed class SqlClassifierPropertyTests
{
    private static readonly Uri DummyDocument = new("file:///dummy.sql");

    [Property]
    public bool Classify_NeverThrows_OnArbitraryInput(NonNull<string> source)
    {
        try
        {
            SqlClassifier.Classify(source.Item, DummyDocument, "1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Property]
    public bool Classify_ResultsAlwaysWithinSourceBounds(NonNull<string> source)
    {
        var results = SqlClassifier.Classify(source.Item, DummyDocument, "1");
        return results.All(result =>
            result.Start >= 0 &&
            result.Length > 0 &&
            result.Start + result.Length <= source.Item.Length);
    }

    [Property]
    public bool Classify_ResultsSortedByStartThenKind(NonNull<string> source)
    {
        var results = SqlClassifier.Classify(source.Item, DummyDocument, "1");
        return results
            .Zip(results.Skip(1), static (previous, next) =>
                previous.Start < next.Start ||
                (previous.Start == next.Start &&
                 string.Compare(previous.Kind, next.Kind, StringComparison.Ordinal) <= 0))
            .All(static ordered => ordered);
    }

    [Property]
    public bool Classify_IsDeterministic(NonNull<string> source)
    {
        var first = SqlClassifier.Classify(source.Item, DummyDocument, "1");
        var second = SqlClassifier.Classify(source.Item, DummyDocument, "1");
        return first.Count == second.Count &&
            first.Zip(second, static (a, b) =>
                a.Start == b.Start && a.Length == b.Length && a.Kind == b.Kind).All(static same => same);
    }

    [Property]
    public bool ClassifyCSharp_NeverThrowsAndStaysWithinSourceBounds(NonNull<string> source)
    {
        var results = SqlClassifier.ClassifyCSharp(source.Item, DummyDocument, "1");
        return results.All(result =>
            result.Start >= 0 &&
            result.Length > 0 &&
            result.Start + result.Length <= source.Item.Length &&
            string.Equals(source.Item.Substring(result.Start, result.Length), result.Kind, StringComparison.OrdinalIgnoreCase));
    }

    [Property]
    public bool ClassifyCSharp_FindsSqlInsideArbitraryStringLiteral(NonNull<string> prefix)
    {
        // Whatever C#-ish text precedes it (unless it opens a comment or string), a later SQL literal is found.
        var safePrefix = new string(prefix.Item.Where(char.IsLetterOrDigit).ToArray());
        var source = safePrefix + "; var sql = \"SELECT Id FROM Orders\";";
        var results = SqlClassifier.ClassifyCSharp(source, DummyDocument, "1");
        return results.Count == 1 && results[0].Start == source.IndexOf("SELECT", StringComparison.Ordinal);
    }

    [Property]
    public bool LooksLikeSql_NeverThrows(NonNull<string> source)
    {
        SqlClassifier.LooksLikeSql(source.Item);
        return true;
    }
}
