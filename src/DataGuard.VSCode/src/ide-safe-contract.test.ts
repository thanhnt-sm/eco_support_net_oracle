import assert from "node:assert/strict";
import * as fs from "fs";
import * as path from "path";
import test from "node:test";
import {
    buildChildEnvironment,
    buildIdeSafeFailureMessage,
    createCliProgressSummary,
    describeCliStatus,
    describeGroundTruth,
    explainCliExit,
    formatProgressLine,
    hasIdeSafeAck,
    isGroundTruthMissingLine,
    isOldCliRejection,
    isPolicyLine,
    MIN_CLI_VERSION,
    recordProgressLine,
} from "./ide-safe-contract";

const ACK = "ide-safe: active";
const OLD_CLI_LINE = "Unrecognized command or argument '--ide-safe'.";

test("hasIdeSafeAck accepts only an exact ack as the first non-empty stderr line", () => {
    assert.equal(hasIdeSafeAck(ACK), true);
    assert.equal(hasIdeSafeAck(`  ${ACK}\r`), true, "surrounding whitespace and CR are ignored");
    assert.equal(hasIdeSafeAck(undefined), false, "no stderr line at all");
    assert.equal(hasIdeSafeAck(""), false);
    assert.equal(hasIdeSafeAck("ide-safe: suppressed X"), false, "the ack must be the first non-empty line, not a later one");
    assert.equal(hasIdeSafeAck("ide-safe: active!"), false, "no prefix match");
    assert.equal(hasIdeSafeAck("IDE-SAFE: ACTIVE"), false, "exact case");
    assert.equal(hasIdeSafeAck(`{"Kind":"PhaseStarted","Detail":"${ACK}"}`), false, "a progress event is not the ack");
});

test("isOldCliRejection is anchored at line start and recognises both new flags", () => {
    assert.equal(isOldCliRejection(OLD_CLI_LINE), true);
    assert.equal(isOldCliRejection(`${OLD_CLI_LINE}\r`), true);
    assert.equal(isOldCliRejection("Unrecognized command or argument '--allow-env-connection'."), true, "the PR #24 CLI accepts --ide-safe but not the carve-out flag");
    assert.equal(isOldCliRejection(`Error: cannot read 'C:\\repo\\${OLD_CLI_LINE}\\x.cs'`), false, "a spoofed path containing the quoted flag mid-line is not an old CLI");
    assert.equal(isOldCliRejection("ide-safe: suppressed '--ide-safe' note"), false);
    assert.equal(isOldCliRejection("Unrecognized command or argument '--progress'."), false, "another unknown option is not the ide-safe rejection");
    assert.equal(isOldCliRejection(undefined), false);
    assert.equal(isOldCliRejection(""), false);
});

test("MIN_CLI_VERSION names the first CLI that acknowledges ide-safe", () => {
    assert.equal(MIN_CLI_VERSION, "0.3.0");
});

test("buildIdeSafeFailureMessage adds the update hint only for an old-CLI rejection", () => {
    const base = "DataGuard CLI did not confirm IDE-safe mode; results were discarded";
    assert.equal(buildIdeSafeFailureMessage(undefined), base);
    assert.equal(buildIdeSafeFailureMessage(""), base);
    assert.equal(buildIdeSafeFailureMessage("Validation failed: boom"), base);
    const withHint = buildIdeSafeFailureMessage(OLD_CLI_LINE);
    assert.ok(withHint.startsWith(base));
    assert.ok(withHint.endsWith(`Update the dataguard CLI (${MIN_CLI_VERSION} or later) or set dataguard.cliPath`));
});

test("formatProgressLine renders the exact ide-safe ack as [INFO] and every other ide-safe:/baseline: line as [WARN]", () => {
    assert.equal(
        formatProgressLine("ide-safe: suppressed ConnectionString from config (database access disabled)"),
        "[WARN] ide-safe: suppressed ConnectionString from config (database access disabled)",
    );
    assert.equal(formatProgressLine("ide-safe: active"), "[INFO] ide-safe: active", "the acknowledgement is not a warning");
    assert.equal(formatProgressLine("  ide-safe: active\r"), "[INFO] ide-safe: active", "trimmed before the exact comparison");
    assert.equal(formatProgressLine("ide-safe: active!"), "[WARN] ide-safe: active!", "only the exact ack line is [INFO]");
    assert.equal(formatProgressLine("ide-safe: kept environment connection (--allow-env-connection)"), "[WARN] ide-safe: kept environment connection (--allow-env-connection)");
    assert.equal(
        formatProgressLine("baseline: 3 violations suppressed by .dataguard-baseline.json"),
        "[WARN] baseline: 3 violations suppressed by .dataguard-baseline.json",
    );
    assert.equal(formatProgressLine("ide-safe: kept Password=hunter2"), "[WARN] ide-safe: kept Password=[REDACTED]", "channel lines stay redacted");
});

test("formatProgressLine renders a BaselineApplied progress event as a [WARN] line", () => {
    const event = JSON.stringify({ Kind: "BaselineApplied", Phase: "Validating rules", Detail: ".dataguard-baseline.json", Data: { SuppressedCount: 3 } });
    assert.equal(formatProgressLine(event), "[WARN] baseline: 3 suppressed by .dataguard-baseline.json");
    const noCount = JSON.stringify({ Kind: "BaselineApplied", Detail: ".dataguard-baseline.json", Data: { SuppressedCount: "many" } });
    assert.equal(formatProgressLine(noCount), "[WARN] baseline: violations suppressed by .dataguard-baseline.json", "an invalid count is never echoed");
});

test("formatProgressLine keeps the existing rendering for other progress events and prefixed lines", () => {
    assert.equal(formatProgressLine(JSON.stringify({ Kind: "PhaseStarted", Phase: "Extracting", Detail: "Server=db;Password=x" })), "Server=db;Password=[REDACTED]");
    assert.equal(formatProgressLine(JSON.stringify({ Kind: "PhaseStarted", Phase: "Extracting" })), "Extracting");
    assert.equal(formatProgressLine(JSON.stringify({ Kind: "PhaseStarted" })), undefined);
    assert.equal(formatProgressLine(JSON.stringify({ NotAKind: true })), undefined);
    assert.equal(formatProgressLine("[INFO] Found SQL"), "[INFO] Found SQL");
    assert.equal(formatProgressLine("[ERROR] token=abc"), "[ERROR] token=[REDACTED]");
    assert.equal(formatProgressLine("random CLI chatter"), undefined);
    assert.equal(formatProgressLine("{not json}"), undefined);
});

test("formatProgressLine renders Warning:, UNEVALUATED:, ACQUISITION: and Rule not evaluated policy lines as [WARN]", () => {
    const gtWarn = "Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran";
    assert.equal(formatProgressLine(gtWarn), `[WARN] ${gtWarn}`);
    assert.equal(
        formatProgressLine("UNEVALUATED: 3 contract(s) could not be evaluated"),
        "[WARN] UNEVALUATED: 3 contract(s) could not be evaluated",
    );
    assert.equal(
        formatProgressLine("ACQUISITION: src/Snapshot.cs: EF model not used: no entities"),
        "[WARN] ACQUISITION: src/Snapshot.cs: EF model not used: no entities",
    );
    assert.equal(
        formatProgressLine("Rule DG012 not evaluated: provider mismatch"),
        "[WARN] Rule DG012 not evaluated: provider mismatch",
    );
    assert.equal(
        formatProgressLine("UNEVALUATED: contract acquisition failed: Login failed for Password=Hunter2"),
        "[WARN] UNEVALUATED: contract acquisition failed: Login failed for Password=[REDACTED]",
    );
    assert.equal(isPolicyLine(" Warning: indented"), false, "policy prefix must be anchored at line start");
    assert.equal(isPolicyLine("[DG1290] C:\\x\\Warning: no ground truth.csproj"), false, "mid-line substring must not match");
    assert.equal(isGroundTruthMissingLine(gtWarn), true);
    assert.equal(isGroundTruthMissingLine("UNEVALUATED: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran"), true);
    assert.equal(isGroundTruthMissingLine("[DG1290] C:\\x\\Warning: no ground truth.csproj: SQL literal skipped"), false);
});

test("recordProgressLine and formatProgressLine capture Summary counts, GroundTruth kind, and GroundTruthMissing", () => {
    const richSummary = JSON.stringify({
        Kind: "Summary",
        Phase: "Validation complete",
        Data: {
            ErrorCount: 2,
            WarningCount: 1,
            ViolationCount: 3,
            UnavailableRuleCount: 1,
            UnevaluatedContractCount: 3,
            AcquisitionDiagnosticCount: 0,
            GroundTruth: "entities",
        },
    });
    assert.equal(
        formatProgressLine(richSummary),
        "✔ Validation complete: 2 errors, 1 warnings; ground truth: entities; unevaluated 3; unavailable rules 1",
    );

    const summary = createCliProgressSummary();
    recordProgressLine("ide-safe: active", summary);
    recordProgressLine("Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran", summary);
    recordProgressLine(richSummary, summary);

    assert.equal(summary.hasSummary, true);
    assert.equal(summary.errorCount, 2);
    assert.equal(summary.warningCount, 1);
    assert.equal(summary.unevaluatedContractCount, 3);
    assert.equal(summary.unavailableRuleCount, 1);
    assert.equal(summary.acquisitionDiagnosticCount, 0);
    assert.equal(summary.groundTruth, "entities");
    assert.equal(summary.groundTruthMissing, true);
    assert.equal(describeCliStatus(summary), "DataGuard: syntactic only (2 errors, 1 warnings)");
    assert.equal(describeGroundTruth(summary), "Ground truth: not loaded (syntactic rules only)");
});

test("explainCliExit distinguishes exit codes 0/1/2/3/4 and GroundTruthMissing/Unevaluated matching Visual Studio Phase 2", () => {
    const cleanSummary = { ...createCliProgressSummary(), hasSummary: true };
    assert.equal(explainCliExit("validate", 0, cleanSummary, true), "[OK] No issues found.");

    const syntacticSummary = { ...createCliProgressSummary(), hasSummary: true, groundTruthMissing: true };
    const syntacticZero = explainCliExit("validate", 0, syntacticSummary, true);
    assert.ok(syntacticZero.startsWith("[WARN] Ran syntactic rules only"));
    assert.ok(syntacticZero.includes("database contract was NOT compared"));
    assert.ok(!syntacticZero.includes("[OK]"));

    const syntacticOne = explainCliExit("validate", 1, syntacticSummary, true);
    assert.ok(syntacticOne.startsWith("[WARN] Validation found errors."));
    assert.ok(syntacticOne.endsWith("Ground truth was not loaded (syntactic only)."));

    const crashSummary = { ...createCliProgressSummary(), hasSummary: false, groundTruthMissing: true };
    assert.ok(explainCliExit("validate", 1, crashSummary, false).includes("failed before producing a validation summary"));

    const unevaluatedSummary = { ...createCliProgressSummary(), hasSummary: true, unevaluatedContractCount: 2 };
    const unevaluatedZero = explainCliExit("validate", 0, unevaluatedSummary, true);
    assert.ok(!unevaluatedZero.includes("syntactic"));
    assert.ok(unevaluatedZero.includes("[WARN] 2 contract(s) could not be evaluated against the loaded ground truth (see UNEVALUATED lines)."));

    assert.ok(explainCliExit("validate", 2, createCliProgressSummary(), false).startsWith("[ERROR] Invalid arguments or configuration."));
    assert.ok(explainCliExit("validate", 3, createCliProgressSummary(), false).startsWith("[WARN] Validation incomplete:"));
    assert.equal(
        explainCliExit("validate", 4, createCliProgressSummary(), false),
        "[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above).",
    );
    assert.equal(
        explainCliExit("assess", 1, createCliProgressSummary(), true),
        "[WARN] Assessment found findings. See Error List.",
    );
    assert.equal(
        explainCliExit("assess", 2, createCliProgressSummary(), false),
        "[ERROR] Invalid arguments for assess. See the [DataGuard CLI] lines above.",
    );
    assert.equal(
        explainCliExit("assess", 4, createCliProgressSummary(), true),
        "[WARN] Assessment completed with tool errors (check config or permissions).",
    );
});

test("buildChildEnvironment strips DATAGUARD_CONNECTION_STRING and connectionEnv under --ide-safe without --allow-env-connection", () => {
    const baseEnv: NodeJS.ProcessEnv = {
        PATH: "/usr/bin",
        DATAGUARD_CONNECTION_STRING: "Server=inherited;Password=secret",
        DataGuard_Connection_String: "Server=mixed-case;Password=secret",
        CUSTOM_DB_CONN: "Server=custom;Password=secret",
    };

    const validateNoAllow = buildChildEnvironment(baseEnv, {
        args: ["validate", "--project", "/workspace", "--ide-safe"],
        connectionEnv: "CUSTOM_DB_CONN",
    });
    assert.ok(validateNoAllow !== undefined);
    assert.equal(validateNoAllow.PATH, "/usr/bin");
    assert.equal("DATAGUARD_CONNECTION_STRING" in validateNoAllow, false);
    assert.equal("DataGuard_Connection_String" in validateNoAllow, false);
    assert.equal("CUSTOM_DB_CONN" in validateNoAllow, false);

    const assessWithStoredCredential = buildChildEnvironment(baseEnv, {
        args: ["assess", "--workspace", "/workspace", "--ide-safe"],
        connectionString: "Server=stored;Password=secret",
        connectionEnv: "custom_db_conn",
    });
    assert.ok(assessWithStoredCredential !== undefined);
    assert.equal("DATAGUARD_CONNECTION_STRING" in assessWithStoredCredential, false);
    assert.equal("CUSTOM_DB_CONN" in assessWithStoredCredential, false);

    const validateWithAllow = buildChildEnvironment(baseEnv, {
        args: ["validate", "--project", "/workspace", "--ide-safe", "--allow-env-connection"],
        connectionString: "Server=stored;Password=secret",
    });
    assert.equal(validateWithAllow?.DATAGUARD_CONNECTION_STRING, "Server=stored;Password=secret");

    const liveWithoutStored = buildChildEnvironment(baseEnv, {
        args: ["snapshot", "refresh", "--provider", "sqlserver"],
    });
    assert.equal(liveWithoutStored, undefined, "live commands without stored secret inherit process.env");
});

function listTypeScriptFiles(directory: string): string[] {
    const files: string[] = [];
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        if (entry.isDirectory()) {
            files.push(...listTypeScriptFiles(full));
        } else if (entry.name.endsWith(".ts")) {
            files.push(full);
        }
    }
    return files;
}

test("no source or README line recommends a NuGet global-tool install of the CLI", () => {
    const extensionRoot = path.resolve(__dirname, "..");
    const candidates = [...listTypeScriptFiles(path.join(extensionRoot, "src")), path.join(extensionRoot, "README.md")];
    assert.ok(candidates.length > 5, "the guard must actually see the sources");
    const guard = /dotnet tool (install|update)/;
    const offenders: string[] = [];
    for (const file of candidates) {
        fs.readFileSync(file, "utf8").split("\n").forEach((line, index) => {
            if (guard.test(line)) {
                offenders.push(`${path.relative(extensionRoot, file)}:${index + 1}`);
            }
        });
    }
    assert.deepEqual(offenders, []);
});
