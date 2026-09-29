export type CliCommand = "validate" | "assess" | "snapshot" | "baseline" | "scan" | "verify-shape";

const PROVIDERS = new Set(["sqlserver", "postgresql", "mysql", "oracle"]);

/**
 * IDE-safe mode: the CLI ignores every repository-controlled setting that could load an assembly
 * (GroundTruthMode: Manual / ManualAssemblyPath), open a database, secret-manager or network
 * connection, or write to a repo-chosen path. Workspace trust gates whether we run at all; this flag
 * bounds what a run can do even in a trusted workspace whose .dataguard.yml was tampered with.
 */
export const IDE_SAFE_FLAG = "--ide-safe";

/**
 * Carve-out for the user's own credential: under --ide-safe the CLI keeps ONLY a connection string
 * supplied through the DATAGUARD_CONNECTION_STRING environment variable (which this extension sets
 * from VS Code SecretStorage). Repository config credentials stay stripped. Validate only.
 */
export const ALLOW_ENV_CONNECTION_FLAG = "--allow-env-connection";

export interface CliArgumentOptions {
    /** True only when VS Code SecretStorage returned a connection string for this workspace. */
    readonly hasUserCredential?: boolean;
}

export function normalizeProvider(value: string): string {
    const provider = value.trim().toLowerCase();
    if (!PROVIDERS.has(provider)) {
        throw new Error("provider must be sqlserver, postgresql, mysql, or oracle.");
    }
    return provider;
}

export function buildCliArguments(
    command: CliCommand,
    workspacePath: string,
    provider: string,
    configPath?: string,
    outputPath?: string,
    options: CliArgumentOptions = {},
): string[] {
    const normalizedProvider = normalizeProvider(provider);
    switch (command) {
        case "validate":
            return [
                "validate",
                "--config",
                configPath!,
                "--provider",
                normalizedProvider,
                "--format",
                "sarif",
                "--output",
                outputPath!,
                "--project",
                workspacePath,
                "--progress",
                IDE_SAFE_FLAG,
                ...(options.hasUserCredential === true ? [ALLOW_ENV_CONNECTION_FLAG] : []),
            ];
        case "assess":
            return ["assess", "--workspace", workspacePath, "--provider", normalizedProvider, "--format", "sarif", "--output", outputPath!, IDE_SAFE_FLAG];
        case "snapshot":
            return ["snapshot", "refresh", "--config", configPath!, "--provider", normalizedProvider];
        case "baseline":
            return ["baseline", "--config", configPath!, "--provider", normalizedProvider];
        case "scan":
            return [
                "scan",
                "--project",
                workspacePath,
                "--format",
                "json",
                "--output",
                outputPath!,
                "--progress",
            ];
        case "verify-shape":
            return [
                "verify-shape",
                "--project",
                workspacePath,
                "--provider",
                normalizedProvider,
                "--format",
                "json",
                "--output",
                outputPath!,
            ];
    }
}
