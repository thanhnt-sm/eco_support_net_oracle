import { ChildProcess, spawn } from "child_process";
import { once } from "events";
import { promises as fs } from "fs";
import * as os from "os";
import * as path from "path";
import * as vscode from "vscode";
import { redactSensitiveText, resolveWorkspaceConfigPath } from "./security";

const RUN_VALIDATION_COMMAND = "dataguard.runValidation";
const CANCEL_VALIDATION_COMMAND = "dataguard.cancelValidation";
const OUTPUT_CHANNEL_NAME = "DataGuard";

interface SarifLog {
    runs?: SarifRun[];
}

interface SarifRun {
    results?: SarifResult[];
}

interface SarifResult {
    level?: "error" | "warning" | "note" | "none";
    message?: { text?: string };
    locations?: SarifLocation[];
}

interface SarifRegion {
    startLine?: number;
    startColumn?: number;
    endLine?: number;
    endColumn?: number;
}

interface SarifLocation {
    physicalLocation?: {
        artifactLocation?: { uri?: string };
        region?: SarifRegion;
    };
}

interface ValidationRun {
    readonly child: ChildProcess;
    readonly outputDirectory: string;
    readonly outputPath: string;
    timedOut: boolean;
    cancelled: boolean;
    timeout: NodeJS.Timeout;
}

let statusBarItem: vscode.StatusBarItem | undefined;
let outputChannel: vscode.OutputChannel | undefined;
let diagnostics: vscode.DiagnosticCollection | undefined;
const activeRuns = new Map<string, ValidationRun>();

export function activate(context: vscode.ExtensionContext): void {
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    statusBarItem.name = "DataGuard";
    setStatus("idle");
    statusBarItem.command = RUN_VALIDATION_COMMAND;
    statusBarItem.show();

    diagnostics = vscode.languages.createDiagnosticCollection("dataguard");
    context.subscriptions.push(statusBarItem, diagnostics);
    context.subscriptions.push(
        vscode.commands.registerCommand(RUN_VALIDATION_COMMAND, () => runValidation()),
        vscode.commands.registerCommand(CANCEL_VALIDATION_COMMAND, () => cancelValidation()),
    );
}

export function deactivate(): void {
    for (const run of activeRuns.values()) {
        clearTimeout(run.timeout);
        terminateProcessTree(run.child);
        void fs.rm(run.outputDirectory, { recursive: true, force: true });
    }
    activeRuns.clear();
    statusBarItem?.dispose();
    outputChannel?.dispose();
    diagnostics?.dispose();
    statusBarItem = undefined;
    outputChannel = undefined;
    diagnostics = undefined;
}

async function runValidation(): Promise<void> {
    if (!vscode.workspace.isTrusted) {
        void vscode.window.showWarningMessage("DataGuard does not run CLI commands in an untrusted workspace. Trust this workspace first.");
        return;
    }

    const workspaceFolder = await selectWorkspaceFolder();
    if (!workspaceFolder) {
        void vscode.window.showWarningMessage("DataGuard: open a workspace folder first.");
        return;
    }

    const runKey = workspaceFolder.uri.toString();
    if (activeRuns.has(runKey)) {
        void vscode.window.showInformationMessage("DataGuard validation is already running for this workspace.");
        return;
    }

    const configuration = vscode.workspace.getConfiguration("dataguard", workspaceFolder.uri);
    if (!configuration.get<boolean>("enabled", true)) {
        void vscode.window.showWarningMessage("DataGuard validation is disabled (dataguard.enabled).");
        return;
    }

    let configPath: string;
    try {
        configPath = resolveWorkspaceConfigPath(
            workspaceFolder.uri.fsPath,
            configuration.get<string>("configPath", ".dataguard.yml"),
        );
    } catch (error) {
        void vscode.window.showErrorMessage(`DataGuard: ${error instanceof Error ? error.message : String(error)}`);
        return;
    }

    const cliPath = vscode.workspace.getConfiguration("dataguard").get<string>("cliPath", "dataguard");
    const timeoutSeconds = Math.min(900, Math.max(5, configuration.get<number>("timeoutSeconds", 60)));
    const channel = getOutputChannel();
    channel.clear();
    channel.show(true);
    diagnostics?.clear();

    const outputDirectory = await fs.mkdtemp(path.join(os.tmpdir(), "dataguard-"));
    const outputPath = path.join(outputDirectory, "validation.sarif");
    const args = ["validate", "--config", configPath, "--format", "sarif", "--output", outputPath];
    channel.appendLine("[DataGuard] Validation started. Detailed CLI output is not displayed to prevent credential disclosure.");

    let child: ChildProcess;
    try {
        child = spawn(cliPath, args, {
            cwd: workspaceFolder.uri.fsPath,
            detached: process.platform !== "win32",
            shell: false,
            windowsHide: true,
        });
    } catch (error) {
        await fs.rm(outputDirectory, { recursive: true, force: true });
        showStartError(error, channel);
        return;
    }

    const run: ValidationRun = {
        child,
        outputDirectory,
        outputPath,
        timedOut: false,
        cancelled: false,
        timeout: setTimeout(() => {
            run.timedOut = true;
            terminateProcessTree(child);
        }, timeoutSeconds * 1000),
    };
    activeRuns.set(runKey, run);
    setStatus("running");

    try {
        const exitCode = await waitForExit(child, channel);
        if (run.timedOut) {
            channel.appendLine(`\n[DataGuard] validation timed out after ${timeoutSeconds} seconds.`);
            setStatus("error");
            void vscode.window.showErrorMessage(`DataGuard validation timed out after ${timeoutSeconds} seconds.`);
            return;
        }
        if (run.cancelled) {
            channel.appendLine("\n[DataGuard] validation cancelled.");
            return;
        }

        await loadDiagnostics(outputPath, workspaceFolder, diagnostics, channel);
        channel.appendLine(`\n[DataGuard] exited with code ${exitCode ?? "unknown"}`);
        if (exitCode === 0) {
            setStatus("idle");
        } else if (exitCode === 1) {
            setStatus("warning");
            void vscode.window.showWarningMessage("DataGuard found contract violations. See Problems or the DataGuard output channel.");
        } else {
            setStatus("error");
            void vscode.window.showErrorMessage("DataGuard could not complete validation. See the DataGuard output channel.");
        }
    } finally {
        clearTimeout(run.timeout);
        activeRuns.delete(runKey);
        await fs.rm(outputDirectory, { recursive: true, force: true });
        if (activeRuns.size === 0 && !run.timedOut && !run.cancelled) {
            setStatus("idle");
        }
    }
}

async function cancelValidation(): Promise<void> {
    const workspaceFolder = await selectWorkspaceFolder();
    if (!workspaceFolder) {
        return;
    }

    const run = activeRuns.get(workspaceFolder.uri.toString());
    if (!run) {
        void vscode.window.showInformationMessage("No DataGuard validation is running for this workspace.");
        return;
    }

    run.cancelled = true;
    terminateProcessTree(run.child);
    setStatus("warning");
}

function getOutputChannel(): vscode.OutputChannel {
    if (!outputChannel) {
        outputChannel = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
    }
    return outputChannel;
}

async function selectWorkspaceFolder(): Promise<vscode.WorkspaceFolder | undefined> {
    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length === 0) {
        return undefined;
    }
    if (folders.length === 1) {
        return folders[0];
    }

    const selected = await vscode.window.showQuickPick(
        folders.map((folder) => ({ label: folder.name, folder })),
        { placeHolder: "Select the workspace for DataGuard validation" },
    );
    return selected?.folder;
}

async function waitForExit(child: ChildProcess, channel: vscode.OutputChannel): Promise<number | null> {
    child.stdout?.resume();
    child.stderr?.resume();

    try {
        const [code] = await once(child, "close");
        return code as number | null;
    } catch (error) {
        showStartError(error, channel);
        return null;
    }
}

async function loadDiagnostics(
    outputPath: string,
    workspaceFolder: vscode.WorkspaceFolder,
    collection: vscode.DiagnosticCollection | undefined,
    channel: vscode.OutputChannel,
): Promise<void> {
    if (!collection) {
        return;
    }
    let sarif: SarifLog;
    try {
        sarif = JSON.parse(await fs.readFile(outputPath, "utf8")) as SarifLog;
    } catch (error) {
        channel.appendLine(`\n[DataGuard] SARIF output was unavailable or invalid: ${redactSensitiveText(String(error))}`);
        return;
    }

    collection.clear();
    const byDocument = new Map<string, { uri: vscode.Uri; diagnostics: vscode.Diagnostic[] }>();
    for (const result of sarif.runs?.flatMap((run) => run.results ?? []) ?? []) {
        const location = result.locations?.[0]?.physicalLocation;
        const sourceUri = location?.artifactLocation?.uri;
        if (!sourceUri) {
            continue;
        }

        const filePath = path.isAbsolute(sourceUri)
            ? sourceUri
            : path.resolve(workspaceFolder.uri.fsPath, sourceUri);
        const uri = vscode.Uri.file(filePath);
        const key = uri.toString();
        const entry = byDocument.get(key) ?? { uri, diagnostics: [] };
        entry.diagnostics.push(new vscode.Diagnostic(
            toRange(location?.region),
            redactSensitiveText(result.message?.text ?? "DataGuard contract violation"),
            toSeverity(result.level),
        ));
        byDocument.set(key, entry);
    }

    for (const { uri, diagnostics: documentDiagnostics } of byDocument.values()) {
        collection.set(uri, documentDiagnostics);
    }
}

function toRange(region: SarifRegion | undefined): vscode.Range {
    const startLine = Math.max(0, (region?.startLine ?? 1) - 1);
    const startColumn = Math.max(0, (region?.startColumn ?? 1) - 1);
    const endLine = Math.max(startLine, (region?.endLine ?? startLine + 1) - 1);
    const endColumn = Math.max(startColumn + 1, (region?.endColumn ?? startColumn + 1) - 1);
    return new vscode.Range(startLine, startColumn, endLine, endColumn);
}

function toSeverity(level: SarifResult["level"]): vscode.DiagnosticSeverity {
    switch (level) {
        case "error": return vscode.DiagnosticSeverity.Error;
        case "warning": return vscode.DiagnosticSeverity.Warning;
        case "note": return vscode.DiagnosticSeverity.Information;
        default: return vscode.DiagnosticSeverity.Hint;
    }
}

function terminateProcessTree(child: ChildProcess): void {
    if (!child.pid) {
        return;
    }

    if (process.platform === "win32") {
        const killer = spawn("taskkill", ["/pid", String(child.pid), "/T", "/F"], { shell: false, windowsHide: true });
        killer.unref();
        return;
    }

    try {
        process.kill(-child.pid, "SIGTERM");
    } catch {
        child.kill("SIGTERM");
    }
    setTimeout(() => {
        try {
            process.kill(-child.pid!, "SIGKILL");
        } catch {
            // The process group already exited.
        }
    }, 3000).unref();
}


function showStartError(error: unknown, channel: vscode.OutputChannel): void {
    const err = error as NodeJS.ErrnoException;
    const message = err.code === "ENOENT"
        ? "DataGuard CLI was not found. Install it with 'dotnet tool install -g DataGuard.Cli' or set dataguard.cliPath in User Settings."
        : `Failed to start DataGuard: ${redactSensitiveText(err.message ?? String(error))}`;
    channel.appendLine(`\n${message}`);
    void vscode.window.showErrorMessage(message);
}

function setStatus(state: "idle" | "running" | "warning" | "error"): void {
    if (!statusBarItem) {
        return;
    }
    const values = {
        idle: ["$(shield) DataGuard", "Run DataGuard contract validation"],
        running: ["$(sync~spin) DataGuard", "DataGuard validation is running"],
        warning: ["$(warning) DataGuard", "DataGuard found contract violations or was cancelled"],
        error: ["$(error) DataGuard", "DataGuard validation failed"],
    } as const;
    statusBarItem.text = values[state][0];
    statusBarItem.tooltip = values[state][1];
}

function quoteForDisplay(argument: string): string {
    return /\s/.test(argument) ? `"${argument}"` : argument;
}
