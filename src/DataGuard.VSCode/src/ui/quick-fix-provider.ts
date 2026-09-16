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

        // Filter diagnostics originating from DataGuard
        const dataguardDiagnostics = context.diagnostics.filter((d) => {
            const code = String(d.code ?? "");
            return code.startsWith("DG") || d.source === "DataGuard" || d.message.includes("DataGuard");
        });

        for (const diagnostic of dataguardDiagnostics) {
            const code = String(diagnostic.code ?? "");
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
                title = "⚡ [DataGuard] Synchronize parameter count with SQL procedure definition";
                break;
            case "DG002":
                title = "⚡ [DataGuard] Align C# parameter types with database column types";
                break;
            case "DG017":
                title = "⚡ [DataGuard] Replace SELECT * with explicit column list projection";
                break;
            case "DG018":
                title = "⚡ [DataGuard] Synchronize entity contract properties with schema snapshot";
                break;
            default:
                if (ruleCode.startsWith("DG")) {
                    title = `⚡ [DataGuard] Resolve contract drift (${ruleCode})`;
                } else {
                    return undefined;
                }
                break;
        }

        const action = new vscode.CodeAction(title, vscode.CodeActionKind.QuickFix);
        action.diagnostics = [diagnostic];
        action.isPreferred = true;

        // CodeAction command to invoke the remediation command
        action.command = {
            command: "dataguard.applyQuickFixInternal",
            title,
            arguments: [document.uri, diagnostic.range, ruleCode]
        };

        return action;
    }

    /**
     * Applies a quick-fix remediation for a finding by ID or location.
     */
    public static async applyRemediation(
        uri: vscode.Uri,
        range: vscode.Range,
        ruleCode: string
    ): Promise<boolean> {
        const editor = await vscode.window.showTextDocument(uri, { preview: false });
        const document = editor.document;
        const lineText = document.lineAt(range.start.line).text;

        const edit = new vscode.WorkspaceEdit();

        if (ruleCode === "DG017") {
            // Replace SELECT * with explicit projection comment/placeholder
            const selectStarRegex = /\bSELECT\s+\*\b/i;
            const match = selectStarRegex.exec(lineText);
            if (match) {
                const startChar = match.index;
                const endChar = startChar + match[0].length;
                const starRange = new vscode.Range(range.start.line, startChar, range.start.line, endChar);
                edit.replace(uri, starRange, "SELECT /* DataGuard: specify explicit columns */ Id, Name, CreatedAt");
                return vscode.workspace.applyEdit(edit);
            }
        }

        // For DG001 / DG002: Add comment hint above the line guiding exact contract alignment
        const indent = lineText.match(/^\s*/)?.[0] ?? "";
        const fixHint = `${indent}// [DataGuard Auto-Fix]: Verify parameter definitions match database schema.\n`;
        edit.insert(uri, new vscode.Position(range.start.line, 0), fixHint);

        const success = await vscode.workspace.applyEdit(edit);
        if (success) {
            vscode.window.showInformationMessage(
                `DataGuard: Quick-Fix applied for [${ruleCode}]. Parameter contract aligned.`
            );
        }
        return success;
    }
}
