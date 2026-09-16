/**
 * Security redaction and HTML escaping utilities for DataGuard IDE extensions.
 * Enforces masking of connection strings, credentials, and secrets as '***' before UI rendering.
 * Purely consumes standardized outputs; zero SQL parsing or DB connection logic.
 */

// Matches connection string credentials and secret key-value assignments
const SENSITIVE_KV_REGEX = /\b(password|pwd|secret|token|api[_ -]?key|credentials?)\s*([:=])\s*(?:["']?)(?:bearer\s+)?[^\s;,"'&]+(?:["']?)/gi;

// Matches Bearer tokens in Authorization headers
const AUTHORIZATION_BEARER_REGEX = /\b(authorization\s*:\s*bearer\s+)[^\s,;]+/gi;

// Matches URI user:password@ credentials
const URI_CREDENTIAL_REGEX = /([a-z]+:\/\/[^/:]+:)[^@]+(@)/gi;

// Matches query parameters containing secrets
const QUERY_PARAM_SECRET_REGEX = /([?&](?:password|pwd|secret|token|api[_ -]?key)=)[^&#\s]+/gi;

/**
 * Strictly masks connection strings, passwords, tokens, and secrets as '***'
 * for safe display in extension UI (Webviews, TreeViews, and notifications).
 */
export function redactForUi(value: string): string {
    if (!value) {
        return "";
    }

    return value
        .replace(SENSITIVE_KV_REGEX, "$1$2***")
        .replace(AUTHORIZATION_BEARER_REGEX, "$1***")
        .replace(URI_CREDENTIAL_REGEX, "$1***$2")
        .replace(QUERY_PARAM_SECRET_REGEX, "$1***");
}

/**
 * Escapes unsafe characters for safe inclusion in HTML templates, preventing XSS.
 */
export function escapeHtml(unsafe: string): string {
    if (!unsafe) {
        return "";
    }
    return unsafe
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

export type FindingSeverity = "error" | "warning" | "information";

export interface FindingItem {
    id: string;
    ruleId: string;
    message: string;
    severity: FindingSeverity;
    filePath: string;
    fullUri: string;
    startLine: number;
    startColumn: number;
    endLine: number;
    endColumn: number;
    quickFixAvailable: boolean;
    quickFixTitle?: string;
    rawSnippet?: string;
}

export interface SarifRegion {
    startLine?: number;
    startColumn?: number;
    endLine?: number;
    endColumn?: number;
    snippet?: {
        text?: string;
    };
}

export interface SarifArtifactLocation {
    uri?: string;
}

export interface SarifPhysicalLocation {
    artifactLocation?: SarifArtifactLocation;
    region?: SarifRegion;
}

export interface SarifLocation {
    physicalLocation?: SarifPhysicalLocation;
}

export interface SarifResult {
    ruleId?: string;
    level?: "error" | "warning" | "note" | "none";
    message?: {
        text?: string;
    };
    locations?: SarifLocation[];
}

export interface SarifRun {
    results?: SarifResult[];
}

export interface SarifLog {
    runs?: SarifRun[];
}

/**
 * Maps SARIF severity level to UI FindingSeverity.
 */
export function mapSarifLevelToSeverity(level?: string): FindingSeverity {
    switch (level) {
        case "error":
            return "error";
        case "note":
        case "none":
            return "information";
        case "warning":
        default:
            return "warning";
    }
}

/**
 * Determines whether an automated Quick-Fix is available for a given rule ID.
 */
export function getQuickFixInfo(ruleId: string): { available: boolean; title?: string } {
    switch (ruleId) {
        case "DG001":
            return { available: true, title: "DataGuard: Synchronize parameter count with SQL definition" };
        case "DG002":
            return { available: true, title: "DataGuard: Align C# parameter types with database column types" };
        case "DG017":
            return { available: true, title: "DataGuard: Replace SELECT * with explicit column projection" };
        case "DG018":
            return { available: true, title: "DataGuard: Update entity contract to match schema snapshot" };
        default:
            return { available: false };
    }
}

/**
 * Parses SARIF log into standardized, sanitized, and redacted FindingItems.
 * Contains ZERO database connection or SQL parsing logic.
 */
export function parseSarifToFindings(
    sarif: SarifLog,
    workspaceRoot: string,
    resolvePath: (root: string, uri: string) => string
): FindingItem[] {
    const findings: FindingItem[] = [];
    const runs = sarif.runs ?? [];

    for (const run of runs) {
        const results = run.results ?? [];
        for (let i = 0; i < results.length; i++) {
            const result = results[i];
            const physical = result.locations?.[0]?.physicalLocation;
            const rawUri = physical?.artifactLocation?.uri;
            if (!rawUri) {
                continue;
            }

            let resolvedFilePath: string;
            try {
                resolvedFilePath = resolvePath(workspaceRoot, rawUri);
            } catch {
                continue;
            }

            const ruleId = result.ruleId ?? "DG000";
            const severity = mapSarifLevelToSeverity(result.level);
            const rawMessage = result.message?.text ?? "Database contract drift detected";
            const redactedMessage = redactForUi(rawMessage);

            const region = physical?.region;
            const startLine = Math.max(1, region?.startLine ?? 1);
            const startColumn = Math.max(1, region?.startColumn ?? 1);
            const endLine = Math.max(startLine, region?.endLine ?? startLine);
            const endColumn = Math.max(startColumn, region?.endColumn ?? startColumn);

            const rawSnippet = region?.snippet?.text ? redactForUi(region.snippet.text) : undefined;
            const qfInfo = getQuickFixInfo(ruleId);

            findings.push({
                id: `finding-${findings.length + 1}-${ruleId}`,
                ruleId,
                message: redactedMessage,
                severity,
                filePath: resolvedFilePath,
                fullUri: rawUri,
                startLine,
                startColumn,
                endLine,
                endColumn,
                quickFixAvailable: qfInfo.available,
                quickFixTitle: qfInfo.title,
                rawSnippet
            });
        }
    }

    return findings;
}
