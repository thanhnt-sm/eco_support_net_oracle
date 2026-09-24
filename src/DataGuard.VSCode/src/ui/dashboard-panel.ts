import * as path from "path";
import * as fs from "fs";
import * as crypto from "crypto";
import * as vscode from "vscode";
import { isPathInWorkspaceFolder } from "../security";
import { renderDashboardHtml } from "./dashboard-view";
import { FindingItem } from "./redaction";
import { ScanReport } from "./sql-queries-tree-provider";

export { buildWebviewCsp, DashboardWebviewOptions, renderDashboardHtml } from "./dashboard-view";
export class DataGuardDashboardPanel {
    public static currentPanel: DataGuardDashboardPanel | undefined;
    private static readonly viewType = "dataguard.dashboard";
    private readonly panel: vscode.WebviewPanel;
    private disposables: vscode.Disposable[] = [];
    private currentFindings: FindingItem[] = [];
    private currentScanReport: ScanReport | null = null;
    private _isDisposed = false;
    public static createOrShow(extensionUri: vscode.Uri, findings: FindingItem[], scanReport?: ScanReport | null): DataGuardDashboardPanel {
        const column = vscode.window.activeTextEditor
            ? vscode.window.activeTextEditor.viewColumn
            : undefined;

        if (DataGuardDashboardPanel.currentPanel) {
            DataGuardDashboardPanel.currentPanel.panel.reveal(column);
            DataGuardDashboardPanel.currentPanel.update(findings);
            if (scanReport !== undefined) {
                DataGuardDashboardPanel.currentPanel.updateScanReport(scanReport);
            }
            return DataGuardDashboardPanel.currentPanel;
        }
        const panel = vscode.window.createWebviewPanel(
            DataGuardDashboardPanel.viewType,
            "DataGuard Contract Drift Dashboard",
            column || vscode.ViewColumn.One,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [vscode.Uri.joinPath(extensionUri, "media")]
            }
        );

        DataGuardDashboardPanel.currentPanel = new DataGuardDashboardPanel(panel, findings, scanReport ?? null);
        return DataGuardDashboardPanel.currentPanel;
    }

    private constructor(panel: vscode.WebviewPanel, initialFindings: FindingItem[], initialReport: ScanReport | null = null) {
        this.panel = panel;
        this.currentFindings = initialFindings;
        this.currentScanReport = initialReport;
        this.updateWebview();

        this.panel.onDidDispose(() => this.dispose(), null, this.disposables);

        this.panel.webview.onDidReceiveMessage(
            async (message) => {
                if (!message || typeof message !== "object" || typeof message.command !== "string") {
                    return;
                }
                try {
                    switch (message.command) {
                        case "jumpToFinding": {
                            if (typeof message.findingId === "string") {
                                const finding = this.currentFindings.find((f) => f.id === message.findingId);
                                if (finding) {
                                    await this.jumpToFinding(finding);
                                }
                            }
                            break;
                        }
                        case "jumpToLocation": {
                            if (typeof message.file === "string" && typeof message.line === "number") {
                                try {
                                    const resolvedPath = this.resolveWorkspaceFilePath(message.file);
                                    if (!this.isPathInWorkspace(resolvedPath) || !fs.existsSync(resolvedPath) || !fs.statSync(resolvedPath).isFile()) {
                                        void vscode.window.showWarningMessage(`DataGuard: Target file is outside active workspace folders or is not a valid file.`);
                                        break;
                                    }
                                    const uri = vscode.Uri.file(resolvedPath);
                                    const doc = await vscode.workspace.openTextDocument(uri);
                                    const editor = await vscode.window.showTextDocument(doc, { preview: true });
                                    const line = Math.max(0, message.line - 1);
                                    const range = new vscode.Range(line, 0, line, 0);
                                    editor.selection = new vscode.Selection(range.start, range.end);
                                    editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
                                } catch {
                                    void vscode.window.showWarningMessage(`DataGuard: Could not open file ${message.file}`);
                                }
                            }
                            break;
                        }
                        case "scanProject": {
                            await vscode.commands.executeCommand("dataguard.scanProject");
                            break;
                        }
                        case "applyQuickFix": {
                            if (typeof message.findingId === "string" && this.currentFindings.some(f => f.id === message.findingId)) {
                                await vscode.commands.executeCommand("dataguard.applyQuickFix", message.findingId);
                            }
                            break;
                        }
                        case "refresh": {
                            await vscode.commands.executeCommand("dataguard.runValidation");
                            break;
                        }
                        case "clear": {
                            await vscode.commands.executeCommand("dataguard.clearFindings");
                            break;
                        }
                    }
                } catch {
                    // Suppress unhandled errors from malformed webview payloads
                }
            },
            null,
            this.disposables
        );
    }

    public update(findings: FindingItem[]): void {
        if (this._isDisposed) {
            return;
        }
        this.currentFindings = findings;
        this.panel.webview.postMessage({
            type: "setFindings",
            findings: this.currentFindings
        });
    }

    public updateScanReport(report: ScanReport | null): void {
        if (this._isDisposed) {
            return;
        }
        this.currentScanReport = report;
        this.panel.webview.postMessage({
            type: "setScanReport",
            report: this.currentScanReport
        });
    }

    public clear(): void {
        if (this._isDisposed) {
            return;
        }
        this.currentFindings = [];
        this.panel.webview.postMessage({
            type: "clearFindings"
        });
    }

    private updateWebview(): void {
        if (this._isDisposed) {
            return;
        }
        const nonce = crypto.randomBytes(16).toString("base64");
        this.panel.webview.html = renderDashboardHtml(this.currentFindings, nonce, this.currentScanReport);
    }
    private resolveWorkspaceFilePath(filePath: string): string {
        const isWindowsAbs = /^[a-zA-Z]:[\\/]/.test(filePath);
        if (path.isAbsolute(filePath) || isWindowsAbs) {
            return path.resolve(filePath);
        }
        try {
            const folders = vscode.workspace.workspaceFolders ?? [];
            for (const folder of folders) {
                const candidate = path.resolve(folder.uri.fsPath, filePath);
                if (fs.existsSync(candidate)) {
                    return candidate;
                }
            }
            if (folders.length > 0) {
                return path.resolve(folders[0].uri.fsPath, filePath);
            }
        } catch {
            // Fall back safely if fs probing throws on invalid paths or device namespaces
        }
        return path.resolve(filePath);
    }

    private isPathInWorkspace(targetPath: string): boolean {
        const folderPaths = (vscode.workspace.workspaceFolders ?? []).map((f) => f.uri.fsPath);
        return isPathInWorkspaceFolder(targetPath, folderPaths);
    }
    private async jumpToFinding(finding: FindingItem): Promise<void> {
        try {
            const resolvedPath = this.resolveWorkspaceFilePath(finding.filePath);
            if (!this.isPathInWorkspace(resolvedPath)) {
                void vscode.window.showWarningMessage(`DataGuard: Target file is outside active workspace folders.`);
                return;
            }
            const uri = vscode.Uri.file(resolvedPath);
            const doc = await vscode.workspace.openTextDocument(uri);
            const editor = await vscode.window.showTextDocument(doc, { preview: true });
            const range = new vscode.Range(
                Math.max(0, finding.startLine - 1),
                Math.max(0, finding.startColumn - 1),
                Math.max(0, finding.endLine - 1),
                Math.max(0, finding.endColumn - 1)
            );
            editor.selection = new vscode.Selection(range.start, range.end);
            editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
        } catch {
            vscode.window.showWarningMessage(`DataGuard: Could not open file ${finding.filePath}`);
        }
    }

    public dispose(): void {
        if (this._isDisposed) {
            return;
        }
        this._isDisposed = true;
        DataGuardDashboardPanel.currentPanel = undefined;
        this.panel.dispose();
        while (this.disposables.length) {
            const d = this.disposables.pop();
            if (d) {
                d.dispose();
            }
        }
    }
}
