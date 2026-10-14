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

const POLICY_LINE_REGEX = /^(?:Warning:|UNEVALUATED:|ACQUISITION:|Rule \S+ not evaluated:)/;

const ACK_FAILURE_MESSAGE = "DataGuard CLI did not confirm IDE-safe mode; results were discarded";

/** Exact starts of the CLI lines that say no ground truth was loaded (only syntactic rules ran). */
export const GROUND_TRUTH_MISSING_PREFIXES = [
    "Warning: no ground truth",
    "UNEVALUATED: no ground truth",
] as const;

/** Explanation of a clean exit whose run compared no ground truth: the database contract was not checked. */
export const SYNTACTIC_ONLY_EXPLANATION =
    "[WARN] Ran syntactic rules only: no snapshot, EF model or database connection was loaded, so the database contract was NOT compared. " +
    "Commit .dataguard-snapshot.json (Tools > DataGuard > Refresh Snapshot) or keep the EF ModelSnapshot.cs in the solution. See Error List for syntactic findings.";

/** Result-block line for a run that compared no ground truth. */
export const GROUND_TRUTH_NOT_LOADED_TEXT = "Ground truth: not loaded (syntactic rules only)";

export interface CliProgressSummary {
    hasSummary: boolean;
    errorCount: number;
    warningCount: number;
    unevaluatedContractCount?: number;
    unavailableRuleCount?: number;
    acquisitionDiagnosticCount?: number;
    groundTruth?: string;
    groundTruthMissing: boolean;
}

export function createCliProgressSummary(): CliProgressSummary {
    return {
        hasSummary: false,
        errorCount: 0,
        warningCount: 0,
        groundTruthMissing: false,
    };
}

/** True when the raw line starts with one of GROUND_TRUTH_MISSING_PREFIXES (ordinal, no leading whitespace). */
export function isGroundTruthMissingLine(rawLine: string): boolean {
    const line = rawLine.replace(/\r$/, "");
    return GROUND_TRUTH_MISSING_PREFIXES.some((prefix) => line.startsWith(prefix));
}

/** True for a CLI policy line (Warning:, UNEVALUATED:, ACQUISITION:, Rule <id> not evaluated:) anchored at line start. */
export function isPolicyLine(rawLine: string): boolean {
    const line = rawLine.replace(/\r$/, "");
    return POLICY_LINE_REGEX.test(line);
}

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

export interface ChildEnvironmentOptions {
    readonly args: readonly string[];
    readonly connectionString?: string;
    readonly connectionEnv?: string;
}

/**
 * Builds the child process environment for a CLI invocation.
 * When running an `--ide-safe` command without `--allow-env-connection`, strips
 * `DATAGUARD_CONNECTION_STRING` and any configured `connectionEnv` variable (case-insensitively)
 * from the child environment and never injects `connectionString`.
 */
export function buildChildEnvironment(
    baseEnv: NodeJS.ProcessEnv,
    options: ChildEnvironmentOptions,
): NodeJS.ProcessEnv | undefined {
    const ideSafe = options.args.includes("--ide-safe");
    const allowEnvConnection = options.args.includes("--allow-env-connection");
    if (ideSafe && !allowEnvConnection) {
        const scrubbed: NodeJS.ProcessEnv = { ...baseEnv };
        const connectionEnvArgIndex = options.args.indexOf("--connection-env");
        const connectionEnvFromArg = connectionEnvArgIndex >= 0 ? options.args[connectionEnvArgIndex + 1]?.trim() : undefined;
        const connectionEnvName = options.connectionEnv?.trim() || connectionEnvFromArg;
        const customUpper = connectionEnvName && connectionEnvName.length > 0 ? connectionEnvName.toUpperCase() : undefined;
        for (const key of Object.keys(scrubbed)) {
            const upper = key.toUpperCase();
            if (upper === "DATAGUARD_CONNECTION_STRING" || (customUpper !== undefined && upper === customUpper)) {
                delete scrubbed[key];
            }
        }
        return scrubbed;
    }
    if (options.connectionString === undefined) {
        return undefined;
    }
    return { ...baseEnv, DATAGUARD_CONNECTION_STRING: options.connectionString };
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
    const unindented = rawLine.replace(/\r$/, "");
    const line = unindented.trim();
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
    if (line.startsWith("ide-safe:") || line.startsWith("baseline:") || isPolicyLine(unindented)) {
        return `[WARN] ${redactSensitiveText(line)}`;
    }
    if (line.startsWith("[INFO]") || line.startsWith("[WARN]") || line.startsWith("[ERROR]")) {
        return redactSensitiveText(line);
    }
    return undefined;
}

/** Updates a run's accumulated progress summary from one raw CLI output line. */
export function recordProgressLine(rawLine: string, summary: CliProgressSummary): void {
    const unindented = rawLine.replace(/\r$/, "");
    if (isGroundTruthMissingLine(unindented)) {
        summary.groundTruthMissing = true;
    }
    const trimmed = unindented.trim();
    if (!trimmed.startsWith("{") || !trimmed.endsWith("}")) {
        return;
    }
    const parsedSummary = parseSummaryEvent(trimmed);
    if (parsedSummary !== undefined) {
        summary.hasSummary = true;
        summary.errorCount = parsedSummary.errorCount;
        summary.warningCount = parsedSummary.warningCount;
        summary.unevaluatedContractCount = parsedSummary.unevaluatedContractCount;
        summary.unavailableRuleCount = parsedSummary.unavailableRuleCount;
        summary.acquisitionDiagnosticCount = parsedSummary.acquisitionDiagnosticCount;
        summary.groundTruth = parsedSummary.groundTruth;
    }
}

/** One-line explanation of the CLI run matching Visual Studio Phase 2 ExitCodeExplainer.Explain. */
export function explainCliExit(
    command: string,
    exitCode: number | null,
    progress: CliProgressSummary,
    sarifExists: boolean,
): string {
    const hasSummary = progress.hasSummary;
    if (hasSummary && !sarifExists && (exitCode === 0 || exitCode === 1)) {
        return "[ERROR] The CLI reported a summary but failed to write results (see [DataGuard CLI] lines)";
    }

    const syntacticOnly = hasSummary && sarifExists && progress.groundTruthMissing && command === "validate";
    if (syntacticOnly && exitCode === 0) {
        return appendUnevaluated(SYNTACTIC_ONLY_EXPLANATION, progress);
    }
    if (syntacticOnly && exitCode === 1) {
        return appendUnevaluated("[WARN] Validation found errors. See Error List. Ground truth was not loaded (syntactic only).", progress);
    }

    if (exitCode === 0 && hasSummary && progress.warningCount > 0) {
        return appendUnevaluated("[WARN] Validation completed with warnings. See Error List.", progress);
    }
    if (exitCode === 0) {
        return appendUnevaluated("[OK] No issues found.", progress);
    }
    if (exitCode === 130) {
        return "[CANCELLED] Cancelled by user.";
    }

    const code = exitCode ?? -1;
    const explanation = command === "validate"
        ? explainValidate(code, hasSummary)
        : (command === "assess" ? explainAssess(code) : explainOtherCommand(command, code));
    return appendUnevaluated(explanation ?? `Unexpected exit code ${exitCode ?? "unknown"}.`, progress);
}

/** Status-bar text after a run: the counts, prefixed with "syntactic only" when no ground truth was compared. */
export function describeCliStatus(progress: CliProgressSummary): string {
    if (!progress.hasSummary) {
        return "DataGuard: Result summary unavailable";
    }
    const counts = `${progress.errorCount} errors, ${progress.warningCount} warnings`;
    return progress.groundTruthMissing
        ? `DataGuard: syntactic only (${counts})`
        : `DataGuard: ${counts}`;
}

/** Result-block line naming the ground truth the CLI compared, or GROUND_TRUTH_NOT_LOADED_TEXT when missing. */
export function describeGroundTruth(progress: CliProgressSummary): string | undefined {
    if (progress.groundTruthMissing) {
        return GROUND_TRUTH_NOT_LOADED_TEXT;
    }
    return progress.groundTruth ? `Ground truth: ${progress.groundTruth}` : undefined;
}

function appendUnevaluated(explanation: string, progress: CliProgressSummary): string {
    if (!progress.hasSummary || !(progress.unevaluatedContractCount !== undefined && progress.unevaluatedContractCount > 0)) {
        return explanation;
    }
    return `${explanation} [WARN] ${progress.unevaluatedContractCount} contract(s) could not be evaluated against the loaded ground truth (see UNEVALUATED lines).`;
}

function explainValidate(exitCode: number, hasSummary: boolean): string | undefined {
    switch (exitCode) {
        case 1:
            return hasSummary
                ? "[WARN] Validation found errors. See Error List."
                : "[ERROR] The CLI failed before producing a validation summary. See the [DataGuard CLI] lines above.";
        case 2:
            return "[ERROR] Invalid arguments or configuration. Check .dataguard.yml and the [DataGuard CLI] lines above.";
        case 3:
            return "[WARN] Validation incomplete: a rule is unavailable for the configured provider or no contract source was found. See the [DataGuard CLI] lines above; previous Error List items were preserved.";
        case 4:
            return "[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above).";
        default:
            return undefined;
    }
}

function explainAssess(exitCode: number): string | undefined {
    switch (exitCode) {
        case 1:
            return "[WARN] Assessment found findings. See Error List.";
        case 2:
            return "[ERROR] Invalid arguments for assess. See the [DataGuard CLI] lines above.";
        case 4:
            return "[WARN] Assessment completed with tool errors (check config or permissions).";
        default:
            return undefined;
    }
}

function explainOtherCommand(command: string, exitCode: number): string | undefined {
    switch (exitCode) {
        case 1:
            return `[ERROR] DataGuard ${command} failed before completing. See the [DataGuard CLI] lines above.`;
        case 2:
            return "[ERROR] Invalid arguments or configuration. Check .dataguard.yml and the [DataGuard CLI] lines above.";
        case 3:
            return "[WARN] Validation incomplete: a rule is unavailable for the configured provider or no contract source was found. See the [DataGuard CLI] lines above; previous Error List items were preserved.";
        case 4:
            return "[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above).";
        default:
            return undefined;
    }
}

interface ParsedSummaryDetails {
    phase: string;
    errorCount: number;
    warningCount: number;
    unevaluatedContractCount?: number;
    unavailableRuleCount?: number;
    acquisitionDiagnosticCount?: number;
    groundTruth?: string;
}

function parseSummaryEvent(line: string): ParsedSummaryDetails | undefined {
    let payload: ProgressEventPayload;
    try {
        const parsed: unknown = JSON.parse(line);
        if (!parsed || typeof parsed !== "object") {
            return undefined;
        }
        payload = parsed as ProgressEventPayload;
    } catch {
        return undefined;
    }
    if (payload.Kind !== "Summary" || typeof payload.Phase !== "string" || !payload.Data || typeof payload.Data !== "object") {
        return undefined;
    }
    const data = payload.Data as Record<string, unknown>;
    const errorCount = readNonNegativeInt(data.ErrorCount);
    const warningCount = readNonNegativeInt(data.WarningCount);
    const criticalCount = readNonNegativeInt(data.CriticalCount);
    const phase = redactSensitiveText(payload.Phase);
    if (
        errorCount === undefined ||
        warningCount === undefined ||
        (phase === "Assessment complete" && criticalCount === undefined)
    ) {
        return undefined;
    }
    const groundTruth = typeof data.GroundTruth === "string" && data.GroundTruth.length > 0
        ? redactSensitiveText(data.GroundTruth)
        : undefined;
    return {
        phase,
        errorCount: errorCount + (criticalCount ?? 0),
        warningCount,
        unevaluatedContractCount: readNonNegativeInt(data.UnevaluatedContractCount),
        unavailableRuleCount: readNonNegativeInt(data.UnavailableRuleCount),
        acquisitionDiagnosticCount: readNonNegativeInt(data.AcquisitionDiagnosticCount),
        groundTruth,
    };
}

function formatSummarySegments(summary: ParsedSummaryDetails): string {
    let text = "";
    if (summary.groundTruth) {
        text += `; ground truth: ${summary.groundTruth}`;
    }
    if (summary.unevaluatedContractCount !== undefined && summary.unevaluatedContractCount > 0) {
        text += `; unevaluated ${summary.unevaluatedContractCount}`;
    }
    if (summary.unavailableRuleCount !== undefined && summary.unavailableRuleCount > 0) {
        text += `; unavailable rules ${summary.unavailableRuleCount}`;
    }
    return text;
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
    if (payload.Kind === "Summary") {
        const summary = parseSummaryEvent(line);
        if (summary !== undefined) {
            return `✔ ${summary.phase}: ${summary.errorCount} errors, ${summary.warningCount} warnings${formatSummarySegments(summary)}`;
        }
    }
    const detail = typeof payload.Detail === "string" ? redactSensitiveText(payload.Detail) : undefined;
    if (payload.Kind === "BaselineApplied") {
        const count = payload.Data && typeof payload.Data === "object"
            ? readNonNegativeInt((payload.Data as Record<string, unknown>).SuppressedCount)
            : undefined;
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

function readNonNegativeInt(value: unknown): number | undefined {
    return typeof value === "number" && Number.isInteger(value) && value >= 0 ? value : undefined;
}
