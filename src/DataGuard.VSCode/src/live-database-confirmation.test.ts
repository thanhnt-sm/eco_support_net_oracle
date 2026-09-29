import assert from "node:assert/strict";
import test from "node:test";
import { buildLiveDatabaseConfirmation, isLiveDatabaseCommand, maskConnectionHost } from "./live-database-confirmation";

test("isLiveDatabaseCommand gates exactly snapshot, baseline and verify-shape", () => {
    assert.equal(isLiveDatabaseCommand("snapshot"), true);
    assert.equal(isLiveDatabaseCommand("baseline"), true);
    assert.equal(isLiveDatabaseCommand("verify-shape"), true);
    assert.equal(isLiveDatabaseCommand("validate"), false);
    assert.equal(isLiveDatabaseCommand("assess"), false);
    assert.equal(isLiveDatabaseCommand("scan"), false);
});

test("maskConnectionHost extracts and masks the host from common connection-string shapes", () => {
    assert.equal(maskConnectionHost("Server=db-prod.corp.example.com,1433;Database=app;User Id=svc;Password=hunter2;"), "db-***.com");
    assert.equal(maskConnectionHost("Data Source=tcp:sql01\\INST;Initial Catalog=app;Integrated Security=true"), "sql***");
    assert.equal(maskConnectionHost("Host=10.20.30.40;Port=5432;Username=u;Password=p"), "10.***.40");
    assert.equal(maskConnectionHost("postgres://user:secretpass@pg.internal.example.org:5432/app"), "p***.org");
    assert.equal(maskConnectionHost("User Id=u;Password=p;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=ora-a.example.net)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=svc)))"), "ora***.net");
    assert.equal(maskConnectionHost("User Id=u;Password=p;Data Source=//oradb.local:1521/svc"), "ora***.local");
    assert.equal(maskConnectionHost("Server=localhost;Database=app"), "loc***");
});

test("maskConnectionHost never leaks credentials and returns undefined when no host is present", () => {
    const masked = maskConnectionHost("Server=db.example.com;User Id=admin;Password=SuperSecret");
    assert.ok(masked !== undefined && !masked.includes("Super") && !masked.includes("admin"));
    const uri = maskConnectionHost("postgres://admin:SuperSecret@host.example.com/app");
    assert.ok(uri !== undefined && !uri.includes("Super") && !uri.includes("admin"));
    assert.equal(maskConnectionHost(undefined), undefined);
    assert.equal(maskConnectionHost("   "), undefined);
    assert.equal(maskConnectionHost("Database=app;User Id=u;Password=p"), undefined);
    assert.equal(maskConnectionHost("Server=;Database=app"), undefined);
});

test("buildLiveDatabaseConfirmation names the masked host and the read-query count for verify-shape", () => {
    const text = buildLiveDatabaseConfirmation({ command: "verify-shape", maskedHost: "db-***.com", readQueryCount: 12 });
    assert.equal(text, "DataGuard will send 12 read SQL queries from the last scan to host db-***.com (your SecretStorage credential) to describe their result shapes. Continue?");
    assert.ok(buildLiveDatabaseConfirmation({ command: "verify-shape", maskedHost: "db-***.com", readQueryCount: 1 }).includes("1 read SQL query from the last scan"));
});

test("buildLiveDatabaseConfirmation never fabricates a count or a host", () => {
    const noScan = buildLiveDatabaseConfirmation({ command: "verify-shape", maskedHost: "db-***.com" });
    assert.ok(noScan.includes("every read SQL query discovered in this workspace"));
    assert.ok(!/\d+ read SQL/.test(noScan));
    const noCredential = buildLiveDatabaseConfirmation({ command: "verify-shape", maskedHost: undefined, readQueryCount: -3 });
    assert.ok(noCredential.includes("no SecretStorage credential is stored"));
    assert.ok(noCredential.includes(".dataguard.yml or DATAGUARD_CONNECTION_STRING"));
    assert.ok(!noCredential.includes("-3"));
});

test("buildLiveDatabaseConfirmation keeps snapshot and baseline wording explicit about the target", () => {
    assert.equal(buildLiveDatabaseConfirmation({ command: "snapshot", maskedHost: "sql***" }), "DataGuard will refresh the schema snapshot from host sql*** (your SecretStorage credential). Continue?");
    assert.ok(buildLiveDatabaseConfirmation({ command: "baseline", maskedHost: undefined }).startsWith("DataGuard will create a baseline using the database configured in"));
});
