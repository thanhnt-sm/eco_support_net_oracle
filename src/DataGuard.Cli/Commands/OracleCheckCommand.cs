using System.CommandLine;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Reporting;
using DataGuard.Oracle.Adapter;
using Microsoft.CodeAnalysis;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.OutputSinks;

namespace DataGuard.Cli.Commands;

/// <summary><c>oracle-check</c>: Oracle-specific dialect and length checks.</summary>
internal static class OracleCheckCommand
{
    public static Command Create(CommonOptions options)
    {
        var connectionOption = options.ConnectionOption;
        var configOption = options.ConfigOption;
        var outputOption = options.OutputOption;
        var formatOption = options.FormatOption;
        var verboseOption = options.VerboseOption;
        var schemaOption = options.SchemaOption;
        var packageOption = options.PackageOption;
        var connectionEnvOption = options.ConnectionEnvOption;

        var oracleCheckCommand = new Command("oracle-check", "Run Oracle-specific dialect and length checks")
        {
            connectionOption, connectionEnvOption, configOption, outputOption, formatOption, verboseOption, schemaOption, packageOption,
        };

        oracleCheckCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var configPath = result.GetValue(configOption);
                var output = result.GetValue(outputOption);
                var format = result.GetValue(formatOption) ?? "text";
                var verbose = result.GetValue(verboseOption);
                var schema = result.GetValue(schemaOption);
                var package = result.GetValue(packageOption);
                if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), "oracle", ct) is not { } resolved)
                {
                    return;
                }

                var config = resolved.Configuration with { GroundTruthMode = GroundTruthMode.Full };
                config = config with
                {
                    DefaultSchema = schema ?? config.DefaultSchema,
                    DefaultPackage = package ?? config.DefaultPackage
                };

                if (!string.IsNullOrEmpty(output) && RefuseUnsafeOutput(output, "SARIF"))
                {
                    return;
                }

                try
                {
                    var violations = await RunOracleValidationAsync(config, verbose, ct);

                    var emitter = new DiagnosticEmitter();
                    emitter.AddDiagnosticSink(new ConsoleDiagnosticSink());

                    if (!string.IsNullOrEmpty(output))
                    {
                        emitter.AddSarifSink(new FileSarifSink(output));
                    }

                    await emitter.EmitAsync(violations, ct);

                    var hasErrors = violations.Any(v => v.Severity == DiagnosticSeverity.Error);
                    if (verbose)
                    {
                        Console.WriteLine($"Oracle check complete: {violations.Count} issues");
                    }

                    Environment.ExitCode = hasErrors ? 1 : 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Oracle check cancelled.");
                    Environment.ExitCode = 130;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Oracle check failed: {ex.Message}");
                    if (verbose)
                    {
                        Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
                    }

                    Environment.ExitCode = 1;
                }
            });

        return oracleCheckCommand;
    }

    private static async Task<IReadOnlyList<ContractViolation>> RunOracleValidationAsync(
        DataGuardConfiguration config,
        bool verbose,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var violations = new List<ContractViolation>();

        if (string.IsNullOrEmpty(config.ConnectionString))
        {
            throw new InvalidOperationException("Oracle check requires --connection");
        }

        var owner = config.DefaultSchema ?? config.Oracle?.Owner;

        // Read NLS length semantics (CHAR vs BYTE) to drive byte-overflow detection.
        var semanticsResolver = new LengthSemanticsResolver(config.ConnectionString);
        var semantics = await semanticsResolver.ResolveAsync(cancellationToken);

        // Read the full schema (all tables' columns) for the owner.
        var columnsReader = new AllTabColumnsReader(config.ConnectionString);
        var tables = new List<DatabaseTableDescriptor>();
        if (!string.IsNullOrEmpty(owner))
        {
            var allColumns = await columnsReader.GetAllColumnsAsync(owner, cancellationToken);
            tables = allColumns
                .Select(kv => new DatabaseTableDescriptor(kv.Key, kv.Value))
                .ToList();
        }

        var schemaDescriptor = new DatabaseSchemaDescriptor(
            Id: "oracle-schema",
            Tables: tables,
            LengthSemantics: semantics == LengthSemantics.Byte ? "BYTE" : "CHAR");

        // Run Oracle dialect checks against the schema column types (unmapped type detection).
        var checker = new OracleDialectChecker();
        var sqlText = string.Join(" ", tables.SelectMany(t => t.Columns).Select(c => $"{c.DataType} {c.Name}"));
        violations.AddRange(checker.CheckRawSqlUnmappedTypeUsage(sqlText, isOracleContext: true));

        if (verbose)
        {
            Console.WriteLine($"Oracle NLS length semantics: {semantics}");
            Console.WriteLine($"Oracle schema '{owner}': {tables.Count} tables, {tables.Sum(t => t.Columns.Count)} columns");
        }

        return violations;
    }
}
