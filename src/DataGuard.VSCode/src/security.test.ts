import assert from "node:assert/strict";
import test from "node:test";
import * as path from "path";
import { pathToFileURL } from "url";
import { connectionSecretKey, readConnectionSecret, redactAndBoundSensitiveText, redactSensitiveText, resolveWorkspaceConfigPath, resolveWorkspaceSarifPath, storeConnectionSecret } from "./security";

test("redactSensitiveText removes connection and bearer credentials", () => {
    const output = redactSensitiveText("Password=s3cret Authorization: Bearer abc.def.ghi api-key: key-value");

    assert.doesNotMatch(output, /s3cret|abc\.def\.ghi|key-value/);
    assert.match(output, /\[REDACTED\]/);
});

test("redactAndBoundSensitiveText caps output after redaction", () => {
    const output = redactAndBoundSensitiveText("Password=secret " + "x".repeat(100), 32);

    assert.ok(output.length <= 64);
    assert.doesNotMatch(output, /secret/);
    assert.match(output, /output truncated/);
});

test("resolveWorkspaceSarifPath confines relative and file URI locations", () => {
    const workspace = path.resolve(path.sep, "workspace", "service");
    const inside = path.resolve(workspace, "src", "contract.cs");

    assert.equal(resolveWorkspaceSarifPath(workspace, "src/contract.cs"), inside);
    assert.equal(resolveWorkspaceSarifPath(workspace, pathToFileURL(inside).toString()), inside);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "../outside.cs"), /remain inside/);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "/workspace/service-other/secret.cs"), /remain inside/);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "https://example.test/result.cs"), /relative path or file URI/);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "%E0%A4%A"), /malformed|URI/);
});

test("resolveWorkspaceConfigPath refuses workspace escapes", () => {
    const workspace = path.resolve(path.sep, "workspace", "service");

    assert.equal(resolveWorkspaceConfigPath(workspace, ".dataguard.yml"), path.resolve(workspace, ".dataguard.yml"));
    assert.throws(
        () => resolveWorkspaceConfigPath(workspace, "../outside.yml"),
        /remain inside the trusted workspace folder/,
    );
    assert.throws(
        () => resolveWorkspaceConfigPath(workspace, path.join(path.sep, "etc", "dataguard.yml")),
        /must be relative/,
    );
});

test("connection secrets are workspace-scoped and never persisted as plaintext configuration", async () => {
    const values = new Map<string, string>();
    const operations: string[] = [];
    const secrets = {
        get: async (key: string) => values.get(key),
        store: async (key: string, value: string) => {
            operations.push(`store:${key}`);
            values.set(key, value);
        },
        delete: async (key: string) => {
            operations.push(`delete:${key}`);
            values.delete(key);
        },
    };

    await storeConnectionSecret(secrets, "file:///workspace/payments", "Server=db;Password=not-in-settings;");

    const key = connectionSecretKey("file:///workspace/payments");
    assert.equal(await readConnectionSecret(secrets, "file:///workspace/payments"), "Server=db;Password=not-in-settings;");
    assert.notEqual(key, connectionSecretKey("file:///workspace/reporting"));
    assert.deepEqual(operations, [`store:${key}`]);

    await storeConnectionSecret(secrets, "file:///workspace/payments", "  ");
    assert.equal(await readConnectionSecret(secrets, "file:///workspace/payments"), undefined);
    assert.deepEqual(operations, [`store:${key}`, `delete:${key}`]);
});
