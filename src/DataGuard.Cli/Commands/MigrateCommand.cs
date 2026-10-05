using System.CommandLine;
using DataGuard.Core.Baseline;

namespace DataGuard.Cli.Commands;

/// <summary><c>migrate</c>: migrate a legacy v1 baseline file to v2.</summary>
internal static class MigrateCommand
{
    public static Command Create(CommonOptions options)
    {
        var baselinePathOption = options.BaselinePathOption;

        var migrateCommand = new Command("migrate", "Migrate a legacy baseline file (v1) to v2")
        {
            baselinePathOption,
        };

        migrateCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var baselinePath = result.GetValue(baselinePathOption);
                var path = baselinePath ?? ".dataguard-baseline.json";

                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"Baseline file not found: {path}");
                    Environment.ExitCode = 1;
                    return;
                }

                var manager = new BaselineManager(path);
                var migrated = await manager.MigrateBaselineAsync();
                if (migrated == null)
                {
                    Console.WriteLine($"Baseline '{path}' is already v2 or not a legacy v1 baseline");
                    return;
                }

                Console.WriteLine($"Migrated baseline to v2: {migrated.Violations.Count} violations, schema hash {migrated.SchemaHash}");
            });

        return migrateCommand;
    }
}
