import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";
const SENSITIVE_ASSIGNMENT = /(?:"|'|(?<![?&])\b)(password|pwd|secret|token|api[_ -]?key|client[_ -]?secret|access[_ -]?token|refresh[_ -]?token|connection\s*string)(?:"|'|\b)\s*[:=]\s*(?:bearer\s+)?(?:"[^"]*"|'[^']*'|\{[^}]*\}|[^;\r\n,\s]+(?:\s+[^;\r\n,\s]+)*(?=\s*(?:[;,]|\r?\n))|[^;\r\n,\s]+)/gi;
const AUTHORIZATION_BEARER = /\bauthorization\s*:\s*bearer\s+[^\s,;]+/gi;
const URI_CREDENTIALS = /([a-z0-9+.-]+:\/\/[^\/\s:]+:)([^/\s]+)(@)/gi;
const QUERY_PARAM_SECRET = /([?&](?:password|pwd|secret|token|api[_ -]?key)=)[^&#\s]+/gi;

export function redactSensitiveText(value: string): string {
    return value
        .replace(SENSITIVE_ASSIGNMENT, "$1=[REDACTED]")
        .replace(AUTHORIZATION_BEARER, "Authorization: Bearer [REDACTED]")
        .replace(URI_CREDENTIALS, "$1[REDACTED]$3")
        .replace(QUERY_PARAM_SECRET, "$1[REDACTED]");
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

/** Validates that a target file path resides strictly inside at least one of the workspace folders. */
export function isPathInWorkspaceFolder(targetPath: string, folderPaths: readonly string[]): boolean {
    if (!folderPaths || folderPaths.length === 0) {
        return false;
    }
    const resolvedPath = path.resolve(targetPath);
    const isCaseInsensitiveFs = process.platform === "win32" || process.platform === "darwin";
    return folderPaths.some((folderPath) => {
        const resolvedFolder = path.resolve(folderPath);
        const base = isCaseInsensitiveFs ? resolvedFolder.toLowerCase() : resolvedFolder;
        const target = isCaseInsensitiveFs ? resolvedPath.toLowerCase() : resolvedPath;
        const rel = path.relative(base, target);
        const isOutside = rel === ".." || rel.startsWith(".." + path.sep) || rel.startsWith("../") || rel.startsWith("..\\");
        if (isOutside || path.isAbsolute(rel)) {
            return false;
        }

        try {
            if (fs.existsSync(resolvedFolder)) {
                const realFolder = fs.realpathSync(resolvedFolder);
                const realBase = isCaseInsensitiveFs ? realFolder.toLowerCase() : realFolder;

                let current = resolvedPath;
                let remainder = "";
                while (!fs.existsSync(current)) {
                    const parent = path.dirname(current);
                    if (parent === current) {
                        break;
                    }
                    remainder = remainder ? path.join(path.basename(current), remainder) : path.basename(current);
                    current = parent;
                }

                if (fs.existsSync(current)) {
                    const realAncestor = fs.realpathSync(current);
                    const realTarget = remainder ? path.join(realAncestor, remainder) : realAncestor;
                    const realTgt = isCaseInsensitiveFs ? realTarget.toLowerCase() : realTarget;
                    const realRel = path.relative(realBase, realTgt);
                    const realOutside = realRel === ".." || realRel.startsWith(".." + path.sep) || realRel.startsWith("../") || realRel.startsWith("..\\");
                    if (realOutside || path.isAbsolute(realRel)) {
                        return false;
                    }
                }
            }
        } catch {
            return false;
        }
        return true;
    });
}
