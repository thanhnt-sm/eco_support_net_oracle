using System.CommandLine;
using DataGuard.Cli.Hooks;

namespace DataGuard.Cli.Commands;

/// <summary><c>hook install|status|uninstall</c>: manage DataGuard-managed pre-commit hooks.</summary>
internal static class HookCommand
{
    public static Command Create()
    {
        var hookTypeOption = new Option<string>("--type");
        hookTypeOption.Description = "Hook integration: auto, native, husky, or lefthook";
        hookTypeOption.DefaultValueFactory = (_) => "auto";
        var hookForceOption = new Option<bool>("--force");
        hookForceOption.Description = "Allow replacement only of a DataGuard-managed hook";
        var hookCommand = new Command("hook", "Install, inspect, or remove DataGuard-managed pre-commit hooks");
        var hookInstallCommand = new Command("install", "Install a DataGuard-managed pre-commit hook")
        {
            hookTypeOption, hookForceOption,
        };
        hookInstallCommand.SetAction(async (ParseResult result, System.Threading.CancellationToken ct) =>
        {
            if (!TryParseHookType(result.GetValue(hookTypeOption), out var hookType))
            {
                Console.Error.WriteLine("Unsupported hook type. Use auto, native, husky, or lefthook.");
                Environment.ExitCode = 2;
                return;
            }

            var installation = await PreCommitHookInstaller.InstallAsync(
                hookType: hookType,
                force: result.GetValue(hookForceOption),
                cancellationToken: ct);
            Console.WriteLine(installation.Message);
            if (!installation.Success)
            {
                Environment.ExitCode = 1;
            }
        });

        var hookStatusCommand = new Command("status", "Show detected pre-commit hook status");
        hookStatusCommand.SetAction((ParseResult _) => Console.WriteLine(PreCommitHookInstaller.GetStatus()));

        var hookUninstallCommand = new Command("uninstall", "Remove only DataGuard-managed pre-commit hooks");
        hookUninstallCommand.SetAction(async (ParseResult _) =>
        {
            var removal = await PreCommitHookInstaller.UninstallAsync();
            Console.WriteLine(removal.Message);
            if (!removal.Success)
            {
                Environment.ExitCode = 1;
            }
        });

        hookCommand.Add(hookInstallCommand);
        hookCommand.Add(hookStatusCommand);
        hookCommand.Add(hookUninstallCommand);

        return hookCommand;
    }

    private static bool TryParseHookType(string? value, out HookType hookType)
    {
        hookType = value?.Trim().ToLowerInvariant() switch
        {
            "auto" => HookType.Auto,
            "native" or "nativegit" or "native-git" => HookType.NativeGit,
            "husky" => HookType.Husky,
            "lefthook" => HookType.Lefthook,
            _ => HookType.None,
        };

        return hookType != HookType.None;
    }
}
