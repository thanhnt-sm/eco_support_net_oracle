import assert from "node:assert/strict";
import test from "node:test";
import { renderDashboardHtml } from "./dashboard-view";
import { FindingItem } from "./redaction";
import { ScanReport } from "./sql-queries-tree-provider";

function makeDummyFinding(id: string, severity: "error" | "warning" | "information" = "error"): FindingItem {
    return {
        id,
        ruleId: "DG001",
        message: "Mismatch in contract for " + id,
        severity,
        filePath: "src/UserRepo.cs",
        fullUri: "file:///workspace/src/UserRepo.cs",
        startLine: 10,
        startColumn: 1,
        endLine: 10,
        endColumn: 20,
        quickFixAvailable: false
    };
}

test("Step 3: Dashboard client script contains index-based keyboard navigation for virtual viewport", () => {
    const html = renderDashboardHtml([makeDummyFinding("1"), makeDummyFinding("2")], "nonce-123");

    assert.match(html, /var\s+focusedFindingIndex\s*=\s*-1|let\s+focusedFindingIndex\s*=\s*-1/);
    assert.match(html, /viewport\.addEventListener\(\s*['"]keydown['"]/);
    assert.match(html, /ArrowDown/);
    assert.match(html, /ArrowUp/);
    assert.match(html, /viewport\.scrollTop\s*=\s*focusedFindingIndex\s*\*\s*ITEM_HEIGHT/);
    assert.match(html, /focusedFindingIndex\s*=\s*-1/); // reset on filter
});

test("Step 4: ARIA tab panel semantics and arrow key navigation on tablist", () => {
    const html = renderDashboardHtml([], "nonce-123");

    assert.match(html, /<div\s+id="findingsView"[^>]*role="tabpanel"[^>]*aria-labelledby="tabFindings"/);
    assert.match(html, /<div\s+id="queriesView"[^>]*role="tabpanel"[^>]*aria-labelledby="tabQueries"/);
    assert.match(html, /<button[^>]*id="tabFindings"[^>]*aria-controls="findingsView"/);
    assert.match(html, /<button[^>]*id="tabQueries"[^>]*aria-controls="queriesView"/);

    assert.match(html, /navTabs\.addEventListener\(\s*['"]keydown['"]/);
    assert.match(html, /ArrowRight/);
    assert.match(html, /ArrowLeft/);
});

test("Step 5: Dashboard HTML does not contain emoji characters", () => {
    const dummyReport: ScanReport = {
        filesScanned: 1,
        queriesFound: 1,
        connectionsFound: 0,
        violationsCount: 0,
        connections: [],
        queries: [{
            sql: "SELECT * FROM Users",
            operation: "Read",
            tables: ["Users"],
            mappingStatus: "matched",
            action: "read",
            columns: ["id"],
            properties: ["Id"],
            location: { file: "test.cs", line: 1 }
        }]
    };
    const finding = makeDummyFinding("1");
    finding.quickFixAvailable = true;
    finding.quickFixTitle = "Apply fix";

    const html = renderDashboardHtml([finding], "nonce-123", dummyReport);

    const bannedEmojis = ["🛡️", "🔄", "🗑️", "✨", "🔍", "⚡", "📍"];
    for (const emoji of bannedEmojis) {
        assert.ok(!html.includes(emoji), `HTML must not contain emoji ${emoji}`);
    }
    assert.ok(!html.includes('style="font-size: 32px;"'), 'HTML must not contain empty-state 32px emoji wrapper divs');
});

test("Step 7: Async buttons (Refresh, Scan Project) toggle loading state on click and response", () => {
    const html = renderDashboardHtml([], "nonce-123");

    assert.match(html, /btnRefresh\.disabled\s*=\s*true/);
    assert.match(html, /btnRefresh\.textContent.*…/);
    assert.match(html, /btnScanProject\.disabled\s*=\s*true/);
    assert.match(html, /btnScanProject\.textContent.*…/);
    assert.match(html, /btnRefresh\.disabled\s*=\s*false/);
    assert.match(html, /btnScanProject\.disabled\s*=\s*false/);
});

test("Step 8: Client script template contains text severity label alongside color", () => {
    const html = renderDashboardHtml([], "nonce-123");

    assert.match(html, /class="severity-label"/);
    assert.match(html, /escapeHtmlClient\(item\.severity\)/);
});

test("Review regression: Content keydown preserves button Enter/Space and synchronizes focusin index", () => {
    const html = renderDashboardHtml([], "nonce-123");

    assert.match(html, /target\.tagName\s*===\s*['"]BUTTON['"]\s*\|\|\s*target\.tagName\s*===\s*['"]A['"]/);
    assert.match(html, /content\.addEventListener\(\s*['"]focusin['"]/);
    assert.match(html, /focusedFindingIndex\s*=\s*idx/);
});
