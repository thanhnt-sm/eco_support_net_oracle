/** Real dashboard/SQL-tree controllers with a recording VS Code API, not an editor host. */
import assert from "node:assert/strict";
import test, { TestContext } from "node:test";
import Module from "node:module";
import { promises as fs } from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import type { QueryScanItem, ScanReport } from "./sql-queries-tree-provider";
import type { FindingItem } from "./redaction";

class Emitter {
    listeners = new Set<() => void>();
    event = (fn: () => void) => { this.listeners.add(fn); return { dispose: () => this.listeners.delete(fn) }; };
    fire() { this.listeners.forEach(fn => fn()); }
}
class Range {
    start: { line: number; character: number }; end: { line: number; character: number };
    constructor(sl: number, sc: number, el: number, ec: number) { this.start = { line: sl, character: sc }; this.end = { line: el, character: ec }; }
}
class Markdown {
    value = "";
    appendMarkdown(v: string) { this.value += v; return this; }
    appendCodeblock(v: string) { this.value += v; return this; }
}
class Icon { constructor(public id: string, public color?: Icon) {} }
const query = (overrides: Partial<QueryScanItem> = {}): QueryScanItem => ({
    sql: "SELECT ID FROM Customer", operation: "Read", tables: ["Customer"], targetType: "CustomerDto",
    mappingStatus: "matched", action: "shape-check", columns: ["ID"], properties: ["Id"],
    unmappedColumns: ["Other"], unmappedProperties: ["OtherProperty"], ...overrides,
});
const report = (queries: QueryScanItem[] = []): ScanReport => ({
    filesScanned: 2, queriesFound: queries.length, connectionsFound: 0, violationsCount: 0, connections: [], queries,
});
const finding = (filePath: string, overrides: Partial<FindingItem> = {}): FindingItem => ({
    id: "f1", ruleId: "DG017", message: "Use explicit columns", severity: "warning", filePath,
    fullUri: `file://${filePath}`, startLine: 2, startColumn: 20, endLine: 4, endColumn: 3,
    quickFixAvailable: false, ...overrides,
});
async function harness(t: TestContext) {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "controller-behavior-"));
    const a = path.join(root, "a"), b = path.join(root, "b");
    await fs.mkdir(a); await fs.mkdir(b);
    await fs.writeFile(path.join(a, "Example.cs"), "synthetic");
    await fs.writeFile(path.join(b, "Other.cs"), "synthetic");
    const uri = (p: string) => ({ fsPath: p, scheme: "file" });
    const calls: any[][] = [], opened: string[] = [], warnings: string[] = [], panels: any[] = [];
    const editor = { selection: undefined as any, ranges: [] as Range[], revealRange(r: Range) { this.ranges.push(r); } };
    let messageHandler: ((v: any) => Promise<void>) | undefined;
    const api = {
        EventEmitter: Emitter, TreeItem: class { constructor(public label: string, public collapsibleState: number) {} },
        TreeItemCollapsibleState: { None: 0, Expanded: 2 }, ThemeIcon: Icon, ThemeColor: Icon,
        MarkdownString: Markdown, Range,
        Selection: class { constructor(public start: any, public end: any) {} },
        Uri: { file: uri, joinPath: (u: any, p: string) => uri(path.join(u.fsPath, p)) },
        ViewColumn: { One: 1 }, TextEditorRevealType: { InCenter: 0 },
        workspace: {
            workspaceFolders: [{ uri: uri(a) }, { uri: uri(b) }],
            asRelativePath: (u: any) => path.relative(root, u.fsPath),
            async openTextDocument(u: any) { opened.push(u.fsPath); return { uri: u }; },
        },
        commands: { async executeCommand(...args: any[]) { calls.push(args); } },
        window: {
            activeTextEditor: undefined as { viewColumn: number } | undefined,
            async showTextDocument(_doc: any, _options: any) { return editor; },
            showWarningMessage(s: string) { warnings.push(s); },
            createWebviewPanel(...args: any[]) {
                const disposeListeners = new Set<() => void>();
                const panel = {
                    args, reveals: [] as any[], disposed: false, disposedSubscriptions: 0,
                    reveal(column: any) { this.reveals.push(column); },
                    dispose() { if (this.disposed) { return; } this.disposed = true; disposeListeners.forEach(f => f()); },
                    onDidDispose(fn: () => void, _this: any, disposables: any[]) {
                        disposeListeners.add(fn); disposables.push({ dispose: () => { disposeListeners.delete(fn); panel.disposedSubscriptions++; } });
                    },
                    webview: {
                        html: "", messages: [] as any[],
                        postMessage(v: any) { this.messages.push(v); return Promise.resolve(true); },
                        onDidReceiveMessage(fn: (v: any) => Promise<void>, _this: any, disposables: any[]) {
                            messageHandler = fn; disposables.push({ dispose: () => { messageHandler = undefined; panel.disposedSubscriptions++; } });
                        },
                    },
                }; panels.push(panel); return panel;
            },
        },
    };
    const loader = Module as unknown as { _load: (id: string, ...args: any[]) => any };
    const original = loader._load;
    let Dashboard!: typeof import("./dashboard-panel").DataGuardDashboardPanel;
    let Tree!: typeof import("./sql-queries-tree-provider").DataGuardSqlQueriesTreeProvider;
    let Item!: typeof import("./sql-queries-tree-provider").SqlQueryTreeItem;
    try {
        delete require.cache[require.resolve("./dashboard-panel")]; delete require.cache[require.resolve("./sql-queries-tree-provider")];
        loader._load = function (id, ...args) { return id === "vscode" ? api : original.call(this, id, ...args); };
        Dashboard = require("./dashboard-panel").DataGuardDashboardPanel;
        ({ DataGuardSqlQueriesTreeProvider: Tree, SqlQueryTreeItem: Item } = require("./sql-queries-tree-provider"));
    } finally { loader._load = original; }
    t.after(async () => { Dashboard.currentPanel?.dispose(); await fs.rm(root, { recursive: true, force: true }); });
    return { root, a, b, api, calls, opened, warnings, editor, panels, Dashboard, Tree, Item,
        show: (findings = [finding(path.join(a, "Example.cs"))], scan?: ScanReport | null) => Dashboard.createOrShow(uri(root) as any, findings, scan),
        message: async (v: any) => { await messageHandler?.(v); } };
}

test("dashboard creates constrained webview, reuses it, sends updates and disposes idempotently", async t => {
    const h = await harness(t); const initial = [finding(path.join(h.a, "Example.cs"))];
    const controller = h.show(initial, report()); const panel = h.panels[0];
    assert.equal(h.panels.length, 1); assert.equal(panel.args[2], 1);
    assert.deepEqual(panel.args[3].localResourceRoots.map((u: any) => u.fsPath), [path.join(h.root, "media")]);
    assert.equal(panel.args[3].enableScripts, true);
    assert.match(panel.webview.html, /Content-Security-Policy/); assert.match(panel.webview.html, /script-src 'nonce-/);
    assert.match(panel.webview.html, /Use explicit columns/);
    h.api.window.activeTextEditor = { viewColumn: 2 };
    assert.equal(h.show([], null), controller); assert.deepEqual(panel.reveals, [2]);
    assert.deepEqual(panel.webview.messages, [{ type: "setFindings", findings: [] }, { type: "setScanReport", report: null }]);
    h.show(initial); assert.equal(panel.webview.messages.length, 3);
    controller.clear(); assert.deepEqual(panel.webview.messages.at(-1), { type: "clearFindings" });
    panel.dispose(); controller.dispose(); assert.equal(h.Dashboard.currentPanel, undefined);
    assert.equal(panel.disposedSubscriptions, 2);
    const count = panel.webview.messages.length;
    controller.update(initial); controller.updateScanReport(report()); controller.clear();
    await h.message({ command: "refresh" });
    assert.equal(panel.webview.messages.length, count); assert.deepEqual(h.calls, []);
    h.show(); assert.equal(h.panels.length, 2);
});

test("dashboard only dispatches known actions and current finding IDs", async t => {
    const h = await harness(t); const controller = h.show();
    for (const payload of [null, "refresh", {}, { command: 42 }, { command: "unknown" }, { command: "applyQuickFix", findingId: 3 }, { command: "applyQuickFix", findingId: "old" }]) { await h.message(payload); }
    assert.deepEqual(h.calls, []);
    for (const command of ["refresh", "scanProject", "clear"]) { await h.message({ command }); }
    await h.message({ command: "applyQuickFix", findingId: "f1" });
    assert.deepEqual(h.calls, [["dataguard.runValidation"], ["dataguard.scanProject"], ["dataguard.clearFindings"], ["dataguard.applyQuickFix", "f1"]]);
    controller.clear(); await h.message({ command: "applyQuickFix", findingId: "f1" }); assert.equal(h.calls.length, 4);
    h.api.commands.executeCommand = async () => { throw new Error("synthetic command failure"); };
    await assert.doesNotReject(h.message({ command: "refresh" }));
});

test("dashboard navigates current finding with multiline selection and rejects outside paths", async t => {
    const h = await harness(t); const controller = h.show();
    await h.message({ command: "jumpToFinding", findingId: 7 });
    await h.message({ command: "jumpToFinding", findingId: "missing" }); assert.deepEqual(h.opened, []);
    await h.message({ command: "jumpToFinding", findingId: "f1" });
    assert.deepEqual(h.opened, [path.join(h.a, "Example.cs")]);
    assert.deepEqual(h.editor.selection.start, { line: 1, character: 19 });
    assert.deepEqual(h.editor.selection.end, { line: 3, character: 2 });
    controller.update([finding(path.join(h.root, "outside.cs"))]);
    await h.message({ command: "jumpToFinding", findingId: "f1" });
    assert.equal(h.opened.length, 1); assert.match(h.warnings.at(-1)!, /outside active workspace/);
    controller.update([finding("Other.cs")]);
    await h.message({ command: "jumpToFinding", findingId: "f1" }); assert.equal(h.opened.at(-1), path.join(h.b, "Other.cs"));
    h.api.workspace.openTextDocument = async () => { throw new Error("synthetic file failure"); };
    await h.message({ command: "jumpToFinding", findingId: "f1" }); assert.match(h.warnings.at(-1)!, /Could not open file/);
});

test("dashboard location messages enforce workspace and regular-file guards", async t => {
    const h = await harness(t); h.show([], report());
    for (const payload of [{ command: "jumpToLocation", file: 1, line: 1 }, { command: "jumpToLocation", file: "Example.cs", line: "2" }]) { await h.message(payload); }
    assert.deepEqual(h.opened, []);
    await h.message({ command: "jumpToLocation", file: "Other.cs", line: 3 });
    assert.equal(h.opened.at(-1), path.join(h.b, "Other.cs")); assert.equal(h.editor.selection.start.line, 2);
    await h.message({ command: "jumpToLocation", file: path.join(h.a, "Example.cs"), line: -4 }); assert.equal(h.editor.selection.start.line, 0);
    for (const file of ["missing.cs", "../outside.cs", h.a]) { await h.message({ command: "jumpToLocation", file, line: 1 }); }
    assert.equal(h.opened.length, 2); assert.equal(h.warnings.length, 3);
    h.api.window.showTextDocument = async () => { throw new Error("synthetic editor failure"); };
    await h.message({ command: "jumpToLocation", file: "Example.cs", line: 1 }); assert.match(h.warnings.at(-1)!, /Could not open file/);
    h.api.workspace.workspaceFolders = [];
    await h.message({ command: "jumpToLocation", file: "Example.cs", line: 1 }); assert.match(h.warnings.at(-1)!, /outside active workspace/);
});

test("SQL tree groups duplicate basenames by full path, expands and emits replacement/clear events", async t => {
    const h = await harness(t); const tree = new h.Tree(); let events = 0; tree.onDidChangeTreeData(() => events++);
    assert.equal((await tree.getChildren())[0].itemType, "empty");
    tree.setScanReport(report([query({ location: { file: path.join(h.a, "Example.cs"), line: 2 } }),
        query({ location: { file: path.join(h.b, "Example.cs"), line: 4 } }), query(), null as any]));
    const groups = await tree.getChildren(); assert.equal(groups.length, 3); assert.notEqual(groups[0].id, groups[1].id);
    assert.equal(groups[0].description, "(1 queries)"); assert.equal(groups[0].tooltip, path.join(h.a, "Example.cs"));
    assert.equal(tree.getTreeItem(groups[0]), groups[0]);
    const [item] = await tree.getChildren(groups[0]); assert.equal(item.label, "SELECT ID FROM Customer");
    assert.equal(item.command!.command, "vscode.open");
    const options = item.command!.arguments![1]; assert.equal(options.selection.start.line, 1);
    assert.deepEqual(await tree.getChildren(item), []);
    const [unknown] = await tree.getChildren(groups[2]); assert.equal(unknown.command, undefined);
    tree.clear(); assert.equal(events, 2); assert.equal((await tree.getChildren())[0].itemType, "empty");
    tree.setScanReport({ ...report(), queries: null as any }); assert.equal((await tree.getChildren())[0].itemType, "empty");
    tree.setScanReport(null); assert.equal(events, 4);
});

for (const [status, icon] of [["matched", "pass"], ["partial", "warning"], ["unmapped", "error"], ["untyped", "database"]] as const) {
    test(`SQL ${status} query exposes mapping detail and navigation icon`, async t => {
        const h = await harness(t); const q = query({ mappingStatus: status, location: { file: path.join(h.a, "Example.cs"), line: 3 } });
        const item = new h.Item("ignored", 0, "query", q);
        assert.equal((item.iconPath as any).id, icon); assert.equal(item.description, `CustomerDto [${status}]`);
        const tooltip = (item.tooltip as any).value;
        for (const expected of ["SELECT ID FROM Customer", "READ Query", "OtherProperty", "Columns", "Location"]) { assert.ok(tooltip.includes(expected)); }
        assert.equal(item.contextValue, "sqlQuery");
    });
}

test("SQL tree redacts query previews, truncates long SQL and suppresses outside navigation", async t => {
    const h = await harness(t); const tree = new h.Tree();
    tree.setScanReport(report([query({ sql: "Password=SyntheticSecret; SELECT " + "column, ".repeat(12),
        targetType: null, tables: [], columns: [], properties: [], unmappedColumns: [], unmappedProperties: [],
        location: { file: path.join(h.root, "outside.cs"), line: 1 } })]));
    const [group] = await tree.getChildren(); const [item] = await tree.getChildren(group);
    assert.ok(String(item.label).endsWith("...")); assert.equal(String(item.label).length, 55);
    assert.doesNotMatch(String(item.label) + (item.tooltip as any).value, /SyntheticSecret/);
    assert.equal(item.description, "untyped [matched]"); assert.equal(item.command, undefined);
    h.api.workspace.workspaceFolders = [];
    assert.equal(new h.Item("", 0, "query", query({ location: { file: path.join(h.a, "Example.cs"), line: 1 } })).command, undefined);
});

test("SQL tree falls back to basename if editor path formatting fails", async t => {
    const h = await harness(t); h.api.workspace.asRelativePath = () => { throw new Error("synthetic formatting failure"); };
    const tree = new h.Tree(); tree.setScanReport(report([query({ location: { file: path.join(h.a, "Example.cs"), line: 1 } })]));
    const [group] = await tree.getChildren(); assert.equal(group.label, "Example.cs");
    const [item] = await tree.getChildren(group); assert.equal(item.command!.arguments![0].fsPath, path.join(h.a, "Example.cs"));
});
