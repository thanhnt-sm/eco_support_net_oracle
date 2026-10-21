import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import * as path from "node:path";
import test from "node:test";
import { renderDashboardHtml } from "./ui/dashboard-view";

const root = path.resolve(__dirname, "..");
const manifest = JSON.parse(readFileSync(path.join(root, "package.json"), "utf8"));

test("dashboard renders the StackAccord brand", () => {
    const html = renderDashboardHtml([], "branding-test");
    assert.match(html, /<title>StackAccord Contract Drift Dashboard<\/title>/);
    assert.match(html, /<span>StackAccord Contract Drift<\/span>/);
    assert.doesNotMatch(html, /DataGuard/);
});

test("extension presents StackAccord while existing installations, commands and settings keep their identities", () => {
    assert.equal(manifest.name, "dataguard-vscode");
    assert.equal(manifest.publisher, "thanhnt-sm");
    assert.equal(manifest.displayName, "StackAccord");
    assert.deepEqual(manifest.contributes.commands.map((c: any) => c.command), [
        "dataguard.runValidation", "dataguard.cancelValidation", "dataguard.assess", "dataguard.refreshSnapshot",
        "dataguard.createBaseline", "dataguard.configureConnection", "dataguard.openDashboard", "dataguard.refreshFindings",
        "dataguard.clearFindings", "dataguard.groupBySeverity", "dataguard.groupByRule", "dataguard.groupByFile",
        "dataguard.applyQuickFix", "dataguard.scanProject", "dataguard.refreshQueries", "dataguard.verifyShape",
    ]);
    for (const command of manifest.contributes.commands) { assert.match(command.title, /^StackAccord: /); }
    const container = manifest.contributes.viewsContainers.activitybar[0];
    assert.equal(container.id, "dataguard-explorer");
    assert.equal(container.title, "StackAccord");
    assert.deepEqual(manifest.contributes.views[container.id].map((v: any) => v.id), ["dataguard.findingsView", "dataguard.sqlQueriesView"]);
    const settings = manifest.contributes.configuration;
    assert.equal(settings.title, "StackAccord");
    assert.deepEqual(Object.keys(settings.properties), ["dataguard.enabled", "dataguard.configPath", "dataguard.provider", "dataguard.cliPath", "dataguard.timeoutSeconds", "dataguard.assessmentTimeoutSeconds"]);
    assert.equal(settings.properties["dataguard.configPath"].default, ".dataguard.yml");
    assert.equal(settings.properties["dataguard.cliPath"].default, "dataguard");
    for (const value of Object.values(settings.properties) as any[]) { assert.doesNotMatch(value.description, /DataGuard/); }
    assert.doesNotMatch(JSON.stringify(manifest.capabilities), /DataGuard/);
    assert.match(readFileSync(path.join(root, "README.md"), "utf8"), /^# StackAccord for VS Code/);
});

test("runtime text has no legacy brand outside explicitly retained technical compatibility literals", () => {
    const allowed = new Set(['} else if (entry.name === "DataGuard") {', 'return code.startsWith("DG") || d.source === "StackAccord" || d.source === "DataGuard" || d.message.includes("DataGuard");']);
    const failures: string[] = [];
    function walk(dir: string) {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            const file = path.join(dir, entry.name);
            if (entry.isDirectory()) { walk(file); continue; }
            if (!file.endsWith(".ts") || file.endsWith(".test.ts")) { continue; }
            const lines = readFileSync(file, "utf8").replace(/\/\*[\s\S]*?\*\//g, "").split("\n");
            for (const [index, line] of lines.entries()) {
                if (line.trimStart().startsWith("//") || allowed.has(line.trim())) { continue; }
                const text = line.replace(/\bDataGuard[A-Z]\w*\b/g, "").replaceAll("DataGuard.LanguageServer.dll", "");
                if (text.includes("DataGuard")) { failures.push(`${path.relative(root, file)}:${index + 1}: ${line.trim()}`); }
            }
        }
    }
    walk(path.join(root, "src"));
    assert.deepEqual(failures, []);
});
