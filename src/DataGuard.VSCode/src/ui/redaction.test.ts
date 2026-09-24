import assert from "node:assert/strict";
import test from "node:test";
import {
    escapeHtml,
    getQuickFixInfo,
    mapSarifLevelToSeverity,
    parseSarifToFindings,
    redactForUi,
    SarifLog
} from "./redaction";

test("redactForUi masks passwords, secrets, and api keys as ***", () => {
    const input = "Failed connection with password=SuperSecret123! and secret: 'TopSecretKey' and api_key=xyz987";
    const redacted = redactForUi(input);

    assert.doesNotMatch(redacted, /SuperSecret123!|TopSecretKey|xyz987/);
    assert.match(redacted, /password=\*\*\*/);
    assert.match(redacted, /secret:\*\*\*/);
    assert.match(redacted, /api_key=\*\*\*/);
});

test("redactForUi masks database connection strings and credentials as ***", () => {
    const connStr = "Data Source=prod-sql;Initial Catalog=CoreDb;User Id=admin;Password=UltraPassword!;Connect Timeout=30;";
    const redacted = redactForUi(connStr);

    assert.doesNotMatch(redacted, /UltraPassword!/);
    assert.match(redacted, /Password=\*\*\*/);
    assert.match(redacted, /User Id=admin/);
    assert.match(redacted, /Data Source=prod-sql/);
});

test("redactForUi masks bearer tokens and URI credentials", () => {
    const bearer = "Request failed: Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
    const uriWithAuth = "Connecting to postgres://dbuser:mypassword123@db.internal:5432/metrics";
    const urlQuery = "https://api.internal/v1/sync?token=secretToken456&mode=full";

    assert.equal(redactForUi(bearer), "Request failed: Authorization: Bearer ***");
    assert.equal(redactForUi(uriWithAuth), "Connecting to postgres://dbuser:***@db.internal:5432/metrics");
    assert.equal(redactForUi(urlQuery), "https://api.internal/v1/sync?token=***&mode=full");
});

test("redactForUi masks URI passwords containing @ correctly", () => {
    const uriWithAtPassword = "Connecting to postgres://dbuser:my@p@ssword!@db.internal:5432/metrics";
    const redacted = redactForUi(uriWithAtPassword);
    assert.equal(redacted, "Connecting to postgres://dbuser:***@db.internal:5432/metrics");
});

test("redactForUi masks OAuth tokens, braced passwords, and quoted passwords", () => {
    const oauth = "client_secret='super-secret-123'; access_token=\"jwt.token.here\"; refresh_token=refresh-xyz";
    const redactedOAuth = redactForUi(oauth);
    assert.doesNotMatch(redactedOAuth, /super-secret-123|jwt\.token\.here|refresh-xyz/);
    assert.match(redactedOAuth, /client_secret=\*\*\*/);
    assert.match(redactedOAuth, /access_token=\*\*\*/);
    assert.match(redactedOAuth, /refresh_token=\*\*\*/);

    const braced = "Server=tcp:sql;User Id=sa;Password={my;complex;p@ssword};";
    const redactedBraced = redactForUi(braced);
    assert.doesNotMatch(redactedBraced, /my;complex;p@ssword/);
    assert.match(redactedBraced, /Password=\*\*\*/);
});

test("redactForUi masks connection string passwords containing ampersands", () => {
    const connStr = "Server=localhost;User Id=sa;Password=foo&bar_123!;Database=app;";
    const redacted = redactForUi(connStr);
    assert.doesNotMatch(redacted, /foo&bar_123!/);
    assert.match(redacted, /Password=\*\*\*/);
    assert.match(redacted, /Database=app/);
});

test("redactForUi resists catastrophic backtracking on large non-delimited payloads", () => {
    const largePayload = "password = " + "a ".repeat(10000);
    const startTime = Date.now();
    const redacted = redactForUi(largePayload);
    const duration = Date.now() - startTime;

    assert.ok(duration < 100, `Redaction took ${duration}ms, expected < 100ms`);
    assert.match(redacted, /password\s*=\s*\*\*\*/i);
});

test("escapeHtml prevents XSS injection", () => {
    const malicious = '<script>alert("XSS")</script><img src=x onerror="alert(1)">';
    const escaped = escapeHtml(malicious);

    assert.doesNotMatch(escaped, /<script>/);
    assert.doesNotMatch(escaped, /<img/);
    assert.doesNotMatch(escaped, /"/);
    assert.match(escaped, /&lt;script&gt;/);
    assert.match(escaped, /&lt;img src=x onerror=&quot;alert\(1\)&quot;&gt;/);
});

test("parseSarifToFindings parses standardized SARIF without database connection logic", () => {
    const mockSarif: SarifLog = {
        runs: [
            {
                results: [
                    {
                        ruleId: "DG001",
                        level: "error",
                        message: {
                            text: "Parameter count mismatch. Password=unredactedPass found."
                        },
                        locations: [
                            {
                                physicalLocation: {
                                    artifactLocation: { uri: "src/Data/OrderRepository.cs" },
                                    region: {
                                        startLine: 42,
                                        startColumn: 10,
                                        endLine: 42,
                                        endColumn: 35,
                                        snippet: { text: "cmd.Parameters.AddWithValue(\"@Secret\", pwd=secret123)" }
                                    }
                                }
                            }
                        ]
                    }
                ]
            }
        ]
    };

    const findings = parseSarifToFindings(
        mockSarif,
        "/workspace",
        (root, uri) => `${root}/${uri}`
    );

    assert.equal(findings.length, 1);
    const item = findings[0];
    assert.equal(item.ruleId, "DG001");
    assert.equal(item.severity, "error");
    assert.equal(item.startLine, 42);
    assert.equal(item.quickFixAvailable, true);
    assert.match(item.quickFixTitle!, /Synchronize parameter count/);

    // Verify message and snippet redaction
    assert.doesNotMatch(item.message, /unredactedPass/);
    assert.match(item.message, /Password=\*\*\*/);
    assert.doesNotMatch(item.rawSnippet!, /secret123/);
    assert.match(item.rawSnippet!, /pwd=\*\*\*/);
});

test("mapSarifLevelToSeverity and getQuickFixInfo return valid contract models", () => {
    assert.equal(mapSarifLevelToSeverity("error"), "error");
    assert.equal(mapSarifLevelToSeverity("warning"), "warning");
    assert.equal(mapSarifLevelToSeverity("note"), "information");
    assert.equal(mapSarifLevelToSeverity("none"), "information");

    assert.equal(getQuickFixInfo("DG001").available, true);
    assert.equal(getQuickFixInfo("DG002").available, true);
    assert.equal(getQuickFixInfo("DG017").available, true);
    assert.equal(getQuickFixInfo("DG018").available, true);
    assert.equal(getQuickFixInfo("UNKNOWN").available, false);
});
