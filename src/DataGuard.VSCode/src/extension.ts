import * as vscode from "vscode";
import * as path from "path";
import { spawn } from "child_process";

const COMMAND_ID = "dataguard.runValidation";
const OUTPUT_CHANNEL_NAME = "DataGuard";

let statusBarItem: vscode.StatusBarItem | undefined;
let outputChannel: vscode.OutputChannel | undefined;

export function activate(context: vscode.ExtensionContext): void {
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    statusBarItem.name = "DataGuard";
    statusBarItem.text = "$(shield) DataGuard";
    statusBarItem.tooltip = "Run DataGuard contract validation";
    statusBarItem.command = COMMAND_ID;
    statusBarItem.show();
    context.subscriptions.push(statusBarItem);

    context.subscriptions.push(vscode.commands.registerCommand(COMMAND_ID, runValidation));
}

export function deactivate(): void {
    statusBarItem?.dispose();
    statusBarItem = undefined;
    outputChannel?.dispose();
    outputChannel = undefined;
}

function getOutputChannel(): vscode.OutputChannel {
    if (!outputChannel) {
        outputChannel = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
    }
    return outputChannel;
}

async function runValidation(): Promise<void> {
    const config = vscode.workspace.getConfiguration("dataguard");
    if (!config.get<boolean>("enabled", true)) {
        void vscode.window.showWarningMessage("DataGuard validation is disabled (dataguard.enabled).");
        return;
    }

    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (!workspaceFolder) {
        void vscode.window.showWarningMessage("DataGuard: open a workspace folder first.");
        return;
    }

    const configPathSetting = config.get<string>("configPath", ".dataguard.yml");
    const configPath = path.isAbsolute(configPathSetting)
        ? configPathSetting
        : path.join(workspaceFolder.uri.fsPath, configPathSetting);

    const channel = getOutputChannel();
    channel.clear();
    channel.appendLine(`$ dataguard validate --config "${configPath}"`);
    channel.show(true);

    if (statusBarItem) {
        statusBarItem.text = "$(sync~spin) DataGuard";
    }

    const child = spawn("dataguard", ["validate", "--config", configPath], {
        cwd: workspaceFolder.uri.fsPath,
        shell: false,
    });

    child.stdout?.setEncoding("utf8");
    child.stderr?.setEncoding("utf8");
    child.stdout?.on("data", (chunk: string) => channel.append(chunk));
    child.stderr?.on("data", (chunk: string) => channel.append(chunk));

    child.on("error", (error: NodeJS.ErrnoException) => {
        if (statusBarItem) {
            statusBarItem.text = "$(error) DataGuard";
        }
        const message = error.code === "ENOENT"
            ? "DataGuard CLI ('dataguard') was not found on PATH. Install it with 'dotnet tool install -g DataGuard.Cli'."
            : `Failed to start dataguard: ${error.message}`;
        channel.appendLine(`\n${message}`);
        void vscode.window.showErrorMessage(message);
    });

    child.on("close", (code: number | null) => {
        if (statusBarItem) {
            statusBarItem.text = code === 0 ? "$(shield) DataGuard" : "$(warning) DataGuard";
        }
        channel.appendLine(`\n[DataGuard] exited with code ${code ?? "unknown"}`);
    });
}
