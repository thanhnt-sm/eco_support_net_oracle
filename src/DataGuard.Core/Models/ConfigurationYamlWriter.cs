using System.Text;
using YamlDotNet.Serialization;

namespace DataGuard.Core.Models;

/// <summary>Writes parseable configuration with canonical inline guidance for CLI init and the setup wizard.</summary>
public static class ConfigurationYamlWriter
{
    private static readonly IReadOnlyDictionary<string, string> Comments = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GroundTruthMode"] = "Schema source: Snapshot, Full or Manual. Snapshot alone does not prohibit live access; use --offline.",
        ["DefaultProvider"] = "SQL dialect: --provider overrides this; otherwise sqlserver is the fallback. AutoDetectProvider does not override it.",
        ["SnapshotFilePath"] = "Committed schema snapshot; init does not create it. Provider metadata must match DefaultProvider.",
        ["BaselineFilePath"] = "Reviewed suppression baseline, not a schema snapshot. Do not baseline false positives as a fix.",
        ["NamingConvention"] = "Database/C# name matching; SnakeCaseToPascalCase, PascalCaseToSnakeCase or ExactMatch.",
        ["EnableBaseline"] = "Apply the baseline only when its file exists; this does not create one.",
        ["ExcludedProcedures"] = "Reserved setting; currently not applied by CLI acquisition. Not a DG019 suppression list.",
        ["ExcludedEntities"] = "Exact CLR type names for EF entity extraction; not global SQL suppression or glob patterns.",
        ["Oracle"] = "Optional mapping (e.g. Owner and DescribeRefCursors), never true/false. Does not select the provider.",
        ["SqlServer"] = "Optional mapping (e.g. Schema and UseFirstResultSet), never true/false. Does not select the provider.",
        ["MaxDegreeOfParallelism"] = "0 = automatic processor-count concurrency, not disabled or unlimited.",
        ["EnableConcurrentValidation"] = "Run independent rule work concurrently; false uses sequential execution.",
        ["ValidationTimeoutSeconds"] = "Stored setting; CLI does not enforce this as a deadline. VS has its own timeout option.",
        ["MaxViolationQueueSize"] = "Bound on retained violations; overflow is incomplete validation, not a successful truncated run.",
        ["ConnectionString"] = "Leave empty. Supply optional live credentials via a named environment variable; never commit secrets.",
        ["EnableCredentialRotationDetection"] = "Credential-store rotation checks; no live credential is required for snapshot analysis.",
        ["CredentialRotationWarningDays"] = "Rotation warning threshold in days when credential management is used.",
        ["EncryptConnectionStringAtRest"] = "Protect locally stored credentials with the OS backend; does not encrypt this YAML.",
        ["RequireEncryptedCredentialStore"] = "If true, fail when OS protection is unavailable instead of falling back to owner-only plaintext.",
        ["KeyVaultUri"] = "Optional secret-store configuration; leave empty for offline snapshots. Backend support varies.",
        ["AwsRegion"] = "Optional AWS Secrets Manager region; leave empty for offline snapshots.",
        ["VaultAddress"] = "Optional secret-store configuration; leave empty for offline snapshots. Backend support varies.",
        ["EnableAuditLogging"] = "Audit credential operations when the credential provider is used; separate from telemetry.",
        ["AuditLogPath"] = "Empty uses the audit logger's application-data default. IDE-safe suppresses repository-chosen writes.",
        ["AuditKeyFile"] = "Optional integrity key path outside the repository; DATAGUARD_AUDIT_KEY takes precedence. Never put the key here.",
        ["AllowPlaintextConfigFallback"] = "Keep false: do not use plaintext ConnectionString from this file as fallback.",
        ["ManualAssemblyPath"] = "Manual mode only; explicit trust is required to load an assembly. Leave empty for Snapshot.",
        ["AutoDetectProvider"] = "Library smart-default hint; does not change the CLI's explicit DefaultProvider or infer dialect from a snapshot.",
        ["AutoDetectEFContext"] = "IDE-safe project scans may read ModelSnapshot.cs as source; no runtime DbContext execution.",
        ["AutoDetectDapper"] = "Reserved smart-default hint; SQL extraction already detects supported call names.",
        ["EnableSmartDefaults"] = "Library defaults; not permission for connections, assembly loading, or overriding the CLI dialect.",
        ["DefaultSchema"] = "Optional owner/schema for unqualified procedure resolution; set only to your actual schema.",
        ["DefaultPackage"] = "Optional Oracle package for unqualified procedure resolution.",
        ["EnableTelemetry"] = "Opt-in local observability; keep false unless intentionally enabled. Separate from audit logging.",
        ["TelemetryFileDirectory"] = "Optional local observability output directory; empty uses product defaults.",
        ["TelemetryServiceName"] = "Service label for observability records.",
        ["TelemetryServiceVersion"] = "Version label for observability records.",
        ["IncludeTelemetryEventDetails"] = "Opt-in redacted bounded event details; keep false to minimize collected details.",
        ["FailOnUnavailableRules"] = "Unavailable rule inventory can fail the run; distinct from unevaluated contracts (normally exit 3).",
        ["StrictConfig"] = "Unknown top-level keys become errors; invalid known value shapes always fail.",
        ["SnapshotMaxAgeDays"] = "Warn for an old snapshot; 0 or less disables the age warning.",
        ["StrictProcedureContracts"] = "Promote procedure-contract mismatches from warnings to errors.",
        ["Plugins"] = "Optional mapping; plugins still require explicit trusted CLI admission and are not loaded by IDE-safe mode.",
    };

    /// <summary>Serializes all settings and canonical comments; arbitrary handwritten comments are not part of the model.</summary>
    public static string Serialize(DataGuardConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var yaml = new SerializerBuilder().WithIndentedSequences().Build().Serialize(config);
        var output = new StringBuilder("# DataGuard configuration. See docs/guides/configuration.md (and configuration.vi.md).\n"
            + "# Oracle/PostgreSQL/MySQL raw SQL grammar is unavailable: unevaluated is not a syntax-validation pass.\n");
        using var reader = new StringReader(yaml);
        while (reader.ReadLine() is { } line)
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && !char.IsWhiteSpace(line[0]) && Comments.TryGetValue(line[..colon], out var comment))
            {
                line += " # " + comment;
            }

            output.AppendLine(line);
        }

        return output.ToString();
    }
}
