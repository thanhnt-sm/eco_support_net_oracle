import assert from "node:assert/strict";
import test from "node:test";
import { buildCliArguments, configArguments, normalizeProvider, resolveExistingConfigPath } from "./command-args";

test("--config is passed only when the workspace config file exists", () => {
    for (const command of ["validate", "snapshot", "baseline"] as const) {
        const withConfig = buildCliArguments(command, "/workspace", "sqlserver", "/workspace/.dataguard.yml", "/tmp/out.sarif");
        const index = withConfig.indexOf("--config");
        assert.ok(index >= 0, `${command} with an existing config must pass --config`);
        assert.equal(withConfig[index + 1], "/workspace/.dataguard.yml");

        const withoutConfig = buildCliArguments(command, "/workspace", "sqlserver", undefined, "/tmp/out.sarif");
        assert.ok(!withoutConfig.includes("--config"), `${command} without a config file must omit --config`);
        assert.ok(withoutConfig.every((arg) => typeof arg === "string"), `${command} must not emit an undefined argv entry`);
    }
    assert.deepEqual(
        buildCliArguments("snapshot", "/workspace", "postgresql", undefined),
        ["snapshot", "refresh", "--provider", "postgresql"],
    );
    assert.deepEqual(
        buildCliArguments("baseline", "/workspace", "oracle", undefined),
        ["baseline", "--provider", "oracle"],
    );
    assert.deepEqual(configArguments(""), []);
    assert.deepEqual(configArguments("/w/.dataguard.yml"), ["--config", "/w/.dataguard.yml"]);
});

test("resolveExistingConfigPath drops a candidate whose file does not exist", async () => {
    const present = new Set(["/workspace/.dataguard.yml"]);
    const exists = async (filePath: string): Promise<boolean> => present.has(filePath);
    assert.equal(await resolveExistingConfigPath("/workspace/.dataguard.yml", exists), "/workspace/.dataguard.yml");
    assert.equal(await resolveExistingConfigPath("/workspace/missing.yml", exists), undefined);
    assert.equal(await resolveExistingConfigPath(undefined, exists), undefined);
    let probed = false;
    assert.equal(await resolveExistingConfigPath("", async () => { probed = true; return true; }), undefined);
    assert.equal(probed, false, "an empty candidate must not be probed");
});

test("CLI argument builder emits positional argv without shell interpolation", () => {
    assert.deepEqual(
        buildCliArguments("validate", "/workspace", " PostgreSQL ", "/workspace/.dataguard.yml", "/tmp/result.sarif"),
        ["validate", "--config", "/workspace/.dataguard.yml", "--provider", "postgresql", "--format", "sarif", "--output", "/tmp/result.sarif", "--project", "/workspace", "--progress", "--ide-safe"],
    );
    assert.deepEqual(
        buildCliArguments("assess", "/workspace with spaces", "mysql", undefined, "/tmp/result.sarif"),
        ["assess", "--workspace", "/workspace with spaces", "--provider", "mysql", "--format", "sarif", "--output", "/tmp/result.sarif", "--ide-safe"],
    );
    assert.deepEqual(
        buildCliArguments("scan", "/workspace", "sqlserver", undefined, "/tmp/summary.json"),
        ["scan", "--project", "/workspace", "--format", "json", "--output", "/tmp/summary.json", "--progress"],
    );
    assert.deepEqual(
        buildCliArguments("verify-shape", "/workspace", "oracle", undefined, "/tmp/verify.json"),
        ["verify-shape", "--project", "/workspace", "--provider", "oracle", "--format", "json", "--output", "/tmp/verify.json"],
    );
});

test("validate and assess always run the CLI in IDE-safe mode", () => {
    for (const command of ["validate", "assess"] as const) {
        const argv = buildCliArguments(command, "/workspace", "sqlserver", "/workspace/.dataguard.yml", "/tmp/out.sarif");
        assert.ok(argv.includes("--ide-safe"), `${command} must pass --ide-safe`);
    }
});

test("provider validation rejects injection-like or unknown values", () => {
    assert.equal(normalizeProvider("ORACLE"), "oracle");
    assert.throws(() => normalizeProvider("mysql; touch /tmp/pwned"), /provider must be/);
});

test("validate carries --allow-env-connection only when the host supplies a user credential", () => {
    const withCredential = buildCliArguments("validate", "/workspace", "sqlserver", "/workspace/.dataguard.yml", "/tmp/out.sarif", { hasUserCredential: true });
    assert.ok(withCredential.includes("--allow-env-connection"), "validate with a SecretStorage credential must pass --allow-env-connection");
    assert.ok(withCredential.indexOf("--allow-env-connection") > withCredential.indexOf("--ide-safe"), "the carve-out flag follows --ide-safe");

    const withoutCredential = buildCliArguments("validate", "/workspace", "sqlserver", "/workspace/.dataguard.yml", "/tmp/out.sarif", { hasUserCredential: false });
    assert.ok(!withoutCredential.includes("--allow-env-connection"), "no credential: the carve-out flag must be absent");

    const defaulted = buildCliArguments("validate", "/workspace", "sqlserver", "/workspace/.dataguard.yml", "/tmp/out.sarif");
    assert.ok(!defaulted.includes("--allow-env-connection"), "the options parameter defaults to no credential");
});

test("assess never carries --allow-env-connection even with a user credential", () => {
    const argv = buildCliArguments("assess", "/workspace", "sqlserver", undefined, "/tmp/out.sarif", { hasUserCredential: true });
    assert.ok(!argv.includes("--allow-env-connection"));
    assert.ok(argv.includes("--ide-safe"));
});

test("snapshot, baseline and verify-shape never carry --ide-safe or --allow-env-connection", () => {
    for (const command of ["snapshot", "baseline", "verify-shape"] as const) {
        const argv = buildCliArguments(command, "/workspace", "sqlserver", "/workspace/.dataguard.yml", "/tmp/out.json", { hasUserCredential: true });
        assert.ok(!argv.includes("--ide-safe"), `${command} is a live-database command and must not claim ide-safe`);
        assert.ok(!argv.includes("--allow-env-connection"), `${command} must not carry the ide-safe carve-out flag`);
    }
});
