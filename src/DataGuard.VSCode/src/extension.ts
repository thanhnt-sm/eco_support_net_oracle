import { ChildProcess, spawn } from "child_process";
import { createHash } from "crypto";
import { once } from "events";
import { promises as fs } from "fs";
import * as os from "os";
import * as path from "path";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions } from "vscode-languageclient/node";
import { redactAndBoundSensitiveText, redactSensitiveText, resolveWorkspaceConfigPath, resolveWorkspaceSarifPath } from "./security";
import { RunCoordinator } from "./run-coordinator";
import { buildCliArguments, normalizeProvider } from "./command-args";

const RUN_VALIDATION_COMMAND = "dataguard.runValidation";
const CANCEL_VALIDATION_COMMAND = "dataguard.cancelValidation";
const ASSESS_COMMAND = "dataguard.assess";
const SNAPSHOT_COMMAND = "dataguard.refreshSnapshot";
const BASELINE_COMMAND = "dataguard.createBaseline";
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
    readonly outputPath?: string;
    timedOut: boolean;
    cancelled: boolean;
    timeout: NodeJS.Timeout;
    cancel: () => void;
}

interface ChildExit {
    readonly code: number | null;
    readonly output: string;
}

const MAX_CLI_OUTPUT = 16 * 1024;

let statusBarItem: vscode.StatusBarItem | undefined;
let outputChannel: vscode.OutputChannel | undefined;
let diagnostics: vscode.DiagnosticCollection | undefined;
const runCoordinator = new RunCoordinator<ValidationRun>();
let languageClient: LanguageClient | undefined;

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
        vscode.commands.registerCommand(ASSESS_COMMAND, () => runAssessment()),
        vscode.commands.registerCommand(SNAPSHOT_COMMAND, () => runConfirmedOperation("snapshot")),
        vscode.commands.registerCommand(BASELINE_COMMAND, () => runConfirmedOperation("baseline")),
    );
    void startLanguageServer(context);
}

export function deactivate(): void {
    void languageClient?.stop();
    languageClient = undefined;
    const run = runCoordinator.cancel();
    if (run) {
        clearTimeout(run.timeout);
        void fs.rm(run.outputDirectory, { recursive: true, force: true });
    }
    statusBarItem?.dispose();
    outputChannel?.dispose();
    diagnostics?.dispose();
    statusBarItem = undefined;
    outputChannel = undefined;
    diagnostics = undefined;
}

async function startLanguageServer(context: vscode.ExtensionContext): Promise<void> {
    const serverPath = context.asAbsolutePath(path.join("server", "DataGuard.LanguageServer.dll"));
    try {
        await verifyLanguageServerArtifact(serverPath);
    } catch {
        getOutputChannel().appendLine("[DataGuard] Local language server artifact is missing or failed integrity verification; LSP is disabled.");
        return;
    }

    const serverOptions: ServerOptions = { run: { command: "dotnet", args: [serverPath] }, debug: { command: "dotnet", args: [serverPath] } };
    const clientOptions: LanguageClientOptions = { documentSelector: [{ scheme: "file", language: "csharp" }] };
    languageClient = new LanguageClient("dataguardLanguageServer", "DataGuard Language Server", serverOptions, clientOptions);
    await languageClient.start();
    context.subscriptions.push({ dispose: () => { void languageClient?.stop(); } });
}

async function verifyLanguageServerArtifact(serverPath: string): Promise<void> {
    const manifestPath = path.join(path.dirname(serverPath), "manifest.json");
    const [artifact, manifestText] = await Promise.all([fs.readFile(serverPath), fs.readFile(manifestPath, "utf8")]);
    const manifest = JSON.parse(manifestText) as { schemaVersion?: number; artifact?: string; sha256?: string };
    const actual = createHash("sha256").update(artifact).digest("hex");
    if (manifest.schemaVersion !== 1 || manifest.artifact !== path.basename(serverPath) || manifest.sha256 !== actual) {
        throw new Error("Invalid DataGuard language server artifact manifest.");
    }
}

async function runValidation(): Promise<void> {
    await runCliCommand("validate", "timeoutSeconds");
}

async function runAssessment(): Promise<void> {
    await runCliCommand("assess", "assessmentTimeoutSeconds");
}

async function runConfirmedOperation(command: "snapshot" | "baseline"): Promise<void> {
    const action = command === "snapshot" ? "refresh the schema snapshot" : "create a baseline";
    const choice = await vscode.window.showWarningMessage(`DataGuard will ${action} using the configured provider and credentials. Continue?`, { modal: true }, "Continue");
    if (choice !== "Continue") {
        return;
    }

    await runCliCommand(command, "timeoutSeconds");
}

async function runCliCommand(command: "validate" | "assess" | "snapshot" | "baseline", timeoutSetting: "timeoutSeconds" | "assessmentTimeoutSeconds"): Promise<void> {
    if (!vscode.workspace.isTrusted) {
        void vscode.window.showWarningMessage("DataGuard does not run CLI commands in an untrusted workspace. Trust this workspace first.");
        return;
    }

    const workspaceFolder = await selectWorkspaceFolder();
    if (!workspaceFolder) {
        void vscode.window.showWarningMessage("DataGuard: open a workspace folder first.");
        return;
    }

    if (runCoordinator.current) {
        const active = runCoordinator.cancel();
        if (active) {
            clearTimeout(active.timeout);
        }
        void vscode.window.showInformationMessage("DataGuard replaced the previous run with this command.");
    }

    const configuration = vscode.workspace.getConfiguration("dataguard", workspaceFolder.uri);
    if (!configuration.get<boolean>("enabled", true)) {
        void vscode.window.showWarningMessage("DataGuard validation is disabled (dataguard.enabled).");
        return;
    }

    let configPath: string | undefined;
    if (command === "validate" || command === "snapshot" || command === "baseline") {
        try {
            configPath = resolveWorkspaceConfigPath(
                workspaceFolder.uri.fsPath,
                configuration.get<string>("configPath", ".dataguard.yml"),
            );
        } catch (error) {
            void vscode.window.showErrorMessage(`DataGuard: ${error instanceof Error ? error.message : String(error)}`);
            return;
        }
    }

    const cliPath = vscode.workspace.getConfiguration("dataguard").get<string>("cliPath", "dataguard");
    const timeoutSeconds = Math.min(900, Math.max(5, configuration.get<number>(timeoutSetting, 60)));
    let provider: string;
    try {
        provider = normalizeProvider(configuration.get<string>("provider", "sqlserver"));
    } catch (error) {
        void vscode.window.showErrorMessage(`DataGuard: ${error instanceof Error ? error.message : String(error)}`);
        return;
    }
    const channel = getOutputChannel();
    channel.clear();
    channel.show(true);
    diagnostics?.clear();

    const outputDirectory = await fs.mkdtemp(path.join(os.tmpdir(), "dataguard-"));
    const expectsSarif = command === "validate" || command === "assess";
    const outputPath = expectsSarif ? path.join(outputDirectory, "validation.sarif") : undefined;
    const args = buildCliArguments(command, workspaceFolder.uri.fsPath, provider, configPath, outputPath);
    channel.appendLine(`[DataGuard] ${command === "validate" ? "Validation" : "Local assessment"} started. Detailed CLI output is not displayed to prevent credential disclosure.`);

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
        cancel: () => terminateProcessTree(child),
    };
    runCoordinator.replace(run);
    setStatus("running");

    try {
        const result = await waitForExit(child, channel);
        const exitCode = result.code;
        if (result.output.length > 0) {
            channel.appendLine(`[DataGuard] ${redactAndBoundSensitiveText(result.output, MAX_CLI_OUTPUT)}`);
        }
        if (run.timedOut) {
            channel.appendLine(`\n[DataGuard] ${command} timed out after ${timeoutSeconds} seconds.`);
            setStatus("error");
            void vscode.window.showErrorMessage(`DataGuard ${command} timed out after ${timeoutSeconds} seconds.`);
            return;
        }
        if (run.cancelled) {
            channel.appendLine(`\n[DataGuard] ${command} cancelled.`);
            return;
        }

        if (expectsSarif && outputPath) {
            const diagnosticCount = await loadDiagnostics(outputPath, workspaceFolder, diagnostics, channel);
            channel.appendLine(`[DataGuard] SARIF summary: ${diagnosticCount} finding(s) loaded into Problems.`);
        }
        channel.appendLine(`\n[DataGuard] exited with code ${exitCode ?? "unknown"}`);
        if (exitCode === 0) {
            setStatus("idle");
        } else if (exitCode === 1) {
            setStatus("warning");
            void vscode.window.showWarningMessage(`DataGuard ${command} found findings. See Problems or the DataGuard output channel.`);
        } else {
            setStatus("error");
            void vscode.window.showErrorMessage(`DataGuard could not complete ${command}. See the DataGuard output channel.`);
        }
    } finally {
        clearTimeout(run.timeout);
        runCoordinator.clear(run);
        await fs.rm(outputDirectory, { recursive: true, force: true });
        if (!runCoordinator.current && !run.timedOut && !run.cancelled) {
            setStatus("idle");
        }
    }
}

async function cancelValidation(): Promise<void> {
    if (!runCoordinator.current) {
        void vscode.window.showInformationMessage("No DataGuard command is running.");
        return;
    }

    const run = runCoordinator.cancel();
    if (run) {
        clearTimeout(run.timeout);
    }
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

async function waitForExit(child: ChildProcess, channel: vscode.OutputChannel): Promise<ChildExit> {
    let output = "";
    const append = (chunk: Buffer | string): void => {
        if (output.length >= MAX_CLI_OUTPUT) {
            return;
        }
        output += chunk.toString().slice(0, MAX_CLI_OUTPUT - output.length);
    };
    child.stdout?.on("data", append);
    child.stderr?.on("data", append);

    try {
        const [code] = await once(child, "close");
        return { code: code as number | null, output };
    } catch (error) {
        showStartError(error, channel);
        return { code: null, output };
    }
}

async function loadDiagnostics(
    outputPath: string,
    workspaceFolder: vscode.WorkspaceFolder,
    collection: vscode.DiagnosticCollection | undefined,
    channel: vscode.OutputChannel,
): Promise<number> {
    if (!collection) {
        return 0;
    }
    let sarif: SarifLog;
    try {
        sarif = JSON.parse(await fs.readFile(outputPath, "utf8")) as SarifLog;
    } catch (error) {
        channel.appendLine(`\n[DataGuard] SARIF output was unavailable or invalid: ${redactSensitiveText(String(error))}`);
        return 0;
    }

    collection.clear();
    const byDocument = new Map<string, { uri: vscode.Uri; diagnostics: vscode.Diagnostic[] }>();
    const results = sarif.runs?.flatMap((run) => run.results ?? []) ?? [];
    for (const result of results) {
        const location = result.locations?.[0]?.physicalLocation;
        const sourceUri = location?.artifactLocation?.uri;
        if (!sourceUri) {
            continue;
        }

        let filePath: string;
        try {
            filePath = resolveWorkspaceSarifPath(workspaceFolder.uri.fsPath, sourceUri);
        } catch (error) {
            channel.appendLine(`[DataGuard] Ignored external SARIF location: ${redactSensitiveText(error instanceof Error ? error.message : String(error))}`);
            continue;
        }
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

    let loadedCount = 0;
    for (const { uri, diagnostics: documentDiagnostics } of byDocument.values()) {
        collection.set(uri, documentDiagnostics);
        loadedCount += documentDiagnostics.length;
    }
    return loadedCount;
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
