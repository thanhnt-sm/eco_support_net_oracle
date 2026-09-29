import { redactSensitiveText } from "./security";

/**
 * Host-side half of the CLI `--ide-safe` contract (see DataGuard.Cli/IdeSafePolicy.cs).
 * Pure functions only: no vscode import, so every rule here is unit-testable.
 */

/** First CLI release that prints the ack line and accepts `--allow-env-connection`. */
export const MIN_CLI_VERSION = "0.3.0";

/** Exact first stderr line the CLI prints under `--ide-safe` (validate and assess). */
export const IDE_SAFE_ACK_LINE = "ide-safe: active";

/**
 * System.CommandLine rejection an older CLI prints for a flag it does not know. Anchored at the
 * line start so a path or message that merely quotes the flag mid-line never counts as "old CLI".
 * `--allow-env-connection` is included because the intermediate PR #24 CLI knows `--ide-safe` but
 * not the carve-out flag; the remedy (update the CLI) is the same.
 */
const OLD_CLI_REJECTION = /^Unrecognized command or argument '--(?:ide-safe|allow-env-connection)'/;

const ACK_FAILURE_MESSAGE = "DataGuard CLI did not confirm IDE-safe mode; results were discarded";

/** True only when the given (first non-empty) stderr line is exactly the ack line. */
export function hasIdeSafeAck(firstLine: string | undefined): boolean {
    return firstLine !== undefined && firstLine.trim() === IDE_SAFE_ACK_LINE;
}

/** True when the given (first) stderr line is the unknown-option rejection of an old CLI. */
export function isOldCliRejection(firstLine: string | undefined): boolean {
    return firstLine !== undefined && OLD_CLI_REJECTION.test(firstLine.trim());
}

/** Error-notification text when a validate/assess run never acknowledged `--ide-safe`. */
export function buildIdeSafeFailureMessage(firstLine: string | undefined): string {
    if (isOldCliRejection(firstLine)) {
        return `${ACK_FAILURE_MESSAGE}. Update the dataguard CLI (${MIN_CLI_VERSION} or later) or set dataguard.cliPath`;
    }
    return ACK_FAILURE_MESSAGE;
}

interface ProgressEventPayload {
    Kind?: unknown;
    Phase?: unknown;
    Detail?: unknown;
    Data?: unknown;
}

/**
 * Turns one CLI stdout/stderr line into the text for the DataGuard output channel, or `undefined`
 * when the line must be dropped. `ide-safe:` and `baseline:` lines are policy decisions the user
 * must see, so they are echoed as `[WARN]` regardless of exit code; the exact acknowledgement
 * `ide-safe: active` is the expected outcome of every run and is echoed as `[INFO]`. Everything
 * is redacted.
 */
export function formatProgressLine(rawLine: string): string | undefined {
    const line = rawLine.trim();
    if (line.length === 0) {
        return undefined;
    }
    if (line.startsWith("{") && line.endsWith("}")) {
        const rendered = formatProgressEvent(line);
        if (rendered !== null) {
            return rendered;
        }
    }
    if (line === IDE_SAFE_ACK_LINE) {
        return `[INFO] ${line}`;
    }
    if (line.startsWith("ide-safe:") || line.startsWith("baseline:")) {
        return `[WARN] ${redactSensitiveText(line)}`;
    }
    if (line.startsWith("[INFO]") || line.startsWith("[WARN]") || line.startsWith("[ERROR]")) {
        return redactSensitiveText(line);
    }
    return undefined;
}

/** `null` when the line is not a progress event (caller falls through to plain-line handling). */
function formatProgressEvent(line: string): string | undefined | null {
    let payload: ProgressEventPayload;
    try {
        const parsed: unknown = JSON.parse(line);
        if (!parsed || typeof parsed !== "object") {
            return null;
        }
        payload = parsed as ProgressEventPayload;
    } catch {
        return null;
    }
    if (typeof payload.Kind !== "string") {
        return null;
    }
    const detail = typeof payload.Detail === "string" ? redactSensitiveText(payload.Detail) : undefined;
    if (payload.Kind === "BaselineApplied") {
        const count = readSuppressedCount(payload.Data);
        const target = detail ?? "baseline";
        return count === undefined
            ? `[WARN] baseline: violations suppressed by ${target}`
            : `[WARN] baseline: ${count} suppressed by ${target}`;
    }
    if (detail !== undefined) {
        return detail;
    }
    return typeof payload.Phase === "string" ? redactSensitiveText(payload.Phase) : undefined;
}

/** External CLI data: only a non-negative integer count is ever echoed. */
function readSuppressedCount(data: unknown): number | undefined {
    if (!data || typeof data !== "object") {
        return undefined;
    }
    const value = (data as Record<string, unknown>).SuppressedCount;
    return typeof value === "number" && Number.isInteger(value) && value >= 0 ? value : undefined;
}
