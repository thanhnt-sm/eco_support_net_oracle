import { getDashboardCss } from "./design-tokens";
import { escapeHtml, FindingItem, redactForUi } from "./redaction";

export interface DashboardWebviewOptions {
    nonce?: string;
    title?: string;
}

/**
 * Generates the CSP header string for DataGuard Webviews.
 * Adheres strictly to the architectural security requirements:
 * default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-...'.
 */
export function buildWebviewCsp(nonce: string): string {
    return `default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'; font-src data: https://fonts.gstatic.com; img-src data: https:;`;
}

/**
 * Generates the self-contained HTML for the DataGuard Dashboard Webview.
 * Purely consumes standardized FindingItem[] data without any SQL parsing or DB connection logic.
 * Enforces strict CSP and virtualized list rendering to support 15,000+ findings at 60fps.
 */
export function renderDashboardHtml(findings: FindingItem[], nonce: string): string {
    const csp = buildWebviewCsp(nonce);
    const css = getDashboardCss();

    // Redact and serialize initial state safely into JSON
    const sanitizedFindings = findings.map((f) => ({
        id: f.id,
        ruleId: escapeHtml(f.ruleId),
        message: escapeHtml(redactForUi(f.message)),
        severity: f.severity,
        filePath: escapeHtml(f.filePath),
        fullUri: escapeHtml(f.fullUri),
        startLine: f.startLine,
        startColumn: f.startColumn,
        endLine: f.endLine,
        endColumn: f.endColumn,
        quickFixAvailable: f.quickFixAvailable,
        quickFixTitle: f.quickFixTitle ? escapeHtml(f.quickFixTitle) : undefined,
        rawSnippet: f.rawSnippet ? escapeHtml(redactForUi(f.rawSnippet)) : undefined
    }));

    const initialFindingsJson = JSON.stringify(sanitizedFindings).replace(/</g, "\\u003c");

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="${csp}">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>DataGuard Contract Drift Dashboard</title>
    <style>
${css}
    </style>
</head>
<body>
    <header class="dashboard-header">
        <div class="dashboard-title">
            <span>🛡️ DataGuard Contract Drift</span>
            <span class="badge-count" id="totalCount">${sanitizedFindings.length}</span>
        </div>
        <div class="toolbar-controls">
            <button class="btn btn-secondary" id="btnRefresh" title="Re-run contract validation">🔄 Refresh</button>
            <button class="btn btn-secondary" id="btnClear" title="Clear current findings">🗑️ Clear</button>
        </div>
    </header>

    <div class="filter-bar">
        <input type="text" class="search-input" id="searchInput" placeholder="Search by rule (DG001), message, or file..." aria-label="Search findings">
        <div class="filter-chip-group" role="radiogroup" aria-label="Filter by severity">
            <button class="filter-chip active" data-severity="all" id="filterAll">All (<span id="countAll">${sanitizedFindings.length}</span>)</button>
            <button class="filter-chip" data-severity="error" id="filterError">Errors (<span id="countError">0</span>)</button>
            <button class="filter-chip" data-severity="warning" id="filterWarning">Warnings (<span id="countWarning">0</span>)</button>
            <button class="filter-chip" data-severity="information" id="filterInfo">Info (<span id="countInfo">0</span>)</button>
        </div>
    </div>

    <main class="virtual-viewport" id="viewport" tabindex="0" aria-label="Diagnostic findings list">
        <div class="virtual-scroll-spacer" id="spacer"></div>
        <div class="virtual-content" id="content"></div>
        <div class="empty-state" id="emptyState" style="display: none;">
            <div style="font-size: 32px;">✨</div>
            <div>No contract drifts detected. All SQL and entity definitions match.</div>
        </div>
    </main>

    <script nonce="${nonce}">
        (function() {
            const vscode = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;
            let allFindings = ${initialFindingsJson};
            let filteredFindings = [...allFindings];
            let activeSeverity = 'all';
            let searchQuery = '';

            const ITEM_HEIGHT = 86; // Fixed height in px for virtual row calculation
            const BUFFER_COUNT = 5;

            const viewport = document.getElementById('viewport');
            const spacer = document.getElementById('spacer');
            const content = document.getElementById('content');
            const emptyState = document.getElementById('emptyState');
            const searchInput = document.getElementById('searchInput');
            const totalCountBadge = document.getElementById('totalCount');
            const countAll = document.getElementById('countAll');
            const countError = document.getElementById('countError');
            const countWarning = document.getElementById('countWarning');
            const countInfo = document.getElementById('countInfo');

            function updateCounts() {
                let errors = 0, warnings = 0, infos = 0;
                for (let i = 0; i < allFindings.length; i++) {
                    const f = allFindings[i];
                    if (f.severity === 'error') errors++;
                    else if (f.severity === 'warning') warnings++;
                    else if (f.severity === 'information') infos++;
                }
                totalCountBadge.textContent = allFindings.length;
                countAll.textContent = allFindings.length;
                countError.textContent = errors;
                countWarning.textContent = warnings;
                countInfo.textContent = infos;
            }

            function applyFilter() {
                filteredFindings = allFindings.filter(function(item) {
                    if (activeSeverity !== 'all' && item.severity !== activeSeverity) {
                        return false;
                    }
                    if (searchQuery) {
                        const q = searchQuery.toLowerCase();
                        const matchRule = item.ruleId.toLowerCase().indexOf(q) !== -1;
                        const matchMsg = item.message.toLowerCase().indexOf(q) !== -1;
                        const matchPath = item.filePath.toLowerCase().indexOf(q) !== -1;
                        return matchRule || matchMsg || matchPath;
                    }
                    return true;
                });

                spacer.style.height = (filteredFindings.length * ITEM_HEIGHT) + 'px';
                if (filteredFindings.length === 0) {
                    emptyState.style.display = 'flex';
                    content.innerHTML = '';
                } else {
                    emptyState.style.display = 'none';
                }
                renderVirtualSlice();
            }

            let ticking = false;
            function renderVirtualSlice() {
                ticking = false;
                const total = filteredFindings.length;
                if (total === 0) {
                    content.innerHTML = '';
                    return;
                }

                const scrollTop = viewport.scrollTop;
                const clientHeight = viewport.clientHeight || 500;

                const startIndex = Math.max(0, Math.floor(scrollTop / ITEM_HEIGHT) - BUFFER_COUNT);
                const endIndex = Math.min(total, Math.ceil((scrollTop + clientHeight) / ITEM_HEIGHT) + BUFFER_COUNT);

                content.style.transform = 'translateY(' + (startIndex * ITEM_HEIGHT) + 'px)';

                let html = '';
                for (let i = startIndex; i < endIndex; i++) {
                    const item = filteredFindings[i];
                    const sevClass = 'severity-' + item.severity;
                    const badgeClass = item.severity === 'error' ? 'error' : (item.severity === 'warning' ? 'warning' : '');

                    html += '<div class="finding-card ' + sevClass + '" data-id="' + item.id + '" tabindex="0" role="button" aria-label="Finding ' + item.ruleId + '">';
                    html += '  <div class="finding-header">';
                    html += '    <span class="rule-badge ' + badgeClass + '">[' + item.ruleId + ']</span>';
                    html += '    <span class="finding-location">' + item.filePath + ':' + item.startLine + ':' + item.startColumn + '</span>';
                    html += '  </div>';
                    html += '  <div class="finding-message">' + item.message + '</div>';
                    if (item.rawSnippet) {
                        html += '  <div class="code-snippet" style="font-size: 11px; color: var(--dg-text-muted); background: rgba(0,0,0,0.3); padding: 3px 6px; border-radius: 3px;">' + item.rawSnippet + '</div>';
                    }
                    html += '  <div class="finding-actions">';
                    html += '    <button class="btn btn-secondary jump-btn" data-id="' + item.id + '">🔍 Jump to Source</button>';
                    if (item.quickFixAvailable) {
                        html += '    <button class="btn btn-cta quick-fix-btn" data-id="' + item.id + '" title="' + (item.quickFixTitle || 'Apply Quick-Fix') + '">⚡ ' + (item.quickFixTitle || 'Quick-Fix') + '</button>';
                    }
                    html += '  </div>';
                    html += '</div>';
                }
                content.innerHTML = html;
            }

            viewport.addEventListener('scroll', function() {
                if (!ticking) {
                    requestAnimationFrame(renderVirtualSlice);
                    ticking = true;
                }
            }, { passive: true });

            let searchTimeout = null;
            searchInput.addEventListener('input', function(e) {
                clearTimeout(searchTimeout);
                searchTimeout = setTimeout(function() {
                    searchQuery = e.target.value.trim();
                    applyFilter();
                }, 150);
            });

            const chips = document.querySelectorAll('.filter-chip');
            chips.forEach(function(chip) {
                chip.addEventListener('click', function() {
                    chips.forEach(function(c) { c.classList.remove('active'); });
                    chip.classList.add('active');
                    activeSeverity = chip.getAttribute('data-severity');
                    applyFilter();
                });
            });

            content.addEventListener('click', function(e) {
                const target = e.target;
                if (!target) return;

                const qfBtn = target.closest('.quick-fix-btn');
                if (qfBtn && vscode) {
                    const id = qfBtn.getAttribute('data-id');
                    vscode.postMessage({ command: 'applyQuickFix', findingId: id });
                    return;
                }

                const jumpBtn = target.closest('.jump-btn');
                if (jumpBtn && vscode) {
                    const id = jumpBtn.getAttribute('data-id');
                    vscode.postMessage({ command: 'jumpToFinding', findingId: id });
                    return;
                }

                const card = target.closest('.finding-card');
                if (card && vscode) {
                    const id = card.getAttribute('data-id');
                    vscode.postMessage({ command: 'jumpToFinding', findingId: id });
                }
            });

            document.getElementById('btnRefresh').addEventListener('click', function() {
                if (vscode) vscode.postMessage({ command: 'refresh' });
            });

            document.getElementById('btnClear').addEventListener('click', function() {
                if (vscode) vscode.postMessage({ command: 'clear' });
            });

            window.addEventListener('message', function(event) {
                const message = event.data;
                if (!message) return;
                switch (message.type) {
                    case 'setFindings':
                        allFindings = message.findings || [];
                        updateCounts();
                        applyFilter();
                        break;
                    case 'clearFindings':
                        allFindings = [];
                        updateCounts();
                        applyFilter();
                        break;
                }
            });

            updateCounts();
            applyFilter();
        })();
    </script>
</body>
</html>`;
}
