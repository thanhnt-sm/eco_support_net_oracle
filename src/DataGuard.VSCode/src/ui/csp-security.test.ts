import assert from "node:assert/strict";
import test from "node:test";
import { buildWebviewCsp, renderDashboardHtml } from "./dashboard-view";
import { FindingItem } from "./redaction";

test("buildWebviewCsp enforces default-src 'none' and script-src with nonce", () => {
    const nonce = "test-crypto-nonce-12345";
    const csp = buildWebviewCsp(nonce);

    assert.match(csp, /default-src 'none'/);
    assert.match(csp, /style-src 'unsafe-inline'/);
    assert.match(csp, /script-src 'nonce-test-crypto-nonce-12345'/);
    assert.doesNotMatch(csp, /script-src 'unsafe-inline'/);
    assert.doesNotMatch(csp, /script-src 'unsafe-eval'/);
});

test("renderDashboardHtml embeds CSP meta tag matching script nonce", () => {
    const nonce = "sampleNonceAbc123==";
    const findings: FindingItem[] = [];
    const html = renderDashboardHtml(findings, nonce);

    // Verify CSP meta tag exists with the exact nonce
    assert.match(html, new RegExp(`<meta http-equiv="Content-Security-Policy" content="[^"]*script-src 'nonce-${nonce.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}'`));

    // Verify script tag uses the exact nonce
    assert.match(html, new RegExp(`<script nonce="${nonce.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}">`));

    // Verify no un-nonced script tags exist
    const unnoncedScripts = html.match(/<script(?![^>]*nonce=)[^>]*>/gi);
    assert.equal(unnoncedScripts, null);
});

test("renderDashboardHtml neutralizes XSS payloads and alert(1) injection attempts", () => {
    const nonce = "safeNonce123";
    const maliciousFindings: FindingItem[] = [
        {
            id: 'finding-xss-1',
            ruleId: 'DG001',
            message: 'Parameter mismatch in <script>alert("xss")</script> and <img src=x onerror=alert(1)>',
            severity: 'error',
            filePath: 'src/Data/User<svg/onload=alert(1)>.cs',
            fullUri: 'file:///workspace/src/Data/User.cs',
            startLine: 1,
            startColumn: 1,
            endLine: 1,
            endColumn: 10,
            quickFixAvailable: true,
            quickFixTitle: 'Fix with <script>alert("fix")</script>',
            rawSnippet: 'SELECT * FROM Users WHERE token = "<script>alert(1)</script>"'
        }
    ];

    const html = renderDashboardHtml(maliciousFindings, nonce);

    // Unescaped script tags must NEVER appear in the HTML body or attributes
    assert.doesNotMatch(html, /<script>alert\(/i);
    assert.doesNotMatch(html, /<img\s+src=x\s+onerror/i);
    assert.doesNotMatch(html, /<svg\/onload/i);

    // HTML entities must be escaped
    assert.match(html, /&lt;script&gt;alert/);
});

test("renderDashboardHtml strictly masks connection strings and passwords as ***", () => {
    const nonce = "safeNonce456";
    const sensitiveFindings: FindingItem[] = [
        {
            id: 'finding-secret-1',
            ruleId: 'DG002',
            message: 'Type mismatch using Server=prod-sql.db;Database=Payments;User Id=dbadmin;Password=MegaSecretP@ssword123;',
            severity: 'warning',
            filePath: 'src/Data/PaymentService.cs',
            fullUri: 'file:///workspace/src/Data/PaymentService.cs',
            startLine: 20,
            startColumn: 5,
            endLine: 20,
            endColumn: 40,
            quickFixAvailable: false,
            rawSnippet: 'var conn = new SqlConnection("Server=127.0.0.1;Password=SuperSecret!;api_key=sk-live-999");'
        }
    ];

    const html = renderDashboardHtml(sensitiveFindings, nonce);

    assert.doesNotMatch(html, /MegaSecretP@ssword123/);
    assert.doesNotMatch(html, /SuperSecret!/);
    assert.doesNotMatch(html, /sk-live-999/);
    assert.match(html, /Password=\*\*\*/);
    assert.match(html, /api_key=\*\*\*/);
});
test("renderDashboardHtml redacts credentials in scanReport queries and never exposes rawSql", () => {
    const nonce = "safeNonce789";
    const report = {
        filesScanned: 1,
        queriesFound: 1,
        connectionsFound: 0,
        violationsCount: 0,
        connections: [],
        queries: [
            {
                sql: "SELECT * FROM users WHERE token = 'super_secret_token_123' AND password = 'secretPassword!'",
                operation: "Read" as const,
                tables: ["users"],
                targetType: "UserDto",
                mappingStatus: "matched" as const,
                action: "verify",
                columns: ["id", "token"],
                properties: ["Id", "Token"],
                location: { file: "src/Data/User.cs", line: 15 }
            }
        ]
    };

    const html = renderDashboardHtml([], nonce, report);
    assert.doesNotMatch(html, /secretPassword!/);
    assert.doesNotMatch(html, /"rawSql"/);
    assert.doesNotMatch(html, /"rawFile"/);
    assert.match(html, /password=\*\*\*/i);
});
