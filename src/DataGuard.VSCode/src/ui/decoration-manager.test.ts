/** Tests the production manager with a recording API boundary, not an editor host. */
import assert from "node:assert/strict";
import test from "node:test";
import Module from "node:module";
import type { FindingItem } from "./redaction";
class Range {
    constructor(public startLine: number, public startColumn: number,
        public endLine: number, public endColumn: number) {}
}
class Token { constructor(public id: string) {} }
const types: { disposed: number; dispose(): void }[] = [];
function editor(filePath: string) {
    return {
        document: { uri: { fsPath: filePath } },
        calls: [] as { type: unknown; ranges: Range[] }[],
        setDecorations(type: unknown, ranges: Range[]) { this.calls.push({ type, ranges }); },
    };
}
const editors = [editor("/src/A.cs"), editor("/src/B.cs")];
const vscode = {
    Range, ThemeIcon: Token, ThemeColor: Token, OverviewRulerLane: { Right: 4 },
    window: {
        visibleTextEditors: editors,
        createTextEditorDecorationType() {
            const type = { disposed: 0, dispose() { this.disposed++; } };
            types.push(type);
            return type;
        },
    },
};
const loader = Module as unknown as { _load: (id: string, ...args: unknown[]) => unknown };
const originalLoad = loader._load;
let Manager: typeof import("./decoration-manager").DataGuardDecorationManager;
try {
    loader._load = function (id, ...args) {
        return id === "vscode" ? vscode : originalLoad.call(this, id, ...args);
    };
    Manager = (require("./decoration-manager") as typeof import("./decoration-manager")).DataGuardDecorationManager;
} finally { loader._load = originalLoad; }
const finding = (overrides: Partial<FindingItem> = {}): FindingItem => ({
    id: "d-1", ruleId: "DG010", message: "Test", severity: "warning",
    filePath: "/src/A.cs", fullUri: "file:///src/A.cs",
    startLine: 1, startColumn: 1, endLine: 1, endColumn: 5,
    quickFixAvailable: false, ...overrides,
});
function setup() {
    editors.forEach(e => { e.calls = []; });
    types.length = 0;
    return new Manager();
}

test("production decorations preserve a smaller end column on a later line", () => {
    const manager = setup();
    try {
        manager.applyFindings([finding({ startLine: 10, startColumn: 20, endLine: 12, endColumn: 3 })]);
        assert.deepEqual(editors[0].calls[1].ranges, [new Range(9, 19, 11, 2)]);
    } finally { manager.dispose(); }
});

test("production decorations convert first-line coordinates and clamp reversed same-line spans", () => {
    const manager = setup();
    try {
        manager.applyFindings([finding({ severity: "error" }), finding({ startColumn: 9, endColumn: 2 })]);
        assert.deepEqual(editors[0].calls[0], { type: types[0], ranges: [new Range(0, 0, 0, 4)] });
        assert.deepEqual(editors[0].calls[1], { type: types[1], ranges: [new Range(0, 8, 0, 8)] });
        assert.deepEqual(editors[1].calls.map(c => c.ranges), [[], []]);
    } finally { manager.dispose(); }
});

test("production decorations replace stale results in every visible editor and clear both severities", () => {
    const manager = setup();
    try {
        manager.applyFindings([finding(), finding({ filePath: "/src/B.cs", severity: "error" })]);
        assert.equal(editors[1].calls[0].ranges.length, 1);
        manager.applyFindings([]);
        manager.clear();
        for (const e of editors) {
            assert.equal(e.calls.length, 6);
            assert.deepEqual(e.calls.slice(2).map(c => c.ranges), [[], [], [], []]);
        }
    } finally { manager.dispose(); }
});

test("production decorations dispose each type once and ignore later updates and clears", () => {
    const manager = setup();
    manager.dispose();
    manager.dispose();
    manager.applyFindings([finding()]);
    manager.clear();
    assert.deepEqual(types.map(t => t.disposed), [1, 1]);
    assert.ok(editors.every(e => e.calls.length === 0));
});
