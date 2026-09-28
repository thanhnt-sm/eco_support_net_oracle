/**
 * DataGuard hover provider — Step 5 revision per red-team plan.
 *
 * Sole authority for rich Markdown hover content over DataGuard diagnostics.
 * - md.isTrusted = false, md.supportHtml = false (no injection risk).
 * - Uses appendText/appendCodeblock for dynamic content.
 * - Combines overlapping diagnostics for the same range into one hover (---separator).
 * - Reads migration hints from finding.properties.migration (F7 pipeline fix).
 * - Does NOT claim quick-fix availability for DG010 (no quick-fix exists — F5 fix).
 */
import * as vscode from "vscode";
import { FindingItem } from "./redaction";

export class DataGuardHoverProvider implements vscode.HoverProvider {
    private _findings: FindingItem[] = [];

    setFindings(findings: FindingItem[]): void {
        this._findings = findings;
    }

    clear(): void {
        this._findings = [];
    }

    provideHover(
        document: vscode.TextDocument,
        position: vscode.Position,
        _token: vscode.CancellationToken
    ): vscode.ProviderResult<vscode.Hover> {
        const docPath = document.uri.fsPath;

        // Find all findings that overlap the hovered position
        const overlapping = this._findings.filter((f) => {
            if (f.filePath !== docPath && !f.fullUri.endsWith(docPath.replace(/\\/g, "/"))) {
                return false;
            }
            const startLine = Math.max(0, f.startLine - 1);
            const endLine = Math.max(startLine, f.endLine - 1);
            const startCol = Math.max(0, f.startColumn - 1);
            const endCol = Math.max(startCol, f.endColumn - 1);
            const range = new vscode.Range(startLine, startCol, endLine, endCol);
            return range.contains(position);
        });

        if (overlapping.length === 0) {
            return undefined;
        }

        const md = new vscode.MarkdownString("", true);
        md.isTrusted = false;
        md.supportHtml = false;

        for (let i = 0; i < overlapping.length; i++) {
            if (i > 0) {
                md.appendMarkdown("\n\n---\n\n");
            }
            const finding = overlapping[i];
            this._appendFindingHover(md, finding);
        }

        return new vscode.Hover(md);
    }

    private _appendFindingHover(md: vscode.MarkdownString, finding: FindingItem): void {
        const severityIcon = finding.severity === "error" ? "$(error)" : "$(warning)";

        // Title: severity icon + rule ID (static structure — safe for appendMarkdown)
        md.appendMarkdown(`**${severityIcon} DataGuard ${finding.ruleId}**\n\n`);

        // Message — use appendText for dynamic content (XSS-safe)
        md.appendText(finding.message);
        md.appendMarkdown("\n\n");

        // Migration hint from properties bag (populated by DG010 after F7 fix)
        const migration = finding.properties?.["migration"];
        if (typeof migration === "string" && migration.length > 0) {
            md.appendMarkdown("**Migration guidance:**\n\n");
            md.appendText(migration);
            md.appendMarkdown("\n\n");
        }

        // Keyword context (DG010)
        const keyword = finding.properties?.["keyword"];
        if (typeof keyword === "string") {
            md.appendMarkdown("**Oracle keyword:**\n\n");
            md.appendCodeblock(keyword, "sql");
        }

        // Operator context (DG010)
        const operator = finding.properties?.["operator"];
        if (typeof operator === "string") {
            md.appendMarkdown("**Oracle operator:**\n\n");
            md.appendCodeblock(operator, "sql");
        }

        // Quick-fix availability (not available for DG010 — explicit check)
        if (finding.quickFixAvailable && finding.quickFixTitle && finding.ruleId !== "DG010") {
            md.appendMarkdown(`\n\n*Quick Fix available: ${finding.quickFixTitle}*`);
        }
    }
}
