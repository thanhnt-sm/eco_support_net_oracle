import assert from "node:assert/strict";
import test from "node:test";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { pathToFileURL } from "url";
import { connectionSecretKey, isPathInWorkspaceFolder, readConnectionSecret, redactAndBoundSensitiveText, redactSensitiveText, resolveWorkspaceConfigPath, resolveWorkspaceSarifPath, storeConnectionSecret } from "./security";

test("redactSensitiveText removes connection and bearer credentials", () => {
    const output = redactSensitiveText("Password=s3cret Authorization: Bearer abc.def.ghi api-key: key-value");

    assert.doesNotMatch(output, /s3cret|abc\.def\.ghi|key-value/);
    assert.match(output, /\[REDACTED\]/);
});

test("redactSensitiveText removes URI connection credentials", () => {
    const output = redactSensitiveText("Connecting to postgres://admin:super_secret_password@db.internal:5432/orders");

    assert.doesNotMatch(output, /super_secret_password/);
    assert.match(output, /postgres:\/\/admin:\[REDACTED\]@db\.internal:5432\/orders/);
});

test("redactSensitiveText removes URI credentials for schemes with numbers and special chars", () => {
    const db2 = redactSensitiveText("Connecting to jdbc:db2://dbadmin:mypass123@db2host:50000/sample");
    assert.doesNotMatch(db2, /mypass123/);
    assert.match(db2, /jdbc:db2:\/\/dbadmin:\[REDACTED\]@db2host/);

    const h2 = redactSensitiveText("Connecting to h2://sa:super_secret@localhost:9092/test");
    assert.doesNotMatch(h2, /super_secret/);
    assert.match(h2, /h2:\/\/sa:\[REDACTED\]@localhost/);

    const mongo = redactSensitiveText("Connecting to mongodb+srv://appuser:p@ssword!@cluster.mongodb.net/test");
    assert.doesNotMatch(mongo, /p@ssword!/);
    assert.match(mongo, /mongodb\+srv:\/\/appuser:\[REDACTED\]@cluster/);
});
test("redactSensitiveText removes query string secrets", () => {
    const url = redactSensitiveText("https://api.internal/v1/query?token=secret123&api-key=my_key&format=json");
    assert.doesNotMatch(url, /secret123|my_key/);
    assert.match(url, /token=\[REDACTED\]/);
    assert.match(url, /api-key=\[REDACTED\]/);
});

test("redactSensitiveText handles quoted passwords and passwords with @", () => {
    const quoted = redactSensitiveText('User Id=admin; password="my secret token"; Server=localhost');
    assert.doesNotMatch(quoted, /my secret token/);
    assert.match(quoted, /password=\[REDACTED\]/);

    const braced = redactSensitiveText("Server=tcp:sql.db;Database=app;User Id=sa;Password={my;complex;p@ssword};");
    assert.doesNotMatch(braced, /my;complex;p@ssword/);
    assert.match(braced, /Password=\[REDACTED\];/);

    const unquotedWithSpaces = redactSensitiveText("Server=tcp:sql.db;Database=app;User Id=sa;Password=my secret pass;Server=localhost");
    assert.doesNotMatch(unquotedWithSpaces, /my secret pass/);
    assert.match(unquotedWithSpaces, /Password=\[REDACTED\];Server=localhost/);

    const unquotedAtEol = redactSensitiveText("Server=tcp:sql.db\nPassword=my secret pass\nServer=localhost");
    assert.doesNotMatch(unquotedAtEol, /my secret pass/);
    assert.match(unquotedAtEol, /Password=\[REDACTED\]/);
    const oauth = redactSensitiveText("client_secret=top_secret_val; access_token='jwt_bearer_token'; refresh_token=refresh_123;");
    assert.doesNotMatch(oauth, /top_secret_val/);
    assert.doesNotMatch(oauth, /jwt_bearer_token/);
    assert.doesNotMatch(oauth, /refresh_123/);
    assert.match(oauth, /client_secret=\[REDACTED\]/);
    assert.match(oauth, /access_token=\[REDACTED\]/);
    assert.match(oauth, /refresh_token=\[REDACTED\]/);


    const jsonSecrets = redactSensitiveText('{"password": "secret_pass_123", "api_key": "my-api-key-999"}');
    assert.doesNotMatch(jsonSecrets, /secret_pass_123/);
    assert.doesNotMatch(jsonSecrets, /my-api-key-999/);
    assert.match(jsonSecrets, /password=\[REDACTED\]/);
    assert.match(jsonSecrets, /api_key=\[REDACTED\]/);
    const uriWithAt = redactSensitiveText("postgres://admin:p@ssword@db.internal:5432/orders");
    assert.doesNotMatch(uriWithAt, /p@ssword/);
    assert.match(uriWithAt, /postgres:\/\/admin:\[REDACTED\]@db\.internal:5432\/orders/);
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

test("isPathInWorkspaceFolder returns false when folder list is empty", () => {
    assert.strictEqual(isPathInWorkspaceFolder("/etc/passwd", []), false);
    assert.strictEqual(isPathInWorkspaceFolder("C:\\Windows\\System32\\cmd.exe", []), false);
});

test("isPathInWorkspaceFolder validates relative containment within workspace folders", () => {
    const ws1 = path.resolve("/projects/app");
    const ws2 = path.resolve("/projects/shared");
    const inside1 = path.join(ws1, "src", "Controller.cs");
    const inside2 = path.join(ws2, "models", "User.cs");
    const outside = path.resolve("/etc/passwd");
    const traversal = path.join(ws1, "..", "..", "etc", "passwd");

    assert.strictEqual(isPathInWorkspaceFolder(inside1, [ws1, ws2]), true);
    assert.strictEqual(isPathInWorkspaceFolder(inside2, [ws1, ws2]), true);
    assert.strictEqual(isPathInWorkspaceFolder(outside, [ws1, ws2]), false);
    assert.strictEqual(isPathInWorkspaceFolder(traversal, [ws1, ws2]), false);
});

test("isPathInWorkspaceFolder handles case differences gracefully on case-insensitive platforms", () => {
    const ws = path.resolve("/Projects/App");
    const target = path.resolve("/projects/app/src/Service.cs");
    if (process.platform === "win32" || process.platform === "darwin") {
        assert.strictEqual(isPathInWorkspaceFolder(target, [ws]), true);
    }
});
test("isPathInWorkspaceFolder rejects symlinks pointing outside workspace", () => {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "dg-ws-test-"));
    const outsideDir = fs.mkdtempSync(path.join(os.tmpdir(), "dg-outside-test-"));
    try {
        const outsideFile = path.join(outsideDir, "secret.txt");
        fs.writeFileSync(outsideFile, "secret");
        const linkFile = path.join(tmpDir, "link.txt");
        const linkDir = path.join(tmpDir, "symlink_dir");
        try {
            fs.symlinkSync(outsideFile, linkFile);
            assert.strictEqual(isPathInWorkspaceFolder(linkFile, [tmpDir]), false);
        } catch {
            // Windows unprivileged environments without symlink rights skip
        }

        try {
            fs.symlinkSync(outsideDir, linkDir, "junction");
            const nonExistentTarget = path.join(linkDir, "new_file.cs");
            assert.strictEqual(isPathInWorkspaceFolder(nonExistentTarget, [tmpDir]), false);
        } catch {
            // Windows unprivileged environments without symlink rights skip
        }
    } finally {
        fs.rmSync(tmpDir, { recursive: true, force: true });
        fs.rmSync(outsideDir, { recursive: true, force: true });
    }
});
