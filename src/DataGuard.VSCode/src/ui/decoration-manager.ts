/**
 * DataGuard decoration manager — Step 2 revision per red-team plan.
 *
 * Applies gutter icons + background highlights for DataGuard findings.
 * - NO hoverMessage on DecorationOptions (prevents triple hover stacking — F6 fix).
 * - Uses semantic theme tokens (editorError.foreground / editorWarning.foreground).
 * - Applies to ALL visibleTextEditors, not just activeTextEditor.
 * - Clears on clearFindingsAndDiagnostics() and on CLI start/cancel/error.
 * - Disposes decoration types in deactivate().
 */
import * as vscode from "vscode";
import { FindingItem } from "./redaction";

export class DataGuardDecorationManager {
    private readonly _errorDecorationType: vscode.TextEditorDecorationType;
    private readonly _warningDecorationType: vscode.TextEditorDecorationType;
    private _disposed = false;

    constructor() {
        this._errorDecorationType = vscode.window.createTextEditorDecorationType({
            gutterIconPath: new vscode.ThemeIcon("error").id as unknown as vscode.Uri,
            gutterIconSize: "contain",
            backgroundColor: new vscode.ThemeColor("diffEditor.removedTextBackground"),
            overviewRulerColor: new vscode.ThemeColor("editorError.foreground"),
            overviewRulerLane: vscode.OverviewRulerLane.Right,
            // No hoverMessage — sole authority for hover is hover-provider.ts (F6 fix)
        });

        this._warningDecorationType = vscode.window.createTextEditorDecorationType({
            gutterIconPath: new vscode.ThemeIcon("warning").id as unknown as vscode.Uri,
            gutterIconSize: "contain",
            backgroundColor: new vscode.ThemeColor("diffEditor.modifiedTextBackground"),
            overviewRulerColor: new vscode.ThemeColor("editorWarning.foreground"),
            overviewRulerLane: vscode.OverviewRulerLane.Right,
            // No hoverMessage — sole authority for hover is hover-provider.ts (F6 fix)
        });
    }

    /**
     * Apply decorations to all currently visible editors that have DataGuard findings.
     */
    applyFindings(findings: FindingItem[]): void {
        if (this._disposed) return;

        const byPath = new Map<string, { errors: vscode.Range[]; warnings: vscode.Range[] }>();

        for (const finding of findings) {
            const key = finding.filePath;
            if (!byPath.has(key)) {
                byPath.set(key, { errors: [], warnings: [] });
            }
            const entry = byPath.get(key)!;
            // Convert 1-based SARIF lines to 0-based VS Code lines
            const startLine = Math.max(0, finding.startLine - 1);
            const startCol = Math.max(0, finding.startColumn - 1);
            const endLine = Math.max(startLine, finding.endLine - 1);
            const endCol = Math.max(startCol, finding.endColumn - 1);
            const range = new vscode.Range(startLine, startCol, endLine, endCol);

            if (finding.severity === "error") {
                entry.errors.push(range);
            } else {
                entry.warnings.push(range);
            }
        }

        // Apply to all visible editors
        for (const editor of vscode.window.visibleTextEditors) {
            const editorPath = editor.document.uri.fsPath;
            const entry = byPath.get(editorPath) ?? { errors: [], warnings: [] };
            editor.setDecorations(this._errorDecorationType, entry.errors);
            editor.setDecorations(this._warningDecorationType, entry.warnings);
        }
    }

    /**
     * Clear all decorations from all visible editors.
     * Called from clearFindingsAndDiagnostics() and on CLI start/cancel/error (F9 fix).
     */
    clear(): void {
        if (this._disposed) return;
        for (const editor of vscode.window.visibleTextEditors) {
            editor.setDecorations(this._errorDecorationType, []);
            editor.setDecorations(this._warningDecorationType, []);
        }
    }

    /**
     * Dispose decoration types. Called from extension deactivate() (F12 fix).
     */
    dispose(): void {
        if (this._disposed) return;
        this._disposed = true;
        this._errorDecorationType.dispose();
        this._warningDecorationType.dispose();
    }
}
