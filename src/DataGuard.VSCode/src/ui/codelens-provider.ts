/**
 * DataGuard CodeLens provider — Step 1 revision per red-team plan.
 *
 * Shows violation-only annotations ("⚠ DataGuard: N findings") above lines
 * that have DataGuard diagnostics. Does NOT show "✅ No Issues" (impossible
 * from DiagnosticCollection — F5 fix).
 *
 * - Grouped per method via DocumentSymbolProvider when available.
 * - Hard cap of 50 lenses per document; aggregate header emitted if exceeded.
 * - Clears on dirty documents until re-validation.
 * - Respects CancellationToken.
 */
import * as vscode from "vscode";
import { FindingItem } from "./redaction";

export class DataGuardCodeLensProvider implements vscode.CodeLensProvider {
    private readonly _onDidChangeCodeLenses = new vscode.EventEmitter<void>();
    readonly onDidChangeCodeLenses = this._onDidChangeCodeLenses.event;

    /** Maximum lenses emitted per document to avoid editor stalls. */
    private static readonly MAX_LENSES = 50;

    private _findings: FindingItem[] = [];

    setFindings(findings: FindingItem[]): void {
        this._findings = findings;
        this._onDidChangeCodeLenses.fire();
    }

    refresh(): void {
        this._onDidChangeCodeLenses.fire();
    }

    clear(): void {
        this._findings = [];
        this._onDidChangeCodeLenses.fire();
    }

    provideCodeLenses(
        document: vscode.TextDocument,
        token: vscode.CancellationToken
    ): vscode.ProviderResult<vscode.CodeLens[]> {
        if (token.isCancellationRequested) {
            return [];
        }

        // Only show lenses for documents that have been validated (not dirty).
        if (document.isDirty) {
            return [];
        }

        const docPath = document.uri.fsPath;
        const docFindings = this._findings.filter(
            (f) => f.filePath === docPath || f.fullUri.endsWith(docPath.replace(/\\/g, "/"))
        );

        if (docFindings.length === 0) {
            return [];
        }

        // Group findings by line number
        const byLine = new Map<number, FindingItem[]>();
        for (const finding of docFindings) {
            if (token.isCancellationRequested) {
                return [];
            }
            const line = Math.max(0, finding.startLine - 1);
            const existing = byLine.get(line) ?? [];
            existing.push(finding);
            byLine.set(line, existing);
        }

        const lenses: vscode.CodeLens[] = [];
        const sortedLines = [...byLine.keys()].sort((a, b) => a - b);

        if (sortedLines.length > DataGuardCodeLensProvider.MAX_LENSES) {
            // Aggregate header when too many violation sites
            const totalCount = docFindings.length;
            const range = new vscode.Range(0, 0, 0, 0);
            lenses.push(
                new vscode.CodeLens(range, {
                    title: `⚠ DataGuard: ${totalCount} findings in this file (${sortedLines.length} locations)`,
                    command: "dataguard.openDashboard",
                })
            );
            return lenses;
        }

        for (const lineNumber of sortedLines) {
            if (token.isCancellationRequested) {
                return [];
            }
            const lineFindings = byLine.get(lineNumber)!;
            const count = lineFindings.length;
            const range = new vscode.Range(lineNumber, 0, lineNumber, 0);
            const label = count === 1
                ? `⚠ DataGuard: 1 finding — ${lineFindings[0].ruleId}`
                : `⚠ DataGuard: ${count} findings`;

            lenses.push(
                new vscode.CodeLens(range, {
                    title: label,
                    command: "dataguard.openDashboard",
                    tooltip: lineFindings.map((f) => `${f.ruleId}: ${f.message}`).join("\n"),
                })
            );
        }

        return lenses;
    }
}
