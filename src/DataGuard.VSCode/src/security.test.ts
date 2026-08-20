import assert from "node:assert/strict";
import test from "node:test";
import * as path from "path";
import { redactSensitiveText, resolveWorkspaceConfigPath } from "./security";

test("redactSensitiveText removes connection and bearer credentials", () => {
    const output = redactSensitiveText("Password=s3cret Authorization: Bearer abc.def.ghi api-key: key-value");

    assert.doesNotMatch(output, /s3cret|abc\.def\.ghi|key-value/);
    assert.match(output, /\[REDACTED\]/);
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
