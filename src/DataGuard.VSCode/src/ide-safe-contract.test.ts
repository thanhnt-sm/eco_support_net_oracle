import assert from "node:assert/strict";
import * as fs from "fs";
import * as path from "path";
import test from "node:test";
import { buildIdeSafeFailureMessage, formatProgressLine, hasIdeSafeAck, isOldCliRejection, MIN_CLI_VERSION } from "./ide-safe-contract";

const ACK = "ide-safe: active";
const OLD_CLI_LINE = "Unrecognized command or argument '--ide-safe'.";

test("hasIdeSafeAck accepts only an exact ack as the first non-empty stderr line", () => {
    assert.equal(hasIdeSafeAck([ACK]), true);
    assert.equal(hasIdeSafeAck(["", "   ", `${ACK}\r`, "ide-safe: suppressed ConnectionString from config"]), true, "blank lines and CR are ignored");
    assert.equal(hasIdeSafeAck([]), false);
    assert.equal(hasIdeSafeAck(["ide-safe: suppressed X", ACK]), false, "the ack must come first");
    assert.equal(hasIdeSafeAck(["ide-safe: active!"]), false, "no prefix match");
    assert.equal(hasIdeSafeAck(["IDE-SAFE: ACTIVE"]), false, "exact case");
    assert.equal(hasIdeSafeAck([`{"Kind":"PhaseStarted","Detail":"${ACK}"}`]), false, "a progress event is not the ack");
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
    assert.equal(buildIdeSafeFailureMessage([]), base);
    assert.equal(buildIdeSafeFailureMessage(["Validation failed: boom"]), base);
    const withHint = buildIdeSafeFailureMessage([OLD_CLI_LINE, "Some other line"]);
    assert.ok(withHint.startsWith(base));
    assert.ok(withHint.endsWith(`Update the dataguard CLI (${MIN_CLI_VERSION} or later) or set dataguard.cliPath`));
    assert.ok(!buildIdeSafeFailureMessage(["", OLD_CLI_LINE]).includes("Update"), "only the first line is consulted for the hint");
});

test("formatProgressLine surfaces ide-safe: and baseline: lines as [WARN] channel lines", () => {
    assert.equal(
        formatProgressLine("ide-safe: suppressed ConnectionString from config (database access disabled)"),
        "[WARN] ide-safe: suppressed ConnectionString from config (database access disabled)",
    );
    assert.equal(formatProgressLine("ide-safe: active"), "[WARN] ide-safe: active");
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
