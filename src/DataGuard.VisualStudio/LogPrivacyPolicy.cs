namespace DataGuard.VisualStudio;

using System;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Conservative, bounded sanitization for local diagnostic text, not an anonymity guarantee.</summary>
internal static class LogPrivacyPolicy
{
    internal const int MaximumInputCharacters = 32768;
    internal const int MaximumRecordBytes = 8192;
    private const string Redacted = "[REDACTED]";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Assignments = new(
        @"(?i)(?<key>password|pwd|passwd|secret|token|api[_ -]?key|access[_ -]?(?:key|token)|refresh[_ -]?token|client[_ -]?secret|authorization|sas|user\s*(?:id|name)?|uid|server|host|data\s*source|database|initial\s*catalog|connection[_ -]?string)\s*[""']?\s*[:=]\s*(?:""(?:[^""]|"""")*(?:""|$)|'(?:[^']|'')*(?:'|$)|\{(?:[^}]|}})*(?:}|$)|[^;\r\n]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);
    private static readonly Regex PrivateText = new(
        @"(?i)\bbearer\s+[^\s;,]+|eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}" +
        @"|[a-z][a-z0-9+.-]*://[^\s]+|(?<![\w./-])[A-Za-z_][\w.$-]*/[^\s/@;,]+@[^\s;,]*" +
        @"|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}" +
        @"|(?:[A-Za-z]:\\|\\\\|(?<!\w)/)[^\r\n]*" +
        @"|'(?:[^']|'')*'|""(?:[^""]|"""")*""" +
        @"|\b(?:SELECT|INSERT|UPDATE|DELETE|MERGE|EXECUTE|EXEC)\b[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>Removes known private text and flattens control characters before either log sink.</summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Never truncate raw input: that could turn a complete secret into an unmatched fragment.
        if (input!.Length > MaximumInputCharacters)
        {
            return "[REDACTED: oversized diagnostic text]";
        }

        try
        {
            var sanitized = Assignments.Replace(input, "${key}=" + Redacted);
            sanitized = PrivateText.Replace(sanitized, Redacted);
            var output = new StringBuilder(sanitized.Length);
            foreach (var character in sanitized)
            {
                output.Append(char.IsControl(character) || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format ? ' ' : character);
            }

            sanitized = output.ToString();
            return Encoding.UTF8.GetByteCount(sanitized) > MaximumRecordBytes
                ? "[REDACTED: oversized diagnostic record]"
                : sanitized;
        }
        catch (RegexMatchTimeoutException)
        {
            return "[REDACTED: input too complex to scan]";
        }
    }
}
