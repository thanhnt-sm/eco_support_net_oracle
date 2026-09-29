namespace DataGuard.Cli;

/// <summary>
/// In-process environment scrubbing for IDE-safe mode. <see cref="IdeSafePolicy.Apply"/> only sanitises the
/// merged configuration; downstream components (<c>CredentialManager</c>, <c>ZeroTrustCredentialProvider</c>,
/// <c>AutoDetectionEngine</c>) re-read the environment directly, so the secrets are cleared here before any
/// of them can run. Clearing is process-local and never touches the host's environment.
/// </summary>
public static class IdeSafeEnvironment
{
    /// <summary>The user-credential variable that <c>--allow-env-connection</c> may keep.</summary>
    public const string ConnectionVariable = "DATAGUARD_CONNECTION_STRING";

    private static readonly string[] FixedSecretVariables =
    {
        "VAULT_TOKEN",
        "VAULT_ADDR",
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_SESSION_TOKEN",
        "AWS_PROFILE",
        "PGPASSWORD",
        "MYSQL_PWD",
    };

    /// <summary>
    /// Non-secret <c>DATAGUARD_*</c> names that survive scrubbing. Every other <c>DATAGUARD_</c> variable is treated as a
    /// credential because <c>ZeroTrustCredentialProvider</c> resolves <c>DATAGUARD_&lt;CREDENTIAL-NAME&gt;</c> for any caller-supplied name.
    /// </summary>
    private static readonly string[] NonSecretDataGuardVariables =
    {
        "DATAGUARD_PROVIDER",
        "DATAGUARD_CLI_PATH",
    };

    /// <summary>
    /// Returns the variable names that <see cref="Scrub"/> would clear, given the current process environment.
    /// Pure selection so it can be unit tested without mutating the environment.
    /// </summary>
    /// <param name="allowEnvConnection">When true, <see cref="ConnectionVariable"/> is kept.</param>
    /// <param name="environmentNames">Names present in the environment (any casing).</param>
    public static IReadOnlyList<string> SelectVariablesToClear(bool allowEnvConnection, IEnumerable<string> environmentNames)
    {
        ArgumentNullException.ThrowIfNull(environmentNames);

        var toClear = new List<string>();
        foreach (var name in environmentNames)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var isConnection = name.Equals(ConnectionVariable, StringComparison.OrdinalIgnoreCase);
            if (isConnection && allowEnvConnection)
            {
                continue;
            }

            if (isConnection
                || FixedSecretVariables.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.StartsWith("AWS_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)
                || IsDataGuardCredentialVariable(name))
            {
                toClear.Add(name);
            }
        }

        return toClear;
    }

    private static bool IsDataGuardCredentialVariable(string name) =>
        name.StartsWith("DATAGUARD_", StringComparison.OrdinalIgnoreCase)
        && !NonSecretDataGuardVariables.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clears every credential-bearing variable from the current process environment and returns the names cleared.
    /// Failures to read or clear a variable are swallowed: scrubbing is best-effort hardening, never a reason to abort.
    /// </summary>
    /// <param name="allowEnvConnection">When true, <see cref="ConnectionVariable"/> is kept for the adapter.</param>
    public static IReadOnlyList<string> Scrub(bool allowEnvConnection)
    {
        var names = new List<string>();
        try
        {
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key)
                {
                    names.Add(key);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or OutOfMemoryException)
        {
            return Array.Empty<string>();
        }

        var cleared = new List<string>();
        foreach (var name in SelectVariablesToClear(allowEnvConnection, names))
        {
            try
            {
                Environment.SetEnvironmentVariable(name, null);
                cleared.Add(name);
            }
            catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException)
            {
                // Leave the variable in place; the policy-sanitised configuration still blocks its use in this run.
            }
        }

        return cleared;
    }
}
