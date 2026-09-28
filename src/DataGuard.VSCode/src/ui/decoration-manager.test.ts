/**
 * Unit tests for DataGuardDecorationManager pure logic.
 * Tests range conversion and severity grouping without VS Code API.
 */
import assert from "node:assert/strict";
import test from "node:test";
import type { FindingItem } from "./redaction";

// Pure helper: convert 1-based SARIF coordinates to 0-based VS Code range values.
function toVSCodeRange(f: FindingItem): { startLine: number; startCol: number; endLine: number; endCol: number } {
    return {
        startLine: Math.max(0, f.startLine - 1),
        startCol: Math.max(0, f.startColumn - 1),
        endLine: Math.max(0, f.endLine - 1),
        endCol: Math.max(0, f.endColumn - 1),
    };
}

// Pure helper: group findings by severity for a given file.
function groupBySeverity(findings: FindingItem[], filePath: string) {
    return findings
        .filter((f) => f.filePath === filePath)
        .reduce<{ errors: FindingItem[]; warnings: FindingItem[] }>(
            (acc, f) => {
                if (f.severity === "error") acc.errors.push(f);
                else acc.warnings.push(f);
                return acc;
            },
            { errors: [], warnings: [] }
        );
}

const makeF = (filePath: string, line: number, severity: "error" | "warning" = "warning"): FindingItem => ({
    id: `d-${line}`,
    ruleId: "DG010",
    message: "Test",
    severity,
    filePath,
    fullUri: `file:///${filePath}`,
    startLine: line,
    startColumn: 5,
    endLine: line,
    endColumn: 20,
    quickFixAvailable: false,
});

test("DecorationManager: 1-based SARIF line converted to 0-based VS Code line", () => {
    const range = toVSCodeRange(makeF("/f.cs", 10));
    assert.strictEqual(range.startLine, 9);
    assert.strictEqual(range.startCol, 4);
});

test("DecorationManager: line 1 maps to 0-based line 0 (no underflow)", () => {
    const range = toVSCodeRange(makeF("/f.cs", 1));
    assert.strictEqual(range.startLine, 0);
});

test("DecorationManager: errors and warnings correctly grouped", () => {
    const path = "/src/Repo.cs";
    const findings = [
        makeF(path, 1, "error"),
        makeF(path, 2, "warning"),
        makeF(path, 3, "error"),
    ];
    const { errors, warnings } = groupBySeverity(findings, path);
    assert.strictEqual(errors.length, 2);
    assert.strictEqual(warnings.length, 1);
});

test("DecorationManager: only current file findings applied (other files excluded)", () => {
    const path = "/src/Repo.cs";
    const findings = [makeF(path, 1), makeF("/src/Other.cs", 2)];
    const { errors, warnings } = groupBySeverity(findings, path);
    assert.strictEqual(errors.length + warnings.length, 1);
});
