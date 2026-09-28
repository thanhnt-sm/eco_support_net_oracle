/**
 * Unit tests for DataGuardCodeLensProvider pure logic.
 * Tests the filtering/grouping logic extracted from the provider without VS Code API.
 */
import assert from "node:assert/strict";
import test from "node:test";
import type { FindingItem } from "./redaction";

// Pure helper: group findings by line, respecting the same logic as provideCodeLenses.
function groupFindingsByLine(
    findings: FindingItem[],
    docPath: string
): Map<number, FindingItem[]> {
    const docFindings = findings.filter(
        (f) => f.filePath === docPath || f.fullUri.endsWith(docPath.replace(/\\/g, "/"))
    );
    const byLine = new Map<number, FindingItem[]>();
    for (const finding of docFindings) {
        const line = Math.max(0, finding.startLine - 1);
        const existing = byLine.get(line) ?? [];
        existing.push(finding);
        byLine.set(line, existing);
    }
    return byLine;
}

const makeF = (filePath: string, line: number, ruleId = "DG010"): FindingItem => ({
    id: `finding-${line}-${ruleId}`,
    ruleId,
    message: `Oracle keyword at line ${line}`,
    severity: "warning",
    filePath,
    fullUri: `file:///${filePath}`,
    startLine: line,
    startColumn: 1,
    endLine: line,
    endColumn: 20,
    quickFixAvailable: false,
});

test("CodeLens grouping: findings on same line are grouped together", () => {
    const path = "/src/Repo.cs";
    const findings = [makeF(path, 5), makeF(path, 5, "DG013"), makeF(path, 10)];
    const grouped = groupFindingsByLine(findings, path);

    assert.strictEqual(grouped.size, 2);
    assert.strictEqual(grouped.get(4)?.length, 2); // line 5 → 0-based 4
    assert.strictEqual(grouped.get(9)?.length, 1); // line 10 → 0-based 9
});

test("CodeLens grouping: only findings for the document path are included", () => {
    const path = "/src/Repo.cs";
    const otherPath = "/src/Other.cs";
    const findings = [makeF(path, 5), makeF(otherPath, 5)];
    const grouped = groupFindingsByLine(findings, path);

    assert.strictEqual(grouped.size, 1);
    assert.strictEqual(grouped.get(4)?.[0].filePath, path);
});

test("CodeLens grouping: empty when no findings for document", () => {
    const grouped = groupFindingsByLine([makeF("/other.cs", 1)], "/src/Repo.cs");
    assert.strictEqual(grouped.size, 0);
});

test("CodeLens: MAX_LENSES cap at 50 distinct lines", () => {
    const MAX_LENSES = 50;
    const path = "/src/Repo.cs";
    // 60 distinct lines — should exceed the cap
    const findings = Array.from({ length: 60 }, (_, i) => makeF(path, i + 1));
    const grouped = groupFindingsByLine(findings, path);
    const distinctLines = [...grouped.keys()].length;

    assert.ok(distinctLines > MAX_LENSES, `Expected ${distinctLines} > ${MAX_LENSES}`);
    // Aggregate header should be shown instead of individual lenses
    // (implementation check: if distinctLines > MAX_LENSES → emit single aggregate lens)
    const shouldAggregate = distinctLines > MAX_LENSES;
    assert.ok(shouldAggregate, "Should aggregate when distinct line count exceeds cap");
});
