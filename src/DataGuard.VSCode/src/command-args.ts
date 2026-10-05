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

/**
 * Returns the `--config <path>` pair only when a config path was resolved for a file that exists.
 * The CLI treats an explicit `--config` that points at a missing file as a usage error (exit 2), so
 * a workspace without `.dataguard.yml` must not pass the flag at all and lets the CLI use defaults.
 */
export function configArguments(configPath: string | undefined): string[] {
    return configPath === undefined || configPath.length === 0 ? [] : ["--config", configPath];
}

/**
 * Resolves the config path to pass to the CLI: the candidate when `exists(candidate)` is true,
 * otherwise `undefined` (so {@link configArguments} omits the flag).
 */
export async function resolveExistingConfigPath(
    candidate: string | undefined,
    exists: (filePath: string) => Promise<boolean>,
): Promise<string | undefined> {
    if (candidate === undefined || candidate.length === 0) {
        return undefined;
    }
    return (await exists(candidate)) ? candidate : undefined;
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
                ...configArguments(configPath),
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
            return ["snapshot", "refresh", ...configArguments(configPath), "--provider", normalizedProvider];
        case "baseline":
            return ["baseline", ...configArguments(configPath), "--provider", normalizedProvider];
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
