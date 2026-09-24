import { getDashboardCss } from "./design-tokens";
import { escapeHtml, FindingItem, redactForUi } from "./redaction";
import { ScanReport } from "./sql-queries-tree-provider";

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
    return `default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}'; font-src data: https://fonts.gstatic.com; img-src data:;`;
}

/**
 * Generates the self-contained HTML for the DataGuard Dashboard Webview.
 * Purely consumes standardized FindingItem[] data and ScanReport data.
 * Enforces strict CSP and virtualized list rendering to support 15,000+ findings at 60fps.
 */
export function renderDashboardHtml(findings: FindingItem[], nonce: string, scanReport: ScanReport | null = null): string {
    const csp = buildWebviewCsp(nonce);
    const css = getDashboardCss();

    // Redact and serialize initial findings safely into JSON
    const sanitizedFindings = (Array.isArray(findings) ? findings : []).map((f) => ({
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

    const sanitizedReport = scanReport ? {
        filesScanned: scanReport.filesScanned,
        queriesFound: scanReport.queriesFound,
        connectionsFound: scanReport.connectionsFound,
        connections: (Array.isArray(scanReport.connections) ? scanReport.connections : []).map((c) => ({
            name: escapeHtml(c.name),
            provider: escapeHtml(c.provider),
            hint: c.hint ? escapeHtml(redactForUi(c.hint)) : undefined
        })),
        queries: (Array.isArray(scanReport.queries) ? scanReport.queries : []).map((q) => ({
            sql: redactForUi(q.sql || ""),
            operation: q.operation,
            tables: q.tables || [],
            targetType: q.targetType || null,
            mappingStatus: q.mappingStatus,
            action: q.action,
            columns: q.columns || [],
            properties: q.properties || [],
            unmappedColumns: q.unmappedColumns || [],
            unmappedProperties: q.unmappedProperties || [],
            location: q.location ? {
                file: q.location.file || "",
                line: q.location.line
            } : null,
            targetTypeLocation: q.targetTypeLocation ? {
                file: q.targetTypeLocation.file || "",
                line: q.targetTypeLocation.line
            } : null
        }))
    } : null;

    const initialFindingsJson = JSON.stringify(sanitizedFindings).replace(/</g, "\\u003c");
    const initialReportJson = JSON.stringify(sanitizedReport).replace(/</g, "\\u003c");
    const initialQueryCount = sanitizedReport ? sanitizedReport.queries.length : 0;

    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="${csp}">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>DataGuard Contract Drift Dashboard</title>
    <style>
${css}
.nav-tabs {
    display: flex;
    gap: 8px;
    border-bottom: 1px solid var(--dg-border);
    margin-bottom: 16px;
    padding-bottom: 0;
}
.nav-tab {
    background: transparent;
    border: none;
    border-bottom: 2px solid transparent;
    color: var(--dg-text-muted);
    font-size: 13px;
    font-weight: 500;
    padding: 8px 16px;
    cursor: pointer;
    transition: all 150ms ease;
}
.nav-tab:hover {
    color: var(--dg-text-main);
}
.nav-tab.active {
    color: var(--dg-secondary);
    border-bottom-color: var(--dg-secondary);
    font-weight: 600;
}
.queries-table-container {
    overflow-x: auto;
    background: var(--dg-bg-card);
    border: 1px solid var(--dg-border);
    border-radius: 6px;
}
.queries-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 12px;
}
.queries-table th {
    background: #0f172a;
    color: var(--dg-text-muted);
    text-align: left;
    padding: 10px 12px;
    border-bottom: 1px solid var(--dg-border);
    font-weight: 600;
}
.queries-table td {
    padding: 10px 12px;
    border-bottom: 1px solid var(--dg-border);
    vertical-align: top;
}
.queries-table tr:hover {
    background: var(--dg-bg-card-hover);
}
.query-sql-link {
    font-family: var(--dg-font-code);
    color: var(--dg-text-main);
    cursor: pointer;
    text-decoration: underline;
    text-decoration-style: dotted;
    word-break: break-word;
}
.badge-status {
    display: inline-block;
    padding: 2px 6px;
    border-radius: 4px;
    font-size: 11px;
    font-weight: 600;
    text-transform: uppercase;
}
.badge-status-matched { background: #065f46; color: #34d399; }
.badge-status-partial { background: #78350f; color: #fbbf24; }
.badge-status-unmapped { background: #7f1d1d; color: #f87171; }
.badge-status-untyped { background: #334155; color: #94a3b8; }
.badge-warning-text { color: #fbbf24; font-weight: 500; font-size: 11px; }
.unmapped-highlight { color: #f87171; font-weight: 500; }
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

    <nav class="nav-tabs" role="tablist">
        <button class="nav-tab active" id="tabFindings" role="tab" aria-selected="true">Contract Drift Findings (<span id="findingsTabCount">${sanitizedFindings.length}</span>)</button>
        <button class="nav-tab" id="tabQueries" role="tab" aria-selected="false">SQL ↔ C# Mappings (<span id="queriesTabCount">${initialQueryCount}</span>)</button>
    </nav>

    <div id="findingsView">
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
    </div>

    <div id="queriesView" style="display: none;">
        <div class="filter-bar">
            <input type="text" class="search-input" id="queriesSearchInput" placeholder="Filter queries by SQL snippet, target type, or table..." aria-label="Filter queries">
            <button class="btn btn-secondary" id="btnScanProject" title="Scan project for SQL and C# mappings">🔍 Scan Project</button>
        </div>
        <div class="queries-table-container">
            <table class="queries-table" id="queriesTable">
                <thead>
                    <tr>
                        <th style="width: 85px;">Status</th>
                        <th style="width: 65px;">Op</th>
                        <th>SQL Query</th>
                        <th style="width: 130px;">Target Type</th>
                        <th style="width: 150px;">Matched Columns</th>
                        <th style="width: 130px;">Unmapped Col</th>
                        <th style="width: 130px;">Unmapped Prop</th>
                    </tr>
                </thead>
                <tbody id="queriesTableBody"></tbody>
            </table>
            <div class="empty-state" id="queriesEmptyState" style="display: none;">
                <div style="font-size: 32px;">🔍</div>
                <div>No SQL queries discovered yet. Click "Scan Project" to discover queries.</div>
            </div>
        </div>
    </div>

    <script nonce="${nonce}">
        (function() {
            const vscode = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null;
            let allFindings = ${initialFindingsJson};
            let filteredFindings = [...allFindings];
            let activeSeverity = 'all';
            let searchQuery = '';

            let currentReport = ${initialReportJson};
            let queriesSearchQuery = '';

            function escapeHtmlClient(str) {
                if (str === null || str === undefined) return '';
                return String(str)
                    .replace(/&(?!([a-zA-Z0-9]+|#[0-9]{1,6}|#[xX][0-9a-fA-F]{1,6});)/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;')
                    .replace(/"/g, '&quot;')
                    .replace(/'/g, '&#39;');
            }

            const ITEM_HEIGHT = 86; // Fixed height in px for virtual row calculation
            const BUFFER_COUNT = 5;

            const tabFindings = document.getElementById('tabFindings');
            const tabQueries = document.getElementById('tabQueries');
            const findingsView = document.getElementById('findingsView');
            const queriesView = document.getElementById('queriesView');
            const findingsTabCount = document.getElementById('findingsTabCount');
            const queriesTabCount = document.getElementById('queriesTabCount');

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

            const queriesSearchInput = document.getElementById('queriesSearchInput');
            const queriesTableBody = document.getElementById('queriesTableBody');
            const queriesEmptyState = document.getElementById('queriesEmptyState');
            const btnScanProject = document.getElementById('btnScanProject');

            // Tabs
            tabFindings.addEventListener('click', function() {
                tabFindings.classList.add('active');
                tabFindings.setAttribute('aria-selected', 'true');
                tabQueries.classList.remove('active');
                tabQueries.setAttribute('aria-selected', 'false');
                findingsView.style.display = 'block';
                queriesView.style.display = 'none';
            });

            tabQueries.addEventListener('click', function() {
                tabQueries.classList.add('active');
                tabQueries.setAttribute('aria-selected', 'true');
                tabFindings.classList.remove('active');
                tabFindings.setAttribute('aria-selected', 'false');
                findingsView.style.display = 'none';
                queriesView.style.display = 'block';
                renderQueries();
            });

            function updateCounts() {
                let errors = 0, warnings = 0, infos = 0;
                for (let i = 0; i < allFindings.length; i++) {
                    const f = allFindings[i];
                    if (f.severity === 'error') errors++;
                    else if (f.severity === 'warning') warnings++;
                    else if (f.severity === 'information') infos++;
                }
                totalCountBadge.textContent = allFindings.length;
                findingsTabCount.textContent = allFindings.length;
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
                    const sevClass = 'severity-' + escapeHtmlClient(item.severity);
                    const badgeClass = item.severity === 'error' ? 'error' : (item.severity === 'warning' ? 'warning' : '');

                    html += '<div class="finding-card ' + sevClass + '" data-id="' + escapeHtmlClient(item.id) + '" tabindex="0" role="button" aria-label="Finding ' + escapeHtmlClient(item.ruleId) + '">';
                    html += '  <div class="finding-header">';
                    html += '    <span class="rule-badge ' + badgeClass + '">[' + escapeHtmlClient(item.ruleId) + ']</span>';
                    html += '    <span class="finding-location">' + escapeHtmlClient(item.filePath) + ':' + item.startLine + ':' + item.startColumn + '</span>';
                    html += '  </div>';
                    html += '  <div class="finding-message">' + escapeHtmlClient(item.message) + '</div>';
                    if (item.rawSnippet) {
                        html += '  <div class="code-snippet" style="font-size: 11px; color: var(--dg-text-muted); background: rgba(0,0,0,0.3); padding: 3px 6px; border-radius: 3px;">' + escapeHtmlClient(item.rawSnippet) + '</div>';
                    }
                    html += '  <div class="finding-actions">';
                    html += '    <button class="btn btn-secondary jump-btn" data-id="' + escapeHtmlClient(item.id) + '">🔍 Jump to Source</button>';
                    if (item.quickFixAvailable) {
                        html += '    <button class="btn btn-cta quick-fix-btn" data-id="' + escapeHtmlClient(item.id) + '" title="' + escapeHtmlClient(item.quickFixTitle || 'Apply Quick-Fix') + '">⚡ ' + escapeHtmlClient(item.quickFixTitle || 'Quick-Fix') + '</button>';
                    }
                    html += '  </div>';
                    html += '</div>';
                }
                content.innerHTML = html;
            }

            // Queries Rendering
            function renderQueries() {
                const queries = (currentReport && currentReport.queries) ? currentReport.queries : [];
                queriesTabCount.textContent = queries.length;

                const filtered = queries.filter(function(q) {
                    if (!queriesSearchQuery) return true;
                    const needle = queriesSearchQuery.toLowerCase();
                    const matchSql = (q.sql || '').toLowerCase().indexOf(needle) !== -1;
                    const matchTarget = (q.targetType || '').toLowerCase().indexOf(needle) !== -1;
                    const matchTable = (q.tables || []).some(function(t) { return t.toLowerCase().indexOf(needle) !== -1; });
                    return matchSql || matchTarget || matchTable;
                });

                if (filtered.length === 0) {
                    queriesEmptyState.style.display = 'flex';
                    queriesTableBody.innerHTML = '';
                    return;
                }

                queriesEmptyState.style.display = 'none';
                let tbody = '';
                for (let i = 0; i < filtered.length; i++) {
                    const q = filtered[i];
                    const statusClass = 'badge-status-' + escapeHtmlClient(q.mappingStatus || 'untyped');
                    const locAttr = q.location ? 'data-file="' + escapeHtmlClient(q.location.file || '') + '" data-line="' + (q.location.line || 1) + '"' : '';
                    const matchedProps = (q.properties || []).filter(function(p) {
                        return !(q.unmappedProperties || []).includes(p);
                    });

                    tbody += '<tr>';
                    tbody += '  <td><span class="badge-status ' + statusClass + '">' + escapeHtmlClient(q.mappingStatus || 'untyped') + '</span></td>';
                    tbody += '  <td>' + escapeHtmlClient(q.operation || 'Read') + '</td>';
                    tbody += '  <td><div class="query-sql-link" ' + locAttr + ' title="Click to jump to source">' + escapeHtmlClient(q.sql) + '</div>';
                    if (q.location && q.location.file) {
                        tbody += '    <div style="font-size: 11px; color: var(--dg-text-muted); margin-top: 2px;">📍 ' + escapeHtmlClient(q.location.file) + ':' + (q.location.line || 1) + '</div>';
                    }
                    tbody += '  </td>';
                    tbody += '  <td><code>' + escapeHtmlClient(q.targetType || 'untyped') + '</code></td>';
                    tbody += '  <td>' + (matchedProps.length > 0 ? matchedProps.map(escapeHtmlClient).join(', ') : '-') + '</td>';
                    tbody += '  <td>' + (q.unmappedColumns && q.unmappedColumns.length > 0 ? '<span class="unmapped-highlight">' + q.unmappedColumns.map(escapeHtmlClient).join(', ') + '</span>' : 'None') + '</td>';
                    tbody += '  <td>' + (q.unmappedProperties && q.unmappedProperties.length > 0 ? '<span class="unmapped-highlight">' + q.unmappedProperties.map(escapeHtmlClient).join(', ') + '</span>' : 'None') + '</td>';
                    tbody += '</tr>';
                }
                queriesTableBody.innerHTML = tbody;
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

            let queriesSearchTimeout = null;
            queriesSearchInput.addEventListener('input', function(e) {
                clearTimeout(queriesSearchTimeout);
                queriesSearchTimeout = setTimeout(function() {
                    queriesSearchQuery = e.target.value.trim();
                    renderQueries();
                }, 150);
            });

            btnScanProject.addEventListener('click', function() {
                if (vscode) vscode.postMessage({ command: 'scanProject' });
            });

            queriesTableBody.addEventListener('click', function(e) {
                const target = e.target;
                if (!target) return;
                const link = target.closest('.query-sql-link');
                if (link && vscode) {
                    const file = link.getAttribute('data-file');
                    const line = parseInt(link.getAttribute('data-line') || '1', 10);
                    if (file) {
                        vscode.postMessage({ command: 'jumpToLocation', file: file, line: line });
                    }
                }
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
                    case 'setScanReport':
                        currentReport = message.report;
                        renderQueries();
                        break;
                }
            });

            updateCounts();
            applyFilter();
            renderQueries();
        })();
    </script>
</body>
</html>`;
}
