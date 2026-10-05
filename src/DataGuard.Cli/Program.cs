using System.CommandLine;
using System.Reflection;
using DataGuard.Cli;
using DataGuard.Cli.Commands;

// Bound every regex in the process before any type with a static Regex field is touched (red-team F11).
RegexMatchTimeoutStartup.Apply();

var assembly = Assembly.GetExecutingAssembly();
var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? assembly.GetName().Version?.ToString() ?? "0.1.0";

var options = new CommonOptions();

var rootCommand = new RootCommand("DataGuard - Entity ↔ SP/Raw SQL Contract Validator");

rootCommand.Add(ValidateCommand.Create(options));
rootCommand.Add(PreflightCommand.Create(options));
rootCommand.Add(BaselineCommand.Create(options));
rootCommand.Add(SnapshotCommands.Create(options));
rootCommand.Add(InitCommand.Create());
rootCommand.Add(HookCommand.Create());
rootCommand.Add(ConfigCommands.Create(options));
rootCommand.Add(OracleCheckCommand.Create(options));
rootCommand.Add(MigrateCommand.Create(options));
rootCommand.Add(AssessCommand.Create(options));
rootCommand.Add(VersionCommand.Create(version));
rootCommand.Add(ScanCommand.Create(options));
rootCommand.Add(VerifyShapeCommand.Create(options));

var parseResult = rootCommand.Parse(args, new ParserConfiguration());
using var invocationCancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    try
    {
        invocationCancellation.Cancel();
    }
    catch (ObjectDisposedException)
    {
    }
};
try
{
    var exitCode = await parseResult.InvokeAsync(new InvocationConfiguration(), invocationCancellation.Token);
    return Environment.ExitCode != 0 ? Environment.ExitCode : exitCode;
}
catch (OperationCanceledException)
{
    return 130;
}
