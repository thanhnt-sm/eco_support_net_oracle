namespace DataGuard.Cli;

/// <summary>
/// Process-wide regex match timeout (red-team F11). <see cref="System.Text.RegularExpressions.Regex"/> reads the
/// <c>REGEX_DEFAULT_MATCH_TIMEOUT</c> AppContext switch exactly once, when its static state is first initialised,
/// so <see cref="Apply"/> must be the first statement of <c>Main</c> - before any type with a static
/// <c>Regex</c> field is touched. Every un-timed regex in Core, the adapters and classification then throws
/// <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/> after <see cref="DefaultMatchTimeout"/>.
/// </summary>
public static class RegexMatchTimeoutStartup
{
    /// <summary>AppContext data key consumed by the runtime's <c>Regex</c> static initialiser.</summary>
    public const string AppContextKey = "REGEX_DEFAULT_MATCH_TIMEOUT";

    /// <summary>Wall-clock bound applied to every regex that does not pass its own timeout.</summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Registers <see cref="DefaultMatchTimeout"/> as the process default. Idempotent.</summary>
    public static void Apply() => AppContext.SetData(AppContextKey, DefaultMatchTimeout);

    /// <summary>The currently registered default, or null when <see cref="Apply"/> has not run in this process.</summary>
    public static TimeSpan? Configured => AppContext.GetData(AppContextKey) as TimeSpan?;
}
