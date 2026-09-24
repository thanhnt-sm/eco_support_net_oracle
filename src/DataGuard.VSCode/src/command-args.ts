export type CliCommand = "validate" | "assess" | "snapshot" | "baseline" | "scan" | "verify-shape";

const PROVIDERS = new Set(["sqlserver", "postgresql", "mysql", "oracle"]);

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
            ];
        case "assess":
            return ["assess", "--workspace", workspacePath, "--provider", normalizedProvider, "--format", "sarif", "--output", outputPath!];
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
