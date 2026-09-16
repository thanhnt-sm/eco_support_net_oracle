import * as path from "path";
import { fileURLToPath } from "url";
const SENSITIVE_ASSIGNMENT = /\b(password|pwd|secret|token|api[_ -]?key|connection\s*string)\s*[:=]\s*(?:bearer\s+)?[^\s;,]+/gi;
const AUTHORIZATION_BEARER = /\bauthorization\s*:\s*bearer\s+[^\s,;]+/gi;

export function redactSensitiveText(value: string): string {
    return value
        .replace(SENSITIVE_ASSIGNMENT, "$1=[REDACTED]")
        .replace(AUTHORIZATION_BEARER, "Authorization: Bearer [REDACTED]");
}

/** Redacts CLI text and caps its size before it reaches an IDE surface. */
export function redactAndBoundSensitiveText(value: string, maximumLength = 16 * 1024): string {
    if (!Number.isInteger(maximumLength) || maximumLength < 1) {
        throw new Error("maximumLength must be a positive integer.");
    }
    const redacted = redactSensitiveText(value);
    return redacted.length <= maximumLength
        ? redacted
        : `${redacted.slice(0, maximumLength)}\n[DataGuard] output truncated.`;
}

export interface ConnectionSecretStorage {
    get(key: string): PromiseLike<string | undefined>;
    store(key: string, value: string): PromiseLike<void>;
    delete(key: string): PromiseLike<void>;
}

const CONNECTION_SECRET_PREFIX = "dataguard.connectionString.";

/** Returns an opaque, workspace-scoped key for VS Code's encrypted SecretStorage. */
export function connectionSecretKey(workspaceIdentity: string): string {
    if (workspaceIdentity.length === 0) {
        throw new Error("workspaceIdentity must not be empty.");
    }

    return `${CONNECTION_SECRET_PREFIX}${Buffer.from(workspaceIdentity).toString("base64url")}`;
}

/** Reads an optional connection string from VS Code's encrypted secret store. */
export async function readConnectionSecret(
    secrets: Pick<ConnectionSecretStorage, "get">,
    workspaceIdentity: string,
): Promise<string | undefined> {
    return secrets.get(connectionSecretKey(workspaceIdentity));
}

/** Stores a connection string only in VS Code's encrypted secret store. */
export async function storeConnectionSecret(
    secrets: Pick<ConnectionSecretStorage, "store" | "delete">,
    workspaceIdentity: string,
    connectionString: string,
): Promise<void> {
    const key = connectionSecretKey(workspaceIdentity);
    if (connectionString.trim().length === 0) {
        await secrets.delete(key);
        return;
    }

    await secrets.store(key, connectionString);
}

export function resolveWorkspaceConfigPath(workspacePath: string, configuredPath: string): string {
    if (path.isAbsolute(configuredPath)) {
        throw new Error("dataguard.configPath must be relative to the trusted workspace folder.");
    }

    const resolved = path.resolve(workspacePath, configuredPath);
    const relative = path.relative(workspacePath, resolved);
    if (relative === "" || (!relative.startsWith(".." + path.sep) && relative !== ".." && !path.isAbsolute(relative))) {
        return resolved;
    }

    throw new Error("dataguard.configPath must remain inside the trusted workspace folder.");
}

/** Resolves a SARIF artifact URI only when it remains inside the workspace. */
export function resolveWorkspaceSarifPath(workspacePath: string, artifactUri: string): string {
    let candidate = artifactUri;
    if (/^[a-z][a-z0-9+.-]*:/i.test(artifactUri)) {
        let parsed: URL;
        try {
            parsed = new URL(artifactUri);
        } catch {
            throw new Error("SARIF artifact URI is malformed.");
        }
        if (parsed.protocol !== "file:") {
            throw new Error("SARIF artifact URI must be a relative path or file URI inside the workspace.");
        }
        candidate = fileURLToPath(parsed);
    }

    try {
        candidate = decodeURIComponent(candidate);
    } catch {
        throw new Error("SARIF artifact URI is malformed.");
    }

    const resolved = path.isAbsolute(candidate)
        ? path.resolve(candidate)
        : path.resolve(workspacePath, candidate);
    const relative = path.relative(workspacePath, resolved);
    if (relative !== "" && (relative === ".." || relative.startsWith(".." + path.sep) || path.isAbsolute(relative))) {
        throw new Error("SARIF artifact location must remain inside the trusted workspace folder.");
    }

    return resolved;
}
