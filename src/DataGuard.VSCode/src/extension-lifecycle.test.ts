/** Real extension entrypoints with editor/provider/process doubles; no editor host or CLI. */
import assert from "node:assert/strict";
import test, { TestContext } from "node:test";
import Module from "node:module";
import { spawn as spawnFixture, ChildProcess } from "node:child_process";
import { once } from "node:events";
import { createHash } from "node:crypto";
import { EventEmitter } from "node:events";
import { promises as fs } from "node:fs";
import * as os from "node:os";
import * as path from "node:path";

class Resource { disposed = false; dispose() { this.disposed = true; } }
class Provider extends Resource {
    static providedCodeActionKinds = [];
    findings: any[] = []; report: any = null;
    setFindings(v: any[]) { this.findings = v; }
    registerFindings(v: any[]) { this.findings = v; }
    applyFindings(v: any[]) { this.findings = v; }
    getFindings() { return this.findings; }
    setScanReport(v: any) { this.report = v; }
    clear() { this.findings = []; this.report = null; }
    refreshCount = 0; grouping: string | undefined;
    refresh() { this.refreshCount++; }
    setGroupingMode(v: string) { this.grouping = v; }
}
class Child extends EventEmitter {
    stdout = new EventEmitter(); stderr = new EventEmitter();
    // No pid: tests never target an OS process, even if cancellation is broken.
    pid = undefined; exitCode: number | null = null; signalCode: string | null = null; killed = false;
    close(code = 0) { this.exitCode = code; this.emit("exit", code); this.emit("close", code); }
}
class Range {
    start: { line: number; character: number }; end: { line: number; character: number };
    constructor(sl: number, sc: number, el: number, ec: number) {
        this.start = { line: sl, character: sc }; this.end = { line: el, character: ec };
    }
}
function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>(r => { resolve = r; });
    return { promise, resolve };
}
async function until(predicate: () => boolean) {
    for (let i = 0; i < 200; i++) {
        if (predicate()) { return; }
        await new Promise(r => setTimeout(r, 5));
    }
    assert.fail("Expected lifecycle event did not occur");
}
interface LspOptions { serverProcess?: ChildProcess; dispose?: () => Promise<void>; construct?: () => void; start?: () => Promise<void>; stop?: () => Promise<void>; delayArtifact?: Promise<void>; invalidArtifact?: boolean; }
async function harness(t: TestContext, lsp?: LspOptions) {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), "extension-lifecycle-test-"));
    const workspace = path.join(root, "workspace"); await fs.mkdir(workspace);
    if (lsp) {
        await fs.mkdir(path.join(root, "server"));
        const artifact = Buffer.from("synthetic dll, never executed");
        await fs.writeFile(path.join(root, "server", "DataGuard.LanguageServer.dll"), artifact);
        await fs.writeFile(path.join(root, "server", "manifest.json"), JSON.stringify({
            schemaVersion: 1, artifact: "DataGuard.LanguageServer.dll",
            sha256: lsp.invalidArtifact ? "bad" : createHash("sha256").update(artifact).digest("hex"),
        }));
    }
    const commands = new Map<string, (...args: any[]) => any>();
    const providers = new Map<string, Provider>(); const registrations: Resource[] = [];
    const notices: string[] = []; const lines: string[] = [];
    const status = Object.assign(new Resource(), { text: "", show() {} });
    const output = Object.assign(new Resource(), { appendLine(v: string) { lines.push(v); }, clear() { lines.length = 0; }, show() {} });
    const values = new Map<string, any[]>();
    const diagnostics = Object.assign(new Resource(), { clear() { values.clear(); }, set(uri: any, v: any[]) { values.set(uri.fsPath, v); } });
    const dashboard = Object.assign(new Provider(), { update(this: Provider, v: any[]) { this.findings = v; }, updateScanReport(this: Provider, v: any) { this.report = v; } });
    const config: Record<string, any> = {};
    const uri = (p: string) => ({ fsPath: p, toString: () => `file://${p}` });
    const register = () => { const r = new Resource(); registrations.push(r); return r; };
    let configChanged: (e: any) => void = () => {};
    const api = {
        Range, Uri: { file: uri }, StatusBarAlignment: { Left: 1 },
        DiagnosticSeverity: { Error: 0, Warning: 1, Information: 2, Hint: 3 },
        Diagnostic: class { constructor(public range: Range, public message: string, public severity: number) {} },
        commands: { registerCommand(name: string, fn: (...args: any[]) => any) { commands.set(name, fn); return register(); } },
        workspace: {
            isTrusted: true, workspaceFolders: [{ name: "workspace", uri: uri(workspace) }],
            getConfiguration: () => ({ get: (key: string, fallback?: any) => config[key] ?? fallback }),
            onDidChangeConfiguration(fn: (e: any) => void) { configChanged = fn; return register(); },
        },
        window: {
            createStatusBarItem: () => status, createOutputChannel: () => output,
            registerTreeDataProvider: register,
            showInputBox: async (_options: any): Promise<string | undefined> => undefined,
            showQuickPick: async (_items: any[]): Promise<any> => undefined,
            showWarningMessage: async (s: string): Promise<string | undefined> => { notices.push(s); return undefined; },
            showErrorMessage: async (s: string) => { notices.push(s); },
            showInformationMessage: async (s: string) => { notices.push(s); },
        },
        languages: { createDiagnosticCollection: () => diagnostics, registerCodeActionsProvider: register,
            registerCodeLensProvider: register, registerHoverProvider: register },
    };
    const stored: any[] = [], remediations: any[] = [], dashboards: any[] = [];
    const children: { child: Child; args: string[]; options: any }[] = [];
    const io = { ...fs };
    if (lsp?.delayArtifact) {
        io.readFile = (async (...args: any[]) => {
            if (String(args[0]).endsWith("DataGuard.LanguageServer.dll")) { await lsp.delayArtifact; }
            return (fs.readFile as any)(...args);
        }) as typeof fs.readFile;
    }
    const clients: Client[] = [];
    class Client {
        startCalls = 0; stopCalls = 0; disposeCalls = 0;
        get serverProcess() { return lsp?.serverProcess; }
        async dispose() { this.disposeCalls++; await lsp?.dispose?.(); }
        constructor() { clients.push(this); lsp?.construct?.(); }
        async start() { this.startCalls++; await lsp?.start?.(); }
        async stop() { this.stopCalls++; await lsp?.stop?.(); }
    }
    const context = { subscriptions: [] as Resource[], extensionUri: uri(root),
        asAbsolutePath: (p: string) => path.join(root, p), secrets: { async get(): Promise<string | undefined> { return undefined; }, async store(...v: any[]) { stored.push(["store", ...v]); }, async delete(...v: any[]) { stored.push(["delete", ...v]); } } };
    let spawnError: Error | undefined;
    const replacements: Record<string, any> = {
        vscode: api, "vscode-languageclient/node": { LanguageClient: Client },
        os: { ...os, tmpdir: () => root }, fs: { promises: io },
        child_process: { spawn: (_command: string, args: string[], options: any) => {
            if (spawnError) { throw spawnError; }
            const child = new Child(); children.push({ child, args, options }); return child;
        } },
        "./ui/dashboard-panel": { DataGuardDashboardPanel: { currentPanel: dashboard, createOrShow(...v: any[]) { dashboards.push(v); } } },
    };
    for (const [module, name] of [
        ["findings-tree-provider", "DataGuardFindingsTreeProvider"], ["sql-queries-tree-provider", "DataGuardSqlQueriesTreeProvider"],
        ["quick-fix-provider", "DataGuardQuickFixProvider"], ["codelens-provider", "DataGuardCodeLensProvider"],
        ["decoration-manager", "DataGuardDecorationManager"], ["hover-provider", "DataGuardHoverProvider"],
    ]) { replacements[`./ui/${module}`] = { [name]: class extends Provider {
        constructor() { super(); providers.set(module, this); }
        static async applyRemediation(...v: any[]) { remediations.push(v); }
    } }; }
    const loader = Module as unknown as { _load: (id: string, ...args: any[]) => any };
    const original = loader._load;
    let extension: typeof import("./extension");
    try {
        delete require.cache[require.resolve("./extension")];
        loader._load = function (id, ...args) { return replacements[id] ?? original.call(this, id, ...args); };
        extension = require("./extension");
    } finally { loader._load = original; }
    extension!.activate(context as any);
    // Wait for the asynchronous missing-artifact check so teardown cannot recreate output.
    if (!lsp || (lsp.invalidArtifact && !lsp.delayArtifact)) { await until(() => lines.some(v => v.includes("LSP is disabled"))); }
    else if (!lsp.delayArtifact) { await until(() => clients.length > 0); }
    t.after(async () => {
        for (const { child } of children) { if (child.exitCode === null) { child.close(); } }
        await extension.deactivate();
        context.subscriptions.forEach(r => r.dispose());
        await fs.rm(root, { recursive: true, force: true });
    });
    const command = (name = "runValidation") => Promise.resolve(commands.get(`dataguard.${name}`)!());
    const start = async (name = "runValidation") => {
        const index = children.length; const done = command(name);
        await until(() => children.length > index && children[index].child.listenerCount("close") > 0);
        return { ...children[index], done };
    };
    const sarif = (ruleId = "DG017") => ({ runs: [{ results: [{ ruleId, level: "warning", message: { text: "Synthetic finding" },
        locations: [{ physicalLocation: { artifactLocation: { uri: "Example.cs" }, region: { startLine: 2, startColumn: 20, endLine: 4, endColumn: 3 } } }] }] }] });
    const finish = async (run: Awaited<ReturnType<typeof start>>, payload: any = sarif(), ack = true, code = 0) => {
        const outputPath = run.args[run.args.indexOf("--output") + 1];
        if (payload !== null) { await fs.writeFile(outputPath, typeof payload === "string" ? payload : JSON.stringify(payload)); }
        if (ack) { run.child.stderr.emit("data", "ide-safe: active\n"); }
        run.child.close(code); await run.done;
    };
    return { root, workspace, api, config, io, context, clients, stored, remediations, dashboards, extension: extension!, commands, registrations, providers, dashboard, status,
        diagnostics, values, output, lines, notices, children, command, start, finish, sarif,
        changeConfig: () => configChanged({ affectsConfiguration: () => true }),
        failSpawn: (e: Error) => { spawnError = e; } };
}

test("activation disposes owned decoration resources even after deactivate clears globals", async t => {
    const h = await harness(t);
    assert.equal(h.commands.size, 17);
    await h.extension.deactivate(); h.context.subscriptions.forEach(r => r.dispose());
    assert.ok(h.providers.get("decoration-manager")!.disposed);
    assert.ok(h.registrations.every(r => r.disposed));
    assert.ok(h.status.disposed && h.output.disposed && h.diagnostics.disposed);
});

test("registered validation publishes correct multiline Problems coordinates and safe spawn options", async t => {
    const h = await harness(t); const run = await h.start(); await h.finish(run);
    const [diagnostic] = h.values.get(path.join(h.workspace, "Example.cs"))!;
    assert.deepEqual(diagnostic.range.start, { line: 1, character: 19 });
    assert.deepEqual(diagnostic.range.end, { line: 3, character: 2 });
    assert.equal(diagnostic.code, "DG017"); assert.equal(diagnostic.severity, 1);
    assert.equal(run.options.shell, false); assert.equal(run.options.cwd, h.workspace);
    assert.ok(run.args.includes("--ide-safe")); assert.ok(!run.args.includes("--allow-env-connection"));
    assert.equal(h.providers.get("hover-provider")!.findings.length, 1);
    await assert.rejects(fs.stat(path.dirname(run.args[run.args.indexOf("--output") + 1])), { code: "ENOENT" });
});

for (const [name, payload] of [["missing", null], ["invalid", "{invalid json"]] as const) {
    test(`${name} result clears previous findings from every UI surface`, async t => {
        const h = await harness(t); await h.finish(await h.start());
        assert.equal(h.dashboard.findings.length, 1);
        h.providers.get("sql-queries-tree-provider")!.report = { queriesFound: 1 };
        h.dashboard.report = { queriesFound: 1 };
        await h.finish(await h.start(), payload);
        assert.equal(h.values.size, 0);
        for (const p of h.providers.values()) { assert.equal(p.findings.length, 0); assert.equal(p.report, null); }
        assert.deepEqual(h.dashboard.findings, []); assert.equal(h.dashboard.report, null);
        assert.ok(!h.lines.join("\n").includes("previous Problems results were kept"));
    });
}
for (const outcome of ["success", "error", "cancel"] as const) {
    test(`process ${outcome} detaches output subscriptions`, async t => {
        const h = await harness(t); const run = await h.start();
        if (outcome === "error") { run.child.emit("error", Object.assign(new Error("missing"), { code: "ENOENT" })); await run.done;
            assert.ok(h.notices.some(v => v.includes("CLI was not found"))); }
        else { if (outcome === "cancel") { await h.command("cancelValidation"); } await h.finish(run); }
        assert.equal(run.child.stdout.listenerCount("data"), 0); assert.equal(run.child.stderr.listenerCount("data"), 0);
        assert.equal(run.child.listenerCount("close"), 0);
        const before = [...h.lines]; run.child.stdout.emit("data", "Error: late output\n"); assert.deepEqual(h.lines, before);
    });
}

test("missing IDE-safe acknowledgement discards a valid SARIF file", async t => {
    const h = await harness(t); await h.finish(await h.start(), h.sarif(), false);
    assert.equal(h.values.size, 0); assert.equal(h.dashboard.findings.length, 0);
    assert.ok(h.notices.some(v => v.toLowerCase().includes("ide-safe"))); assert.match(h.status.text, /error/);
});

test("cancellation during temporary-directory preparation prevents a late spawn", async t => {
    const h = await harness(t); const entered = deferred<void>(); const release = deferred<void>();
    h.io.mkdtemp = (async (...args: Parameters<typeof fs.mkdtemp>) => {
        entered.resolve(); await release.promise; return fs.mkdtemp(...args);
    }) as typeof fs.mkdtemp;
    const done = h.command(); await entered.promise;
    await h.command("cancelValidation"); release.resolve();
    // On the buggy version finish the unexpected child so the regression cannot hang.
    await Promise.race([done, until(() => h.children.length > 0).then(() => h.children[0].child.close())]);
    await done; assert.equal(h.children.length, 0);
    assert.deepEqual((await fs.readdir(h.root)).filter(p => p.startsWith("dataguard-")), []);
});

test("an old process error cannot replace the current run's status or notification", async t => {
    const h = await harness(t); const old = await h.start(); const current = await h.start();
    const before = [...h.notices]; const outputBefore = [...h.lines];
    old.child.stdout.emit("data", '{"event":"progress","message":"stale output"}\n');
    assert.deepEqual(h.lines, outputBefore);
    old.child.emit("error", new Error("old start failed")); await old.done;
    assert.match(h.status.text, /sync~spin/); assert.deepEqual(h.notices, before);
    await h.finish(current); assert.equal(h.values.size, 1);
});

test("a SARIF read finishing after replacement cannot publish stale diagnostics", async t => {
    const h = await harness(t); const old = await h.start();
    const entered = deferred<void>(); const release = deferred<void>(); const originalRead = h.io.readFile;
    const outputPath = old.args[old.args.indexOf("--output") + 1];
    h.io.readFile = (async (...args: any[]) => {
        const result = await (originalRead as any)(...args);
        if (args[0] === outputPath) { entered.resolve(); await release.promise; }
        return result;
    }) as typeof fs.readFile;
    const oldDone = h.finish(old, h.sarif("DG001")); await entered.promise;
    const current = await h.start(); await h.finish(current, h.sarif("DG017"));
    release.resolve(); await oldDone;
    assert.equal(h.values.get(path.join(h.workspace, "Example.cs"))![0].code, "DG017");
    assert.equal(h.dashboard.findings[0].ruleId, "DG017");
});

for (const guard of ["untrusted", "no workspace", "disabled", "invalid provider", "secret failure", "live consent"] as const) {
    test(`${guard} prevents command execution`, async t => {
        const h = await harness(t);
        if (guard === "untrusted") { h.api.workspace.isTrusted = false; }
        if (guard === "no workspace") { h.api.workspace.workspaceFolders = []; }
        if (guard === "disabled") { h.config.enabled = false; }
        if (guard === "invalid provider") { h.config.provider = "unsafe-provider"; }
        if (guard === "secret failure") { h.context.secrets.get = async () => { throw new Error("synthetic storage failure"); }; }
        await h.command(guard === "live consent" ? "verifyShape" : "runValidation");
        assert.equal(h.children.length, 0); assert.equal(h.notices.length, 1);
    });
}

test("a summary read finishing after replacement cannot overwrite the current scan", async t => {
    const h = await harness(t); const old = await h.start("scanProject");
    const entered = deferred<void>(); const release = deferred<void>(); const originalRead = h.io.readFile;
    const outputPath = old.args[old.args.indexOf("--output") + 1];
    h.io.readFile = (async (...args: any[]) => {
        const result = await (originalRead as any)(...args);
        if (args[0] === outputPath) { entered.resolve(); await release.promise; }
        return result;
    }) as typeof fs.readFile;
    const oldDone = h.finish(old, { filesScanned: 1, queriesFound: 2, queries: [] }); await entered.promise;
    const current = await h.start("scanProject"); await h.finish(current, { filesScanned: 3, queriesFound: 4, queries: [] });
    release.resolve(); await oldDone;
    assert.equal(h.dashboard.report.queriesFound, 4);
    assert.equal(h.providers.get("sql-queries-tree-provider")!.report.filesScanned, 3);
});

test("synchronous spawn failure clears prior results, reports failure and removes temporary output", async t => {
    const h = await harness(t); await h.finish(await h.start());
    h.failSpawn(new Error("synthetic launch failure")); await h.command();
    assert.match(h.status.text, /error/);
    assert.ok(h.notices.some(v => v.includes("synthetic launch failure")));
    assert.equal(h.values.size, 0); assert.equal(h.dashboard.findings.length, 0);
    assert.deepEqual((await fs.readdir(h.root)).filter(p => p.startsWith("dataguard-")), []);
});

test("clear command resets all published results and single-line diagnostics retain a nonempty range", async t => {
    const h = await harness(t); const data = h.sarif();
    data.runs[0].results[0].locations[0].physicalLocation.region = { startLine: 1, startColumn: 5, endLine: 1, endColumn: 2 };
    await h.finish(await h.start(), data);
    const [diagnostic] = h.values.get(path.join(h.workspace, "Example.cs"))!;
    assert.deepEqual(diagnostic.range.start, { line: 0, character: 4 });
    assert.deepEqual(diagnostic.range.end, { line: 0, character: 5 });
    await h.command("clearFindings");
    assert.equal(h.values.size, 0); assert.equal(h.dashboard.findings.length, 0);
    for (const p of h.providers.values()) { assert.equal(p.findings.length, 0); }
});

test("an older config lookup cannot erase the new run's output or report obsolete provider errors", async t => {
    const h = await harness(t); const entered = deferred<void>(); const release = deferred<void>();
    const originalStat = h.io.stat; let first = true;
    h.io.stat = (async (...args: any[]) => {
        if (first && args[0] === path.join(h.workspace, ".dataguard.yml")) {
            first = false; entered.resolve(); await release.promise;
        }
        return (originalStat as any)(...args);
    }) as typeof fs.stat;
    const old = h.command(); await entered.promise;
    const current = await h.start();
    const outputBefore = [...h.lines]; const noticesBefore = [...h.notices];
    h.config.provider = "obsolete-invalid-setting";
    release.resolve(); await old;
    assert.deepEqual(h.notices, noticesBefore); assert.deepEqual(h.lines, outputBefore);
    assert.equal(h.children.length, 1); await h.finish(current);
});

for (const stage of ["workspace", "secret", "consent"] as const) {
    test(`cancel while awaiting ${stage} prevents execution after the response arrives`, async t => {
        const h = await harness(t); const entered = deferred<void>(); const release = deferred<any>();
        if (stage === "workspace") {
            h.api.workspace.workspaceFolders.push({ name: "second", uri: h.api.Uri.file(path.join(h.root, "second")) });
            h.api.window.showQuickPick = async () => { entered.resolve(); return release.promise; };
        } else if (stage === "secret") {
            h.context.secrets.get = async () => { entered.resolve(); return release.promise; };
        } else { h.api.window.showWarningMessage = async () => { entered.resolve(); return release.promise; }; }
        const done = h.command(stage === "consent" ? "refreshSnapshot" : "runValidation");
        await entered.promise; await h.command("cancelValidation");
        release.resolve(stage === "workspace" ? { folder: h.api.workspace.workspaceFolders[0] } : stage === "consent" ? "Continue" : undefined);
        await Promise.race([done, until(() => h.children.length > 0).then(() => h.children[0].child.close())]);
        await done; assert.equal(h.children.length, 0);
    });
}
for (const interruption of ["replacement", "deactivation"] as const) {
    test(`${interruption} invalidates an old SecretStorage continuation`, async t => {
        const h = await harness(t); const entered = deferred<void>(); const release = deferred<undefined>();
        let first = true;
        h.context.secrets.get = async () => { if (first) { first = false; entered.resolve(); return release.promise; } return undefined; };
        const old = h.command(); await entered.promise;
        if (interruption === "replacement") { await h.finish(await h.start()); } else { await h.extension.deactivate(); }
        const expected = h.children.length; release.resolve(undefined);
        await Promise.race([old, until(() => h.children.length > expected).then(() => h.children[expected].child.close())]);
        await old; assert.equal(h.children.length, expected);
        if (interruption === "replacement") { assert.equal(h.values.size, 1); }
    });
}

test("deactivate awaits LSP stop and subscription disposal does not stop twice", async t => {
    const release = deferred<void>(); const h = await harness(t, { stop: () => release.promise });
    let finished = false; const shutdown = h.extension.deactivate().then(() => { finished = true; });
    await until(() => h.clients[0].stopCalls > 0);
    await new Promise(r => setImmediate(r)); const finishedBeforeStop = finished;
    release.resolve(); await shutdown;
    h.context.subscriptions.forEach(r => r.dispose()); await new Promise(r => setImmediate(r));
    assert.equal(finishedBeforeStop, false); assert.equal(h.clients[0].stopCalls, 1);
});

test("deactivate during artifact verification prevents late LSP start and output recreation", async t => {
    const release = deferred<void>(); const h = await harness(t, { delayArtifact: release.promise });
    const shutdown = h.extension.deactivate(); release.resolve(); await shutdown;
    await new Promise(r => setTimeout(r, 20));
    assert.equal(h.clients.length, 0); assert.equal(h.lines.length, 0);
});

test("deactivate during LSP start waits for startup before stopping exactly once", async t => {
    const release = deferred<void>(); const h = await harness(t, { start: () => release.promise });
    const shutdown = h.extension.deactivate(); await new Promise(r => setImmediate(r));
    const prematureStops = h.clients[0].stopCalls;
    release.resolve(); await shutdown; h.context.subscriptions.forEach(r => r.dispose());
    assert.equal(prematureStops, 0); assert.equal(h.clients[0].stopCalls, 1);
});

test("LSP stop failure still releases editor resources and is handled by deactivate", async t => {
    const h = await harness(t, { stop: async () => { throw new Error("synthetic stop failure"); } });
    await h.extension.deactivate(); h.context.subscriptions.forEach(r => r.dispose());
    assert.ok(h.status.disposed && h.diagnostics.disposed);
    assert.equal(h.clients[0].stopCalls, 1);
});

test("invalid LSP integrity manifest never starts a client", async t => {
    const h = await harness(t, { invalidArtifact: true }); assert.equal(h.clients.length, 0);
    assert.ok(h.lines.some(v => v.includes("integrity verification")));
});

for (const stalled of ["start", "stop"] as const) {
    test(`deactivate bounds stalled LSP ${stalled}, releases UI and cleans up late settlement`, async t => {
        const release = deferred<void>(); const h = await harness(t, { [stalled]: () => release.promise });
        t.mock.timers.enable({ apis: ["setTimeout"] });
        let finished = false; const shutdown = h.extension.deactivate().then(() => { finished = true; });
        // Do not settle the client operation until after the shutdown deadline.
        t.mock.timers.tick(1000); await new Promise(r => setImmediate(r));
        const finishedAtDeadline = finished; const disposedAtDeadline = h.status.disposed && h.diagnostics.disposed;
        release.resolve(); await shutdown; await new Promise(r => setImmediate(r));
        assert.equal(finishedAtDeadline, true); assert.equal(disposedAtDeadline, true);
        assert.equal(h.clients[0].stopCalls, 1);
    });
}

test("LSP constructor failure is reported and cannot reject deactivation cleanup", async t => {
    const h = await harness(t, { construct: () => { throw new Error("synthetic constructor failure"); } });
    await assert.doesNotReject(h.extension.deactivate());
    assert.ok(h.status.disposed && h.diagnostics.disposed);
    assert.ok(h.lines.some(v => v.includes("synthetic constructor failure")));
});

test("LSP startup rejection is redacted and subscription-first disposal stops only once", async t => {
    const h = await harness(t, { start: async () => { throw new Error("Password=SyntheticSecret; startup failed"); } });
    assert.ok(h.lines.some(v => v.includes("Failed to start language server")));
    assert.doesNotMatch(h.lines.join("\n"), /SyntheticSecret/);
    h.context.subscriptions.forEach(r => r.dispose());
    await h.extension.deactivate(); assert.equal(h.clients[0].stopCalls, 1);
});

test("cancelled SecretStorage rejection cannot show an obsolete error", async t => {
    const h = await harness(t); const entered = deferred<void>(); const release = deferred<void>();
    h.context.secrets.get = async () => { entered.resolve(); await release.promise; throw new Error("synthetic storage rejection"); };
    const done = h.command(); await entered.promise; await h.command("cancelValidation");
    const before = [...h.notices]; release.resolve(); await done;
    assert.deepEqual(h.notices, before); assert.equal(h.children.length, 0);
});

test("late failed integrity verification cannot recreate the output channel after shutdown", async t => {
    const release = deferred<void>(); const h = await harness(t, { delayArtifact: release.promise, invalidArtifact: true });
    const shutdown = h.extension.deactivate(); release.resolve(); await shutdown;
    assert.equal(h.clients.length, 0); assert.deepEqual(h.lines, []);
});

test("late LSP stop rejection after the deadline cannot write to disposed output", async t => {
    const release = deferred<void>(); const h = await harness(t, { stop: async () => { await release.promise; throw new Error("late stop failure"); } });
    await h.finish(await h.start());
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const shutdown = h.extension.deactivate(); t.mock.timers.tick(1000); await shutdown;
    const before = [...h.lines]; release.resolve(); await new Promise(r => setImmediate(r));
    assert.deepEqual(h.lines, before); assert.ok(h.output.disposed);
});

test("stalled LSP deadline disposes client and terminates only its exposed synthetic child", async t => {
    const child = spawnFixture(process.execPath, ["-e", "setInterval(() => {}, 1000)"], { stdio: "pipe" });
    const exited = once(child, "exit"); await once(child, "spawn");
    t.after(async () => { if (child.exitCode === null && child.signalCode === null) { child.kill("SIGKILL"); } await exited; });
    const release = deferred<void>(); const h = await harness(t, { serverProcess: child, start: () => release.promise,
        dispose: async () => { throw new Error("Client is not running and cannot be stopped while Starting"); } });
    t.mock.timers.enable({ apis: ["setTimeout"] });
    const shutdown = h.extension.deactivate(); t.mock.timers.tick(1000); await shutdown;
    const disposalCount = h.clients[0].disposeCalls; const requestedKill = child.killed;
    release.resolve(); await new Promise(r => setImmediate(r));
    assert.equal(disposalCount, 1); assert.equal(requestedKill, true);
    const [code, signal] = await exited;
    assert.ok(code !== null || signal !== null); assert.ok(h.status.disposed);
});

test("configuration commands store, remove and cancel credentials without invoking the CLI", async t => {
    const h = await harness(t); let prompt: any;
    h.api.window.showInputBox = async options => { prompt = options; return "synthetic credential"; };
    await h.command("configureConnection"); assert.equal(prompt.password, true);
    assert.equal(h.stored[0][0], "store"); assert.equal(h.stored[0][2], "synthetic credential");
    h.api.window.showInputBox = async () => " "; await h.command("configureConnection");
    assert.deepEqual(h.stored[1], ["delete", h.stored[0][1]]);
    h.api.window.showInputBox = async () => undefined; await h.command("configureConnection"); assert.equal(h.stored.length, 2);
    h.api.workspace.workspaceFolders = []; await h.command("configureConnection");
    assert.match(h.notices.at(-1)!, /workspace folder/); assert.equal(h.children.length, 0);
});

for (const configPath of ["../outside.yml", path.resolve("/absolute.yml")]) {
    test(`unsafe config path ${configPath} never spawns a process`, async t => {
        const h = await harness(t); h.config.configPath = configPath; await h.command();
        assert.equal(h.children.length, 0); assert.match(h.notices[0], /configPath/);
    });
}

test("existing config and explicit synthetic SecretStorage credential produce safe command arguments", async t => {
    const h = await harness(t); await fs.writeFile(path.join(h.workspace, ".dataguard.yml"), "# synthetic");
    h.context.secrets.get = async () => "Server=synthetic;Password=Fixture;";
    h.config.provider = " ORACLE "; h.config.connectionEnv = " CUSTOM_CONNECTION ";
    const run = await h.start();
    assert.equal(run.args[run.args.indexOf("--config") + 1], path.join(h.workspace, ".dataguard.yml"));
    assert.equal(run.args[run.args.indexOf("--provider") + 1], "oracle");
    assert.ok(run.args.includes("--allow-env-connection"));
    assert.equal(run.options.env.DATAGUARD_CONNECTION_STRING, "Server=synthetic;Password=Fixture;");
    assert.ok(!run.args.join(" ").includes("Fixture")); await h.finish(run);
    h.context.secrets.get = async () => "  "; const blank = await h.start();
    assert.ok(!blank.args.includes("--allow-env-connection")); await h.finish(blank);
});

test("workspace selection cancellation and configuration notification preserve command boundaries", async t => {
    const h = await harness(t); h.api.workspace.workspaceFolders.push({ name: "second", uri: h.api.Uri.file(path.join(h.root, "second")) });
    await h.command(); assert.equal(h.children.length, 0); assert.match(h.notices.at(-1)!, /workspace folder/);
    h.changeConfig(); assert.equal(h.providers.get("codelens-provider")!.refreshCount, 1);
});

test("finding actions use only current actionable findings and forward exact ranges", async t => {
    const h = await harness(t); const apply = h.commands.get("dataguard.applyQuickFix")!;
    await apply(); await apply("unknown");
    await h.finish(await h.start()); await apply(h.dashboard.findings[0].id);
    // DG017 guidance is eligible; the production quick-fix module separately verifies it never edits code.
    assert.equal(h.remediations.length, 1); assert.equal(h.remediations[0][2], "DG017");
    assert.equal(h.remediations[0][1].end.character, 2);
    const f = h.dashboard.findings[0]; f.quickFixAvailable = false; await apply({ finding: f });
    assert.equal(h.remediations.length, 1); assert.match(h.notices.at(-1)!, /No automated Quick-Fix/);
    await h.command("groupBySeverity"); assert.equal(h.providers.get("findings-tree-provider")!.grouping, "severity");
    await h.command("groupByRule"); assert.equal(h.providers.get("findings-tree-provider")!.grouping, "rule");
    await h.command("groupByFile"); assert.equal(h.providers.get("findings-tree-provider")!.grouping, "file");
    await h.command("openDashboard"); assert.equal(h.dashboards[0][1].length, 1);
    await h.commands.get("dataguard.applyQuickFixInternal")!("uri", "range", "DG003");
    assert.deepEqual(h.remediations.at(-1), ["uri", "range", "DG003"]);
});

test("summary normalization preserves verified query mappings and safely defaults optional fields", async t => {
    const h = await harness(t); h.api.window.showWarningMessage = async () => "Continue";
    const run = await h.start("verifyShape");
    await h.finish(run, { filesScanned: 2, queriesVerified: 3, connectionsFound: 1, violationsCount: 4, connections: [], results: [
        { sql: "SELECT " + "COLUMN, ".repeat(20), status: "verified", tables: ["T"], targetType: "Dto", columns: ["ID"], properties: ["Id"],
            unmappedColumns: ["Other"], unmappedProperties: ["Extra"], location: { file: "Example.cs", line: 3 }, targetTypeLocation: { file: "Dto.cs", line: 4 } },
        { sql: "SELECT 1", mappingStatus: "matched", operation: "Read", action: "custom", location: { file: "Example.cs" }, targetTypeLocation: { file: "Dto.cs" } },
        null,
    ] });
    const report = h.dashboard.report; assert.equal(report.queriesFound, 3); assert.equal(report.filesScanned, 2); assert.equal(report.violationsCount, 4);
    assert.equal(report.queries[0].mappingStatus, "matched"); assert.equal(report.queries[0].action, "shape-check");
    assert.equal(report.queries[1].action, "custom"); assert.equal(report.queries[1].location.line, 1);
    assert.equal(report.queries[2].mappingStatus, "partial"); assert.equal(report.queries[2].action, "untyped-query");
    assert.ok(h.lines.some(v => v.includes("..."))); assert.ok(h.lines.some(v => v.includes('"SELECT 1"')));
    await h.finish(await h.start("scanProject"), {}); assert.equal(h.dashboard.report.queriesFound, 0);
    await h.finish(await h.start("refreshQueries"), "not json"); assert.equal(h.dashboard.report, null);
});

test("SARIF defaults, missing locations and outside paths cannot create invalid Problems entries", async t => {
    const h = await harness(t); const payload = { runs: [{ results: [
        {}, { locations: [{ physicalLocation: { artifactLocation: { uri: "../outside.cs" } } }] },
        ...["error", "note", "none", undefined].map(level => ({ level, locations: [{ physicalLocation: { artifactLocation: { uri: "Example.cs" } } }] })),
    ] }] };
    await h.finish(await h.start(), payload);
    const values = h.values.get(path.join(h.workspace, "Example.cs"))!;
    assert.equal(values.length, 4); assert.deepEqual(values.map(v => v.severity), [0, 2, 3, 3]);
    assert.equal(values[0].code, "DG000"); assert.match(values[0].message, /contract violation/);
    assert.deepEqual(values[0].range.end, { line: 0, character: 1 });
    assert.ok(h.lines.some(v => v.includes("Ignored external SARIF location")));
    await h.finish(await h.start(), {}); assert.equal(h.values.size, 0);
});

for (const mode of ["warning", "error", "assessment"] as const) {
    test(`${mode} CLI progress determines status and reports redacted error output`, async t => {
        const h = await harness(t); const run = await h.start(mode === "assessment" ? "assess" : "runValidation");
        run.child.stderr.emit("data", "\nide-safe: active\r\n");
        run.child.stdout.emit("data", JSON.stringify({ Kind: "Summary", Phase: "done", Data: { ErrorCount: 0, WarningCount: mode === "warning" ? 1 : 0, GroundTruth: "snapshot" } }) + "\n");
        if (mode === "error") { run.child.stdout.emit("data", "Error: Password=SyntheticSecret; failed\n"); }
        await h.finish(run, h.sarif(), false, mode === "error" ? 2 : 0);
        assert.match(h.status.text, mode === "warning" ? /warning/ : mode === "error" ? /error/ : /shield/);
        assert.ok(h.lines.some(v => v.includes("Ground truth: snapshot")));
        assert.doesNotMatch(h.lines.join("\n"), /SyntheticSecret/);
    });
}

test("timeout discards results and cleans up even when the child eventually exits successfully", async t => {
    const h = await harness(t); h.config.timeoutSeconds = 5;
    const original = global.setTimeout; let expire: (() => void) | undefined;
    t.mock.method(global, "setTimeout", ((fn: (...args: any[]) => void, ms?: number, ...args: any[]) => {
        if (ms === 5000) { expire = () => fn(...args); }
        return original(fn, ms, ...args);
    }) as typeof setTimeout);
    const run = await h.start(); assert.ok(expire); expire!(); await h.finish(run);
    assert.equal(h.values.size, 0); assert.match(h.status.text, /error/); assert.match(h.notices.at(-1)!, /timed out after 5 seconds/);
});

test("stream decoders flush partial UTF-8 and overlong progress lines stay bounded", async t => {
    const h = await harness(t); const run = await h.start();
    run.child.stderr.emit("data", "ide-safe: active");
    run.child.stdout.emit("data", "x".repeat(1024 * 1024 + 1));
    run.child.stdout.emit("data", "\n[INFO] ending "); run.child.stdout.emit("data", Buffer.from([0xe2]));
    await h.finish(run, null, false);
    assert.ok(h.lines.some(v => v.includes("progress buffer exceeded")));
    assert.ok(h.lines.some(v => v.includes("ending �"))); assert.ok(!h.notices.some(v => v.includes("did not confirm")));
});

for (const outcome of ["already exited", "kill rejected", "kill threw"] as const) {
    test(`LSP deadline preserves UI cleanup when the owned child ${outcome}`, async t => {
        const release = deferred<void>(); let kills = 0;
        const child = Object.assign(new Child(), { kill: (_signal?: string) => {
            kills++; if (outcome === "kill threw") { throw new Error("synthetic kill failure"); } return false;
        } });
        if (outcome === "already exited") { child.exitCode = 0; }
        const h = await harness(t, { serverProcess: child as unknown as ChildProcess, start: () => release.promise,
            dispose: async () => { throw new Error("synthetic dispose failure while starting"); } });
        t.mock.timers.enable({ apis: ["setTimeout"] });
        const shutdown = h.extension.deactivate(); t.mock.timers.tick(1000); await shutdown;
        release.resolve(); await new Promise(r => setImmediate(r));
        assert.equal(kills, outcome === "already exited" ? 0 : 1);
        assert.equal(h.clients[0].disposeCalls, 1); assert.ok(h.status.disposed && h.diagnostics.disposed);
    });
}
