import * as crypto from "crypto";
import * as vscode from "vscode";
import { renderDashboardHtml } from "./dashboard-view";
import { FindingItem } from "./redaction";

export { buildWebviewCsp, DashboardWebviewOptions, renderDashboardHtml } from "./dashboard-view";
export class DataGuardDashboardPanel {
    public static currentPanel: DataGuardDashboardPanel | undefined;
    private static readonly viewType = "dataguard.dashboard";
    private readonly panel: vscode.WebviewPanel;
    private disposables: vscode.Disposable[] = [];
    private currentFindings: FindingItem[] = [];

    public static createOrShow(extensionUri: vscode.Uri, findings: FindingItem[]): DataGuardDashboardPanel {
        const column = vscode.window.activeTextEditor
            ? vscode.window.activeTextEditor.viewColumn
            : undefined;

        if (DataGuardDashboardPanel.currentPanel) {
            DataGuardDashboardPanel.currentPanel.panel.reveal(column);
            DataGuardDashboardPanel.currentPanel.update(findings);
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

        DataGuardDashboardPanel.currentPanel = new DataGuardDashboardPanel(panel, findings);
        return DataGuardDashboardPanel.currentPanel;
    }

    private constructor(panel: vscode.WebviewPanel, initialFindings: FindingItem[]) {
        this.panel = panel;
        this.currentFindings = initialFindings;

        this.updateWebview();

        this.panel.onDidDispose(() => this.dispose(), null, this.disposables);

        this.panel.webview.onDidReceiveMessage(
            async (message) => {
                switch (message.command) {
                    case "jumpToFinding": {
                        const finding = this.currentFindings.find((f) => f.id === message.findingId);
                        if (finding) {
                            await this.jumpToFinding(finding);
                        }
                        break;
                    }
                    case "applyQuickFix": {
                        await vscode.commands.executeCommand("dataguard.applyQuickFix", message.findingId);
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
            },
            null,
            this.disposables
        );
    }

    public update(findings: FindingItem[]): void {
        this.currentFindings = findings;
        this.panel.webview.postMessage({
            type: "setFindings",
            findings: this.currentFindings
        });
    }

    public clear(): void {
        this.currentFindings = [];
        this.panel.webview.postMessage({
            type: "clearFindings"
        });
    }

    private updateWebview(): void {
        const nonce = crypto.randomBytes(16).toString("base64");
        this.panel.webview.html = renderDashboardHtml(this.currentFindings, nonce);
    }

    private async jumpToFinding(finding: FindingItem): Promise<void> {
        try {
            const uri = vscode.Uri.file(finding.filePath);
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
