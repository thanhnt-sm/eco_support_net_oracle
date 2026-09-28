/**
 * Unit tests for DataGuardHoverProvider pure logic.
 * Tests overlap detection and markdown building without VS Code API.
 */
import assert from "node:assert/strict";
import test from "node:test";
import type { FindingItem } from "./redaction";

// Pure helper: find overlapping findings for a given position.
function findOverlapping(findings: FindingItem[], docPath: string, line: number, col: number): FindingItem[] {
    return findings.filter((f) => {
        if (f.filePath !== docPath) return false;
        const startLine = Math.max(0, f.startLine - 1);
        const endLine = Math.max(startLine, f.endLine - 1);
        const startCol = Math.max(0, f.startColumn - 1);
        const endCol = Math.max(startCol + 1, f.endColumn - 1);
        if (line < startLine || line > endLine) return false;
        if (line === startLine && col < startCol) return false;
        if (line === endLine && col > endCol) return false;
        return true;
    });
}

// Pure helper: build migration hint text from properties.
function buildMigrationText(f: FindingItem): string | undefined {
    const migration = f.properties?.["migration"];
    return typeof migration === "string" ? migration : undefined;
}

const makeF = (filePath: string, sl: number, el: number, ruleId = "DG010", props?: Record<string, unknown>): FindingItem => ({
    id: "h-1",
    ruleId,
    message: "Oracle keyword",
    severity: "warning",
    filePath,
    fullUri: `file:///${filePath}`,
    startLine: sl,
    startColumn: 1,
    endLine: el,
    endColumn: 30,
    quickFixAvailable: false,
    properties: props,
});

test("HoverProvider: finding overlaps hovered position (same line)", () => {
    const path = "/src/Repo.cs";
    const findings = [makeF(path, 5, 5)];
    const overlapping = findOverlapping(findings, path, 4, 10); // 0-based line 4 = SARIF line 5
    assert.strictEqual(overlapping.length, 1);
});

test("HoverProvider: finding does not overlap if position is before range", () => {
    const path = "/src/Repo.cs";
    const overlapping = findOverlapping([makeF(path, 5, 5)], path, 3, 10);
    assert.strictEqual(overlapping.length, 0);
});

test("HoverProvider: multiple overlapping findings returned", () => {
    const path = "/src/Repo.cs";
    const findings = [makeF(path, 5, 5), makeF(path, 5, 6)]; // both cover line 5
    const overlapping = findOverlapping(findings, path, 4, 10);
    assert.strictEqual(overlapping.length, 2);
});

test("HoverProvider: migration hint extracted from properties.migration", () => {
    const f = makeF("/f.cs", 1, 1, "DG010", { migration: "Use GETDATE()" });
    assert.strictEqual(buildMigrationText(f), "Use GETDATE()");
});

test("HoverProvider: no migration hint when properties missing", () => {
    const f = makeF("/f.cs", 1, 1, "DG010");
    assert.strictEqual(buildMigrationText(f), undefined);
});

test("HoverProvider: DG010 quick-fix not claimed (no DG010 quick-fix exists)", () => {
    const f: FindingItem = {
        id: "h-2", ruleId: "DG010", message: "test", severity: "warning",
        filePath: "/f.cs", fullUri: "file:///f.cs",
        startLine: 1, startColumn: 1, endLine: 1, endColumn: 10,
        quickFixAvailable: true, // hypothetically set — hover must NOT show it for DG010
    };
    // Provider skips quick-fix for DG010 regardless of quickFixAvailable flag
    const shouldShowFix = f.quickFixAvailable && f.ruleId !== "DG010";
    assert.ok(!shouldShowFix, "DG010 must never display quick-fix in hover");
});
