const assert = require("node:assert/strict");
const { promises: fs } = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vscode = require("vscode");

async function run() {
    const extension = vscode.extensions.getExtension("thanhnt-sm.dataguard-vscode");
    assert.ok(extension, "DataGuard extension must be discoverable in the extension host");

    await extension.activate();
    assert.equal(extension.isActive, true, "DataGuard extension must activate");

    const commands = await vscode.commands.getCommands(true);
    for (const command of ["dataguard.runValidation", "dataguard.cancelValidation", "dataguard.assess", "dataguard.refreshSnapshot", "dataguard.createBaseline"]) {
        assert.ok(commands.includes(command), `DataGuard command '${command}' must be registered`);
    }

    const directory = await fs.mkdtemp(path.join(os.tmpdir(), "dataguard-lsp-"));
    const filePath = path.join(directory, "Probe.cs");
    const uri = vscode.Uri.file(filePath);
    try {
        await fs.writeFile(filePath, "var query = \"SELECT CustomerId FROM Customers\";\n", "utf8");
        const document = await vscode.workspace.openTextDocument(uri);
        await vscode.window.showTextDocument(document, { preview: false });
        await waitFor(() => vscode.languages.getDiagnostics(uri).some((diagnostic) => diagnostic.code === "DGSQL001"));
    } finally {
        await fs.rm(directory, { recursive: true, force: true });
    }
}

async function waitFor(predicate) {
    const deadline = Date.now() + 10_000;
    while (Date.now() < deadline) {
        if (predicate()) {
            return;
        }
        await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error("Timed out waiting for DataGuard language-server diagnostic DGSQL001.");
}

module.exports = { run };
