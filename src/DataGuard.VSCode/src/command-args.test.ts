import assert from "node:assert/strict";
import test from "node:test";
import { buildCliArguments, normalizeProvider } from "./command-args";

test("CLI argument builder emits positional argv without shell interpolation", () => {
    assert.deepEqual(
        buildCliArguments("validate", "/workspace", " PostgreSQL ", "/workspace/.dataguard.yml", "/tmp/result.sarif"),
        ["validate", "--config", "/workspace/.dataguard.yml", "--provider", "postgresql", "--format", "sarif", "--output", "/tmp/result.sarif"],
    );
    assert.deepEqual(
        buildCliArguments("assess", "/workspace with spaces", "mysql", undefined, "/tmp/result.sarif"),
        ["assess", "--workspace", "/workspace with spaces", "--provider", "mysql", "--format", "sarif", "--output", "/tmp/result.sarif"],
    );
});

test("provider validation rejects injection-like or unknown values", () => {
    assert.equal(normalizeProvider("ORACLE"), "oracle");
    assert.throws(() => normalizeProvider("mysql; touch /tmp/pwned"), /provider must be/);
});
