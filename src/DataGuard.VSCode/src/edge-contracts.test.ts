import assert from "node:assert/strict";
import test from "node:test";
import { buildChildEnvironment, createCliProgressSummary, describeCliStatus, describeGroundTruth, explainCliExit, formatProgressLine, recordProgressLine } from "./ide-safe-contract";
import { connectionSecretKey, redactAndBoundSensitiveText, resolveWorkspaceSarifPath } from "./security";
import { maskConnectionHost } from "./live-database-confirmation";
import { RunCoordinator } from "./run-coordinator";
import { escapeHtml, parseSarifToFindings, redactForUi } from "./ui/redaction";
import { renderDashboardHtml } from "./ui/dashboard-view";

test("exit explanations distinguish incomplete, cancelled and unknown commands without hiding missing output", () => {
    const empty = createCliProgressSummary(); const summary = { ...empty, hasSummary: true };
    for (const command of ["validate", "assess", "scan"]) {
        assert.match(explainCliExit(command, 130, empty, false), /CANCELLED/);
        assert.match(explainCliExit(command, 99, empty, false), /Unexpected exit code 99/);
        assert.match(explainCliExit(command, null, empty, false), /Unexpected exit code unknown/);
        for (const code of [0, 1]) { assert.match(explainCliExit(command, code, summary, false), /failed to write results/); }
    }
    assert.match(explainCliExit("validate", 1, summary, true), /^\[WARN\] Validation found errors/);
    assert.match(explainCliExit("validate", 0, { ...summary, warningCount: 1 }, true), /completed with warnings/);
    assert.match(explainCliExit("validate", 1, { ...summary, groundTruthMissing: true }, true), /syntactic only/);
    assert.match(describeCliStatus(empty), /unavailable/);
    assert.equal(describeCliStatus(summary), "StackAccord: 0 errors, 0 warnings");
    assert.equal(describeGroundTruth(empty), undefined);
    assert.equal(describeGroundTruth({ ...summary, groundTruth: "synthetic snapshot" }), "Ground truth: synthetic snapshot");
});

test("malformed summary counts cannot be accepted as a successful validation summary", () => {
    const cases: unknown[] = [
        {}, { Kind: "Summary", Phase: 4, Data: {} }, { Kind: "Summary", Phase: "done" },
        { Kind: "Summary", Phase: "done", Data: "invalid" },
        { Kind: "Summary", Phase: "done", Data: { ErrorCount: -1, WarningCount: 0 } },
        { Kind: "Summary", Phase: "done", Data: { ErrorCount: 0, WarningCount: 1.5 } },
        { Kind: "Summary", Phase: "Assessment complete", Data: { ErrorCount: 0, WarningCount: 0 } },
    ];
    for (const value of cases) { const state = createCliProgressSummary(); recordProgressLine(JSON.stringify(value), state); assert.equal(state.hasSummary, false); }
    const state = createCliProgressSummary(); recordProgressLine("{invalid}", state); assert.equal(state.hasSummary, false);
    recordProgressLine(JSON.stringify({ Kind: "Summary", Phase: "done", Data: { ErrorCount: 0, WarningCount: 0, GroundTruth: "" } }), state);
    assert.equal(state.hasSummary, true); assert.equal(state.groundTruth, undefined);
    assert.equal(formatProgressLine(" \r\n "), undefined);
    assert.equal(formatProgressLine('{"Kind":"BaselineApplied"}'), "[WARN] baseline: violations suppressed by baseline");
    assert.equal(formatProgressLine('{"Kind":"BaselineApplied","Data":3}'), "[WARN] baseline: violations suppressed by baseline");
});

test("child environment strips case-insensitive argument-selected credentials even with missing argument values", () => {
    const env = { SAFE: "yes", Mixed_Secret: "synthetic", DATAGUARD_CONNECTION_STRING: "synthetic" };
    assert.deepEqual(buildChildEnvironment(env, { args: ["--ide-safe", "--connection-env", " mixed_secret "] }), { SAFE: "yes" });
    assert.deepEqual(buildChildEnvironment(env, { args: ["--ide-safe", "--connection-env"] }), { SAFE: "yes", Mixed_Secret: "synthetic" });
    assert.equal(maskConnectionHost("ignored segment;Host=localhost"), "loc***");
});

test("cancellation failures do not corrupt reservation ownership or prevent replacement", () => {
    const c = new RunCoordinator<{ cancelled: boolean; cancel(): void }>();
    const broken = () => ({ cancelled: false, cancel() { throw new Error("synthetic cancellation failure"); } });
    const first = broken(); c.replace(first); const second = broken();
    assert.equal(c.replace(second), first); assert.equal(first.cancelled, true); assert.equal(c.current, second);
    const token = c.nextReservation(); assert.equal(second.cancelled, true); assert.equal(c.current, undefined);
    assert.ok(c.isReservationCurrent(token)); c.cancel(); assert.equal(c.isReservationCurrent(token), false);
});

test("redaction and SARIF boundary defaults reject invalid locations and preserve safe empty values", () => {
    assert.equal(redactForUi(""), ""); assert.equal(escapeHtml(""), "");
    assert.throws(() => connectionSecretKey(""), /must not be empty/);
    for (const length of [0, -1, 1.5, NaN]) { assert.throws(() => redactAndBoundSensitiveText("x", length), /positive integer/); }
    assert.match(redactAndBoundSensitiveText("1234567890", 3), /^123\n.*truncated/);
    assert.throws(() => resolveWorkspaceSarifPath("/workspace", "file://[invalid"), /malformed/);
    assert.deepEqual(parseSarifToFindings({}, "/workspace", resolveWorkspaceSarifPath), []);
    const results = parseSarifToFindings({ runs: [{}, { results: [
        { locations: [{ physicalLocation: { artifactLocation: { uri: "../outside.cs" } } }] },
        { locations: [{ physicalLocation: { artifactLocation: { uri: "Example.cs" } } }] },
    ] }] }, "/workspace", resolveWorkspaceSarifPath);
    assert.equal(results.length, 1); assert.equal(results[0].ruleId, "DG000");
    assert.equal(results[0].message, "Database contract drift detected");
    assert.deepEqual([results[0].startLine, results[0].startColumn, results[0].endLine, results[0].endColumn], [1, 1, 1, 1]);
});

test("dashboard renderer normalizes optional arrays and target-type locations in report payloads", () => {
    const html = renderDashboardHtml(null as any, "nonce", { filesScanned: 1, queriesFound: 2, connectionsFound: 1,
        connections: [{ name: "synthetic", provider: "oracle" }], queries: [
            { operation: "Read", mappingStatus: "untyped", action: "inspect", location: {}, targetTypeLocation: {} },
            { sql: "SELECT 1", operation: "Read", targetTypeLocation: { file: "Dto.cs", line: 3 } },
        ] } as any);
    assert.match(html, /"tables":\[\]/); assert.match(html, /"columns":\[\]/); assert.match(html, /"properties":\[\]/);
    assert.match(html, /"targetTypeLocation":\{"file":"Dto.cs","line":3\}/);
    assert.match(html, /"location":\{"file":""\}/);
    const empty = renderDashboardHtml([], "nonce", { connections: null, queries: null } as any);
    assert.match(empty, /"connections":\[\],"queries":\[\]/);
});
