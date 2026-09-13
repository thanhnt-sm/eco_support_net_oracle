import assert from "node:assert/strict";
import test from "node:test";
import * as path from "path";
import { redactAndBoundSensitiveText, redactSensitiveText, resolveWorkspaceConfigPath, resolveWorkspaceSarifPath } from "./security";

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
    const workspace = path.join(path.sep, "workspace", "service");
    const inside = path.join(workspace, "src", "contract.cs");

    assert.equal(resolveWorkspaceSarifPath(workspace, "src/contract.cs"), inside);
    assert.equal(resolveWorkspaceSarifPath(workspace, `file://${inside}`), inside);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "../outside.cs"), /remain inside/);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "/workspace/service-other/secret.cs"), /remain inside/);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "https://example.test/result.cs"), /relative path or file URI/);
    assert.throws(() => resolveWorkspaceSarifPath(workspace, "%E0%A4%A"), /malformed|URI/);
});

test("resolveWorkspaceConfigPath refuses workspace escapes", () => {
    const workspace = path.join(path.sep, "workspace", "service");

    assert.equal(resolveWorkspaceConfigPath(workspace, ".dataguard.yml"), path.join(workspace, ".dataguard.yml"));
    assert.throws(
        () => resolveWorkspaceConfigPath(workspace, "../outside.yml"),
        /remain inside the trusted workspace folder/,
    );
    assert.throws(
        () => resolveWorkspaceConfigPath(workspace, path.join(path.sep, "etc", "dataguard.yml")),
        /must be relative/,
    );
});
