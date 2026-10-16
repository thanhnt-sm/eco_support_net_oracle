/** Production providers with a recording VS Code API boundary, not an editor host. */
import assert from "node:assert/strict";
import test from "node:test";
import Module from "node:module";
import type { FindingItem } from "./redaction";
class Position { constructor(public line: number, public character: number) {} }
class Range {
    start: Position; end: Position;
    constructor(sl: number, sc: number, el: number, ec: number) {
        this.start = new Position(sl, sc); this.end = new Position(el, ec);
    }
    contains(p: Position) {
        return (p.line > this.start.line || p.line === this.start.line && p.character >= this.start.character)
            && (p.line < this.end.line || p.line === this.end.line && p.character <= this.end.character);
    }
}
class MarkdownString {
    isTrusted = false; supportHtml = false;
    parts: { kind: string; text: string }[] = [];
    constructor(value = "") { if (value) this.parts.push({ kind: "markdown", text: value }); }
    appendMarkdown(text: string) { this.parts.push({ kind: "markdown", text }); return this; }
    appendText(text: string) { this.parts.push({ kind: "text", text }); return this; }
    appendCodeblock(text: string, language: string) { this.parts.push({ kind: language, text }); return this; }
}
class EventEmitter {
    listeners: (() => void)[] = [];
    event = (fn: () => void) => { this.listeners.push(fn); return { dispose() {} }; };
    fire() { this.listeners.forEach(fn => fn()); }
}
class Token { constructor(public id: string, public color?: Token) {} }
class TreeItem { constructor(public label: string, public collapsibleState: number) {} }
class CodeLens { constructor(public range: Range, public command: any) {} }
class CodeAction { constructor(public title: string, public kind: string) {} }
const notices: string[] = []; const mutations: unknown[] = []; const opened: unknown[] = [];
const api = {
    Position, Range, MarkdownString, EventEmitter, TreeItem, CodeLens, CodeAction,
    ThemeIcon: Token, ThemeColor: Token, TreeItemCollapsibleState: { None: 0, Expanded: 2 },
    CodeActionKind: { QuickFix: "quickfix" },
    Uri: { file: (p: string) => ({ fsPath: p, toString: () => `file://${p}` }), parse: (p: string) => ({ fsPath: decodeURIComponent(p.replace(/^file:\/\//, "")) }) },
    Hover: class { constructor(public contents: MarkdownString) {} },
    WorkspaceEdit: class { replace(...args: unknown[]) { mutations.push(args); } insert(...args: unknown[]) { mutations.push(args); } },
    workspace: { async applyEdit(edit: unknown) { mutations.push(edit); return true; } },
    window: {
        async showTextDocument(uri: unknown) { opened.push(uri); return { document: { lineAt: () => ({ text: 'var sql = "SELECT * FROM Customer";' }) } }; },
        showInformationMessage(message: string) { notices.push(message); },
        showWarningMessage(message: string) { notices.push(message); },
    },
};
const loader = Module as unknown as { _load: (id: string, ...args: unknown[]) => any };
const original = loader._load;
let Lens: any, Hover: any, Tree: any, Fix: any;
try {
    loader._load = function (id, ...args) { return id === "vscode" ? api : original.call(this, id, ...args); };
    Lens = require("./codelens-provider").DataGuardCodeLensProvider;
    Hover = require("./hover-provider").DataGuardHoverProvider;
    Tree = require("./findings-tree-provider").DataGuardFindingsTreeProvider;
    Fix = require("./quick-fix-provider").DataGuardQuickFixProvider;
} finally { loader._load = original; }
const f = (overrides: Partial<FindingItem> = {}): FindingItem => ({
    id: "f1", ruleId: "DG017", severity: "warning", message: "Select explicit columns",
    filePath: "/src/A.cs", fullUri: "file:///src/A.cs", startLine: 2, startColumn: 1,
    endLine: 2, endColumn: 8, quickFixAvailable: false, ...overrides,
});
const doc = { uri: api.Uri.file("/src/A.cs"), isDirty: false };
const token = { isCancellationRequested: false };

test("CodeLens groups and sorts actual findings, emits changes, then clears", () => {
    const p = new Lens(); let changes = 0; p.onDidChangeCodeLenses(() => changes++);
    p.setFindings([f({ startLine: 8 }), f(), f({ id: "f2" })]); p.refresh();
    const result = p.provideCodeLenses(doc, token);
    assert.equal(changes, 2); assert.deepEqual(result.map((v: any) => v.range.start.line), [1, 7]);
    assert.match(result[0].command.title, /2 findings/); assert.match(result[1].command.title, /1 finding.*DG017/);
    assert.equal(result[0].command.command, "dataguard.openDashboard");
    assert.match(result[0].command.tooltip, /Select explicit columns/);
    p.clear(); assert.equal(changes, 3); assert.deepEqual(p.provideCodeLenses(doc, token), []);
});
test("CodeLens rejects dirty/cancelled documents including cancellation during grouping", () => {
    const p = new Lens(); p.setFindings([f()]);
    assert.deepEqual(p.provideCodeLenses({ ...doc, isDirty: true }, token), []);
    assert.deepEqual(p.provideCodeLenses(doc, { isCancellationRequested: true }), []);
    let reads = 0;
    assert.deepEqual(p.provideCodeLenses(doc, { get isCancellationRequested() { return ++reads === 2; } }), []);
    reads = 0;
    assert.deepEqual(p.provideCodeLenses(doc, { get isCancellationRequested() { return ++reads === 3; } }), []);
});
test("CodeLens emits 50 locations but aggregates 51", () => {
    const p = new Lens();
    const findings = Array.from({ length: 51 }, (_, i) => f({ id: String(i), startLine: i + 1 }));
    p.setFindings(findings.slice(0, 50)); assert.equal(p.provideCodeLenses(doc, token).length, 50);
    p.setFindings(findings); const result = p.provideCodeLenses(doc, token);
    assert.equal(result.length, 1); assert.match(result[0].command.title, /51 findings.*51 locations/);
});
for (const [name, Provider, invoke] of [
    ["CodeLens", Lens, (p: any) => p.provideCodeLenses(doc, token)],
    ["Hover", Hover, (p: any) => p.provideHover(doc, new Position(1, 1), token)],
] as const) {
    test(`${name} does not leak findings from a different root sharing the file suffix`, () => {
        const p = new Provider(); p.setFindings([f({ filePath: "/other/src/A.cs", fullUri: "file:///other/src/A.cs" })]);
        assert.deepEqual(invoke(p), name === "CodeLens" ? [] : undefined);
    });
}
test("Hover respects multi-line ending columns and clears replacement findings", () => {
    const p = new Hover(); p.setFindings([f({ startColumn: 20, endLine: 4, endColumn: 3 })]);
    assert.equal(p.provideHover(doc, new Position(3, 5), token), undefined);
    assert.ok(p.provideHover(doc, new Position(3, 2), token));
    assert.equal(p.provideHover(doc, new Position(1, 18), token), undefined);
    p.clear(); assert.equal(p.provideHover(doc, new Position(3, 2), token), undefined);
});
test("Hover combines findings with untrusted text/code and suppresses DG010 quick fix", () => {
    const p = new Hover();
    p.setFindings([f({ ruleId: "DG010", severity: "error", quickFixAvailable: true, quickFixTitle: "hidden",
        properties: { migration: "[x](command:evil)", keyword: "SYSDATE", operator: "(+)" } }),
        f({ quickFixAvailable: true, quickFixTitle: "Review" })]);
    const md = p.provideHover(doc, new Position(1, 1), token).contents as MarkdownString;
    assert.equal(md.isTrusted, false); assert.equal(md.supportHtml, false);
    assert.ok(md.parts.some(x => x.kind === "text" && x.text === "[x](command:evil)"));
    assert.ok(md.parts.some(x => x.kind === "sql" && x.text === "SYSDATE"));
    assert.ok(md.parts.some(x => x.kind === "sql" && x.text === "(+)"));
    assert.ok(md.parts.some(x => x.text.includes("---")));
    assert.ok(md.parts.some(x => x.text.includes("Review")));
    assert.ok(md.parts.every(x => !x.text.includes("hidden")));
});
test("Hover renders rule IDs and quick-fix titles as text, never dynamic Markdown", () => {
    const p = new Hover(); const payload = "[click](command:evil)";
    p.setFindings([f({ ruleId: payload, quickFixAvailable: true, quickFixTitle: payload })]);
    const md = p.provideHover(doc, new Position(1, 1), token).contents as MarkdownString;
    assert.ok(md.parts.filter(x => x.kind === "markdown").every(x => !x.text.includes(payload)));
    assert.equal(md.parts.filter(x => x.kind === "text" && x.text.includes(payload)).length, 2);
});
test("Findings tree filters actual state by rule/message/path and severity, preserves copies and clears", async () => {
    const p = new Tree(); let changes = 0; p.onDidChangeTreeData(() => changes++);
    assert.deepEqual(await p.getChildren(), []);
    const values = [f(), f({ id: "err", ruleId: "DG001", severity: "error", filePath: "/repo/B.cs", message: "Mismatch" }),
        f({ id: "info", severity: "information" })];
    p.setFindings(values); values.pop(); assert.equal(p.getAllFindingsCount(), 3);
    p.getFindings().pop(); assert.equal(p.getFindings().length, 3);
    for (const query of [" DG001 ", "mismatch", "/repo/"]) {
        p.setFilter(query, "error"); assert.deepEqual(p.getFindings().map((x: any) => x.id), ["err"]);
    }
    p.setFilter("", "warning"); assert.equal(p.getFindings().length, 1);
    p.setFilter(); const groups = await p.getChildren(); assert.equal(groups.length, 3);
    for (const group of groups) {
        const children = await p.getChildren(group); assert.equal(children.length, 1);
        assert.equal(p.getTreeItem(children[0]), children[0]);
        assert.equal(children[0].command.command, "vscode.open");
        assert.equal(children[0].command.arguments[1].selection.start.line, 1);
        assert.equal(children[0].contextValue, "finding");
    }
    p.clear(); assert.equal(p.getAllFindingsCount(), 0); assert.deepEqual(await p.getChildren(), []);
    assert.ok(changes >= 7);
});
test("Findings tree rule grouping shows quick-fix context and replaces old groups", async () => {
    const p = new Tree(); p.setGroupingMode("rule");
    p.setFindings([f({ quickFixAvailable: true, quickFixTitle: "Review" }), f({ id: "second" })]);
    const groups = await p.getChildren(); assert.equal(groups[0].label, "DG017"); assert.equal(groups[0].count, 2);
    assert.equal((await p.getChildren(groups[0]))[0].contextValue, "findingWithQuickFix");
    p.setFindings([f({ ruleId: "DG002" })]); assert.deepEqual((await p.getChildren()).map((x: any) => x.label), ["DG002"]);
});
test("Findings tree file grouping keeps same basenames from separate folders distinct", async () => {
    const p = new Tree(); p.setGroupingMode("file");
    p.setFindings([f(), f({ id: "other", filePath: "/other/A.cs" })]);
    const groups = await p.getChildren(); assert.equal(groups.length, 2);
    assert.deepEqual((await Promise.all(groups.map((g: any) => p.getChildren(g)))).map(x => x.length), [1, 1]);
});
test("Quick fixes accept structured diagnostic codes but remain manual review actions", () => {
    const p = new Fix();
    const diagnostics = ["DG001", "DG002", "DG017", "DG018", "DG999"].map(code => ({ code: { value: code, target: {} }, message: "finding", source: "DataGuard", range: new Range(1, 0, 1, 8) }));
    const actions = p.provideCodeActions(doc, new Range(1, 0, 1, 8), { diagnostics }, token);
    assert.equal(actions.length, 5);
    actions.forEach((a: any, i: number) => {
        assert.equal(a.isPreferred, false); assert.match(a.title, /Review/);
        assert.deepEqual(a.diagnostics, [diagnostics[i]]);
        assert.equal(a.command.arguments[2], diagnostics[i].code.value);
    });
    assert.deepEqual(p.provideCodeActions(doc, new Range(1, 0, 1, 8), { diagnostics }, { isCancellationRequested: true }), []);
});
test("Quick fixes ignore unrelated diagnostics and unknown non-DataGuard rule codes", () => {
    const p = new Fix();
    const diagnostics = [{ code: "CS001", message: "x" }, { code: "CS002", source: "DataGuard", message: "x" }, { message: "DataGuard note" }];
    assert.deepEqual(p.provideCodeActions(doc, new Range(0, 0, 0, 0), { diagnostics }, token), []);
    const action = p.provideCodeActions(doc, new Range(0, 0, 0, 0), { diagnostics: [{ code: "DG017", message: "x", range: new Range(0, 0, 0, 1) }] }, token);
    assert.equal(action.length, 1);
});
test("Quick-fix remediation never fabricates columns or reports an unperformed contract alignment", async () => {
    notices.length = mutations.length = opened.length = 0;
    for (const code of ["DG017", "DG001", "DG002", "DG018", "DG999", "CS001"]) {
        assert.equal(await Fix.applyRemediation(doc.uri, new Range(1, 0, 1, 8), code), false);
    }
    assert.deepEqual(mutations, []); assert.deepEqual(opened, []);
    assert.ok(notices.length > 0); assert.ok(notices.every(x => !/applied|aligned/i.test(x)));
});

test("SARIF parser feeds correct multi-line spans into hover and tree navigation", async () => {
    const { parseSarifToFindings } = require("./redaction") as typeof import("./redaction");
    const findings = parseSarifToFindings({ runs: [{ results: [{ ruleId: "DG017", locations: [{ physicalLocation: {
        artifactLocation: { uri: "A.cs" }, region: { startLine: 2, startColumn: 20, endLine: 4, endColumn: 3 },
    } }] }] }] }, "/src", () => "/src/A.cs");
    const hover = new Hover(); hover.setFindings(findings);
    assert.equal(hover.provideHover(doc, new Position(3, 3), token), undefined);
    assert.ok(hover.provideHover(doc, new Position(3, 2), token));
    const tree = new Tree(); tree.setFindings(findings);
    const child = (await tree.getChildren((await tree.getChildren())[0]))[0];
    assert.deepEqual(child.command.arguments[1].selection.end, new Position(3, 2));
});
test("shared quick-fix metadata promises manual review rather than an automated repair", () => {
    const { getQuickFixInfo } = require("./redaction") as typeof import("./redaction");
    for (const code of ["DG001", "DG002", "DG017", "DG018"]) {
        assert.match(getQuickFixInfo(code).title!, /Review/);
    }
});
