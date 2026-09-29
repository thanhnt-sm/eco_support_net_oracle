import assert from "node:assert/strict";
import test from "node:test";
import { buildCliArguments, normalizeProvider } from "./command-args";

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
