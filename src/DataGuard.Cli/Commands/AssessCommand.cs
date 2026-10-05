using System.CommandLine;
using System.Text.Json;
using DataGuard.Core.Assessment;
using DataGuard.Core.Assessment.Internal;
using DataGuard.Core.Reporting;
using Microsoft.CodeAnalysis;
using static DataGuard.Cli.Services.OutputSinks;

namespace DataGuard.Cli.Commands;

/// <summary><c>assess</c>: read-only environment/dependency/config assessment.</summary>
internal static class AssessCommand
{
    public static Command Create(CommonOptions options)
    {
        var outputOption = options.OutputOption;
        var formatOption = options.FormatOption;
        var verboseOption = options.VerboseOption;
        var progressOption = options.ProgressOption;
        var ideSafeOption = options.IdeSafeOption;

        var assessWorkspaceOption = new Option<string>("--workspace");
        assessWorkspaceOption.Description = "Workspace root to assess (default: current directory)";
        assessWorkspaceOption.DefaultValueFactory = (_) => ".";
        var assessFilterOption = new Option<string[]>("--project-filter");
        assessFilterOption.Description = "Optional project path filters (substring, case-insensitive)";
        assessFilterOption.AllowMultipleArgumentsPerToken = true;
        var remoteAdvisoriesOption = new Option<string>("--remote-advisories");
        remoteAdvisoriesOption.Description = "Optional remote advisory provider; only 'osv' is supported";
        var allowNetworkOption = new Option<bool>("--allow-network");
        allowNetworkOption.Description = "Permit explicitly requested advisory egress for this assessment";
        var remotePublicPackageOption = new Option<string[]>("--remote-public-package");
        remotePublicPackageOption.Description = "Public NuGet package ID approved for advisory lookup; repeat for each package";
        remotePublicPackageOption.AllowMultipleArgumentsPerToken = true;
        var assessCommand = new Command("assess", "Run read-only environment/dependency/config assessment and emit a structured report")
        {
            assessWorkspaceOption,
            assessFilterOption,
            remoteAdvisoriesOption,
            allowNetworkOption,
            remotePublicPackageOption,
            outputOption,
            formatOption,
            verboseOption,
            progressOption,
            ideSafeOption,
        };

        assessCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var workspace = result.GetValue(assessWorkspaceOption);
                var filters = result.GetValue(assessFilterOption);
                var output = result.GetValue(outputOption);
                var format = result.GetValue(formatOption) ?? "text";
                var verbose = result.GetValue(verboseOption);
                var remoteProvider = result.GetValue(remoteAdvisoriesOption);
                var allowNetwork = result.GetValue(allowNetworkOption);
                var approvedPackages = result.GetValue(remotePublicPackageOption) ?? Array.Empty<string>();
                if (result.GetValue(ideSafeOption))
                {
                    Console.Error.WriteLine(IdeSafePolicy.ActiveLine);
                    var rejectedOption = IdeSafePolicy.FirstRejectedAssessOption(allowNetwork, remoteProvider);
                    if (rejectedOption is not null)
                    {
                        Console.Error.WriteLine(IdeSafePolicy.FormatRejectionLine(rejectedOption));
                        Environment.ExitCode = 2;
                        return;
                    }

                    IdeSafeEnvironment.Scrub(allowEnvConnection: false);
                }

                ProgressEmitter? progress = result.GetValue(progressOption) ? new ProgressEmitter(Console.Error, enabled: true) : null;
                var normalizedFormat = format?.ToLowerInvariant() ?? "text";
                if (normalizedFormat is not ("text" or "json" or "sarif"))
                {
                    Console.Error.WriteLine($"Unsupported --format '{format}' for assess. Supported values: text, json, sarif.");
                    Environment.ExitCode = 2;
                    return;
                }

                if (remoteProvider is not null && !string.Equals(remoteProvider, "osv", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("Unsupported --remote-advisories provider. Supported value: osv.");
                    Environment.ExitCode = 2;
                    return;
                }

                if (normalizedFormat is not "text" && string.IsNullOrEmpty(output))
                {
                    Console.Error.WriteLine($"--format {normalizedFormat} requires --output <path>; DataGuard never writes machine-readable output to stdout.");
                    Environment.ExitCode = 2;
                    return;
                }

                progress?.Emit(new ProgressEvent(
                    ProgressEventKind.PhaseStarted,
                    "Assessing workspace",
                    "Assessing the workspace configuration and dependencies."));

                try
                {
                    var request = new AssessmentRequest
                    {
                        WorkspaceRoot = Path.GetFullPath(workspace ?? "."),
                        ProjectFilters = filters ?? Array.Empty<string>(),
                        AllowRemoteLookups = string.Equals(remoteProvider, "osv", StringComparison.OrdinalIgnoreCase),
                    };
                    var policy = new RemoteAdvisoryPolicy
                    {
                        AllowRemoteLookups = request.AllowRemoteLookups,
                        AllowNetwork = allowNetwork,
                        Provider = remoteProvider ?? "osv",
                        ApprovedPublicPackageIds = new HashSet<string>(approvedPackages.Where(package => !string.IsNullOrWhiteSpace(package)), StringComparer.OrdinalIgnoreCase),
                    };
                    var report = await RunAssessmentWithRemoteAdvisories(request, policy, ct);
                    progress?.Emit(new ProgressEvent(
                        ProgressEventKind.PhaseCompleted,
                        "Assessing workspace",
                        "Workspace assessment completed.",
                        new Dictionary<string, object?>
                        {
                            ["FindingCount"] = report.Findings.Count,
                            ["ToolErrorCount"] = report.Errors.Count,
                        }));

                    // Cancellation is causal: do not publish a partial machine-readable artifact.
                    if (ct.IsCancellationRequested || report.Errors.Any(error => error.Code == "DG1006"))
                    {
                        Console.Error.WriteLine("Assessment cancelled.");
                        Environment.ExitCode = 130;
                        return;
                    }

                    if (normalizedFormat == "json")
                    {
                        await AssessmentReportWriter.WriteJsonAsync(report, output!, ct);
                        Console.WriteLine($"Assessment JSON written to {output}");
                    }
                    else if (normalizedFormat == "sarif")
                    {
                        await WriteSarifAssessment(report, output!, ct);
                        Console.WriteLine($"Assessment SARIF written to {output}");
                    }
                    else if (verbose)
                    {
                        foreach (var finding in report.Findings)
                        {
                            Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
                            foreach (var evidence in finding.Evidence)
                            {
                                Console.WriteLine($"    at {evidence.Path}{(evidence.Line is { } l ? $":{l}" : string.Empty)}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"DataGuard assessment: {report.Summary.TotalFindings} findings ({report.Summary.Critical} critical, {report.Summary.Errors_} errors, {report.Summary.Warnings} warnings, {report.Summary.Information} info), {report.Summary.ToolErrors} tool errors");
                    }

                    foreach (var error in report.Errors)
                    {
                        Console.Error.WriteLine($"[{error.Code}] {RelativizeToWorkspace(request.WorkspaceRoot, error.Path)}: {error.Message}");
                    }

                    // Findings are a failed assessment; operational/tool errors use the frozen code 4.
                    Environment.ExitCode = report.Errors.Count > 0 ? 4 : report.Findings.Count > 0 ? 1 : 0;

                    progress?.Emit(new ProgressEvent(
                        ProgressEventKind.Summary,
                        "Assessment complete",
                        "Assessment completed.",
                        new Dictionary<string, object?>
                        {
                            ["CriticalCount"] = report.Summary.Critical,
                            ["ErrorCount"] = report.Summary.Errors_,
                            ["WarningCount"] = report.Summary.Warnings,
                            ["ToolErrorCount"] = report.Summary.ToolErrors,
                        }));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Assessment cancelled.");
                    Environment.ExitCode = 130;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Assessment failed: {(verbose ? ex.ToString() : ex.Message)}");
                    Environment.ExitCode = 4;
                }
            });

        return assessCommand;
    }

    private static async Task<AssessmentReport> RunAssessmentWithRemoteAdvisories(AssessmentRequest request, RemoteAdvisoryPolicy policy, CancellationToken cancellationToken)
    {
        using var advisoryClient = new OsvAdvisoryClient();
        return await AssessmentEngine.RunAsync(request, policy, advisoryClient, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSarifAssessment(AssessmentReport report, string outputPath, CancellationToken cancellationToken)
    {
        var sarif = new SarifLog
        {
            Version = "2.1.0",
            Runs = new List<Run>
            {
                new Run
                {
                    Tool = new Tool { Driver = new ToolComponent { Name = "DataGuard.Assessment", Version = report.ToolVersion } },
                    Results = report.Findings.Select(f => new Result
                    {
                        RuleId = f.RuleId,
                        Level = f.Severity switch
                        {
                            FindingSeverity.Critical or FindingSeverity.Error => "error",
                            FindingSeverity.Warning => "warning",
                            _ => "note",
                        },
                        Message = new Message { Text = f.Message },
                        Locations = f.Evidence.Where(e => e.Path is not null).Select(e => new SarifLocation
                        {
                            PhysicalLocation = new PhysicalLocation
                            {
                                ArtifactLocation = new ArtifactLocation { Uri = e.Path! },
                                Region = e.Line is { } line ? new Region { StartLine = line } : new Region(),
                            },
                        }).ToList(),
                    }).ToList(),
                },
            },
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        if (!IsSafeWritablePath(outputPath))
        {
            throw new InvalidOperationException("Refusing to write SARIF through a symbolic link or invalid path.");
        }

        await WriteTextAtomicallyAsync(outputPath, JsonSerializer.Serialize(sarif, jsonOptions), cancellationToken);
    }
}
