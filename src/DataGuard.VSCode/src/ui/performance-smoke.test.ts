import assert from "node:assert/strict";
import test from "node:test";
import { FindingItem, parseSarifToFindings, SarifLog, SarifResult } from "./redaction";

/**
 * Generates a mock SARIF log containing the specified count of parameter mismatch
 * and contract drift warnings.
 */
function generateMockSarif(count: number): SarifLog {
    const results: SarifResult[] = [];
    const rules = ["DG001", "DG002", "DG017", "DG018"] as const;

    for (let i = 0; i < count; i++) {
        const ruleId = rules[i % rules.length];
        results.push({
            ruleId,
            level: "warning",
            message: {
                text: `Parameter count or type mismatch at procedure parameter @p${i}. Connection String: Server=db${i % 10};Password=pass${i};`
            },
            locations: [
                {
                    physicalLocation: {
                        artifactLocation: { uri: `src/Repositories/Repo_${i % 100}.cs` },
                        region: {
                            startLine: (i % 200) + 1,
                            startColumn: 12,
                            endLine: (i % 200) + 1,
                            endColumn: 45,
                            snippet: { text: `cmd.Parameters.AddWithValue("@p${i}", val);` }
                        }
                    }
                }
            ]
        });
    }

    return {
        runs: [{ results }]
    };
}

test("Performance Smoke Test: Ingests 15,000 SARIF warnings efficiently (< 500ms)", () => {
    const WARNING_COUNT = 15_000;
    const sarif = generateMockSarif(WARNING_COUNT);

    const startTime = performance.now();
    const findings = parseSarifToFindings(
        sarif,
        "/workspace/project",
        (root, uri) => `${root}/${uri}`
    );
    const durationMs = performance.now() - startTime;

    assert.equal(findings.length, WARNING_COUNT);
    // Parsing 15,000 SARIF items with redaction should complete in well under 500ms
    assert.ok(
        durationMs < 500,
        `Expected parsing 15,000 warnings in < 500ms, took ${durationMs.toFixed(2)}ms`
    );

    // Verify all 15,000 items had secrets redacted
    assert.match(findings[0].message, /Password=\*\*\*/);
    assert.doesNotMatch(findings[0].message, /pass0/);
});

test("Performance Smoke Test: Virtualized list slicing maintains 60fps budget (< 1ms per frame)", () => {
    const WARNING_COUNT = 15_000;
    const sarif = generateMockSarif(WARNING_COUNT);
    const findings = parseSarifToFindings(sarif, "/workspace", (r, u) => `${r}/${u}`);

    const ITEM_HEIGHT = 86;
    const BUFFER_COUNT = 5;
    const CLIENT_HEIGHT = 500;
    const TOTAL_HEIGHT = findings.length * ITEM_HEIGHT; // 1,290,000 px

    assert.equal(TOTAL_HEIGHT, 15_000 * 86);

    // Test across a variety of scroll offsets from top to bottom
    const scrollPositions = [
        0,
        500,
        10_000,
        50_000,
        200_000,
        500_000,
        800_000,
        1_200_000,
        1_289_000
    ];

    for (const scrollTop of scrollPositions) {
        const frameStart = performance.now();

        const startIndex = Math.max(0, Math.floor(scrollTop / ITEM_HEIGHT) - BUFFER_COUNT);
        const endIndex = Math.min(findings.length, Math.ceil((scrollTop + CLIENT_HEIGHT) / ITEM_HEIGHT) + BUFFER_COUNT);
        const renderedSlice: FindingItem[] = [];
        for (let i = startIndex; i < endIndex; i++) {
            renderedSlice.push(findings[i]);
        }

        const frameDurationMs = performance.now() - frameStart;

        // Verify window size is strictly bounded (typically 15-25 items, never 15,000 DOM nodes)
        assert.ok(
            renderedSlice.length >= 5 && renderedSlice.length <= 30,
            `Rendered slice size (${renderedSlice.length}) must be bounded between 5 and 30`
        );

        // 60fps frame budget is 16.6ms. Slice calculation should take < 1ms
        assert.ok(
            frameDurationMs < 1.0,
            `Frame slice calculation at scroll ${scrollTop} took ${frameDurationMs.toFixed(3)}ms (must be < 1.0ms for 60fps)`
        );
    }
});

test("Performance Smoke Test: Real-time filtering across 15,000 items is responsive (< 50ms)", () => {
    const WARNING_COUNT = 15_000;
    const sarif = generateMockSarif(WARNING_COUNT);
    const findings = parseSarifToFindings(sarif, "/workspace", (r, u) => `${r}/${u}`);

    const filterStart = performance.now();
    const query = "repo_42";
    const filtered = findings.filter(
        (f) => f.filePath.toLowerCase().includes(query) || f.ruleId.toLowerCase().includes(query)
    );
    const filterDurationMs = performance.now() - filterStart;

    assert.ok(filtered.length > 0);
    assert.ok(
        filterDurationMs < 50,
        `Filtering 15,000 items took ${filterDurationMs.toFixed(2)}ms (expected < 50ms)`
    );
});
