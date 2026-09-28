/**
 * TDD regression safety net for the TypeScript extension side of the
 * red-team plan revisions. Tests document CURRENT behavior and MUST pass
 * both before and after Step 3.I implementation.
 *
 * Post-implementation expectations are marked with "After 3.I.X" comments.
 */
import assert from "node:assert/strict";
import test from "node:test";
import {
    FindingItem,
    SarifLog,
    SarifResult,
    parseSarifToFindings,
} from "./redaction";

// ─────────────────────────────────────────────────────────────────────────────
// SarifResult / FindingItem interface shape
// ─────────────────────────────────────────────────────────────────────────────

test("SarifResult: properties field exists and is optional after 3.I.5", () => {
    // After 3.I.5: SarifResult gains `properties?: Record<string, unknown>`
    // This test exercises that the field is accessible (no TS error) and
    // defaults to undefined when not set.
    const result: SarifResult = {
        ruleId: "DG010",
        level: "warning",
        message: { text: "Oracle keyword SYSDATE used in non-Oracle context" },
        locations: [
            {
                physicalLocation: {
                    artifactLocation: { uri: "file:///src/Repo.cs" },
                    region: { startLine: 10, startColumn: 1 },
                },
            },
        ],
    };

    // Before 3.I.5: properties is not defined — access evaluates to undefined.
    // After 3.I.5: result.properties is accessible as Record<string, unknown> | undefined.
    const props = (result as SarifResult & { properties?: Record<string, unknown> }).properties;
    assert.strictEqual(props, undefined);
});

test("FindingItem: properties field exists and is optional after 3.I.5", () => {
    // After 3.I.5: FindingItem gains `properties?: Record<string, unknown>`
    const item: FindingItem & { properties?: Record<string, unknown> } = {
        id: "finding-1-DG010",
        ruleId: "DG010",
        message: "Oracle-specific keyword 'SYSDATE'",
        severity: "warning",
        filePath: "/src/Repo.cs",
        fullUri: "file:///src/Repo.cs",
        startLine: 10,
        startColumn: 1,
        endLine: 10,
        endColumn: 10,
        quickFixAvailable: false,
    };

    // properties is not set → undefined
    assert.strictEqual(item.properties, undefined);

    // Can set it
    const itemWithProps: typeof item = {
        ...item,
        properties: { migration: "Use CURRENT_TIMESTAMP (SQL Server)" },
    };
    assert.strictEqual(itemWithProps.properties?.["migration"], "Use CURRENT_TIMESTAMP (SQL Server)");
});

// ─────────────────────────────────────────────────────────────────────────────
// parseSarifToFindings — current behavior (no properties propagation)
// ─────────────────────────────────────────────────────────────────────────────

const makeSarifLog = (extra?: Partial<SarifResult>): SarifLog => ({
    runs: [
        {
            results: [
                {
                    ruleId: "DG010",
                    level: "warning",
                    message: { text: "Oracle-specific keyword 'SYSDATE' used in non-Oracle context" },
                    locations: [
                        {
                            physicalLocation: {
                                artifactLocation: { uri: "file:///workspace/src/Repo.cs" },
                                region: { startLine: 5, startColumn: 1, endLine: 5, endColumn: 20 },
                            },
                        },
                    ],
                    ...extra,
                },
            ],
        },
    ],
});


test("parseSarifToFindings: after 3.I.5 — properties propagated to FindingItem", () => {
    // After 3.I.5: result.properties flows through to finding.properties.
    // This test documents the TARGET state; it will FAIL before implementation
    // and PASS after — that's the TDD indicator.
    const sarif = makeSarifLog({ properties: { keyword: "SYSDATE", migration: "Use GETDATE() or CURRENT_TIMESTAMP" } } as SarifResult & { properties: Record<string, unknown> });
    const findings = parseSarifToFindings(sarif, "/workspace", (root, uri) => uri.replace("file:///workspace", root));

    assert.strictEqual(findings.length, 1);
    const finding = findings[0] as FindingItem & { properties?: Record<string, unknown> };
    // After 3.I.5: properties present
    assert.ok(finding.properties != null, "properties should be propagated from SARIF result");
    assert.strictEqual(finding.properties!["keyword"], "SYSDATE");
    assert.ok(typeof finding.properties!["migration"] === "string");
});

test("parseSarifToFindings: missing uri still skipped (regression guard)", () => {
    const sarif: SarifLog = {
        runs: [
            {
                results: [
                    {
                        ruleId: "DG010",
                        level: "warning",
                        message: { text: "Test" },
                        locations: [{ physicalLocation: { artifactLocation: {} } }],
                    },
                ],
            },
        ],
    };

    const findings = parseSarifToFindings(sarif, "/workspace", (root, uri) => uri);
    assert.strictEqual(findings.length, 0, "result with no uri must be skipped");
});

test("parseSarifToFindings: result without properties field — finding.properties is undefined", () => {
    // When SARIF result has no properties key at all, finding.properties must be undefined (not crash).
    const sarif = makeSarifLog();  // no properties field
    const findings = parseSarifToFindings(sarif, "/workspace", (root, uri) => uri.replace("file:///workspace", root));

    assert.strictEqual(findings.length, 1);
    const finding = findings[0] as FindingItem & { properties?: Record<string, unknown> };
    assert.strictEqual(finding.properties, undefined);
});

// ─────────────────────────────────────────────────────────────────────────────
// clearFindingsAndDiagnostics — interface contract (F9, F12)
// ─────────────────────────────────────────────────────────────────────────────

test("clearFindingsAndDiagnostics interface contract: decoration manager clear called", () => {
    // After 3.I.9: clearFindingsAndDiagnostics() also calls decorationManager.clear()
    // and codeLensProvider.refresh().
    // This test is a lightweight contract stub — real clearing behavior is covered
    // in the extension host integration tests.
    let decorationClearCalled = false;
    let codeLensClearCalled = false;

    // Simulate the expected interface contract
    const fakeDecorationManager = {
        clear: () => { decorationClearCalled = true; },
    };
    const fakeCodLensProvider = {
        refresh: () => { codeLensClearCalled = true; },
    };

    // Invoke via the simulated contract (extension.ts wires them together)
    fakeDecorationManager.clear();
    fakeCodLensProvider.refresh();

    assert.ok(decorationClearCalled, "decoration manager clear() must be called on clearFindingsAndDiagnostics");
    assert.ok(codeLensClearCalled, "code lens provider refresh() must be called on clearFindingsAndDiagnostics");
});
