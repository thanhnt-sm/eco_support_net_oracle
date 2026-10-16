import * as vscode from "vscode";
import { FindingItem } from "./redaction";

export class DataGuardQuickFixProvider implements vscode.CodeActionProvider {
    public static readonly providedCodeActionKinds = [
        vscode.CodeActionKind.QuickFix
    ];

    private findingsMap = new Map<string, FindingItem>();

    public registerFindings(findings: FindingItem[]): void {
        this.findingsMap.clear();
        for (const f of findings) {
            this.findingsMap.set(f.id, f);
        }
    }

    public provideCodeActions(
        document: vscode.TextDocument,
        range: vscode.Range | vscode.Selection,
        context: vscode.CodeActionContext,
        _token: vscode.CancellationToken
    ): vscode.CodeAction[] {
        const actions: vscode.CodeAction[] = [];
        if (_token.isCancellationRequested) return actions;

        // Filter diagnostics originating from DataGuard
        const dataguardDiagnostics = context.diagnostics.filter((d) => {
            const code = String(typeof d.code === "object" ? d.code.value : d.code ?? "");
            return code.startsWith("DG") || d.source === "DataGuard" || d.message.includes("DataGuard");
        });

        for (const diagnostic of dataguardDiagnostics) {
            const code = String(typeof diagnostic.code === "object" ? diagnostic.code.value : diagnostic.code ?? "");
            const fix = this.createCodeAction(document, diagnostic, code);
            if (fix) {
                actions.push(fix);
            }
        }

        return actions;
    }

    private createCodeAction(
        document: vscode.TextDocument,
        diagnostic: vscode.Diagnostic,
        ruleCode: string
    ): vscode.CodeAction | undefined {
        let title: string;

        switch (ruleCode) {
            case "DG001":
                title = "⚡ [DataGuard] Review parameter count against SQL procedure definition";
                break;
            case "DG002":
                title = "⚡ [DataGuard] Review C# parameter types against database column types";
                break;
            case "DG017":
                title = "⚡ [DataGuard] Review SELECT * and choose schema-verified columns";
                break;
            case "DG018":
                title = "⚡ [DataGuard] Review entity contract properties against schema snapshot";
                break;
            default:
                if (ruleCode.startsWith("DG")) {
                    title = `⚡ [DataGuard] Review contract drift (${ruleCode})`;
                } else {
                    return undefined;
                }
                break;
        }

        const action = new vscode.CodeAction(title, vscode.CodeActionKind.QuickFix);
        action.diagnostics = [diagnostic];
        action.isPreferred = false;

        // CodeAction command to invoke the remediation command
        action.command = {
            command: "dataguard.applyQuickFixInternal",
            title,
            arguments: [document.uri, diagnostic.range, ruleCode]
        };

        return action;
    }

    /**
     * Explains manual remediation; returns false because no automatic edit was applied.
     */
    public static async applyRemediation(
        uri: vscode.Uri,
        range: vscode.Range,
        ruleCode: string
    ): Promise<boolean> {
        // No verified schema-derived edit is available. Never insert guessed columns or
        // claim a comment has repaired a contract. Keep this command informational.
        if (!ruleCode.startsWith("DG")) return false;
        await vscode.window.showInformationMessage(
            `DataGuard: Review [${ruleCode}] against the schema or snapshot and edit manually. No automatic edit is available.`
        );
        return false;
    }
}
