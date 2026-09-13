const { existsSync, promises: fs } = require("node:fs");
const path = require("node:path");
const { runTests } = require("@vscode/test-electron");

async function main() {
    const extensionDevelopmentPath = path.resolve(__dirname, "..");
    const extensionTestsPath = path.join(extensionDevelopmentPath, "test", "extension-host.cjs");
    const profileRoot = path.join("/tmp", `dg-vscode-host-${process.pid}`);
    const vscodeExecutablePath = process.env.VSCODE_EXECUTABLE_PATH
        || "/Applications/Visual Studio Code.app/Contents/MacOS/Code";

    if (!existsSync(vscodeExecutablePath)) {
        throw new Error("Set VSCODE_EXECUTABLE_PATH to a local VS Code executable for extension-host tests.");
    }

    let exitCode;
    try {
        exitCode = await runTests({
            vscodeExecutablePath,
            extensionDevelopmentPath,
            extensionTestsPath,
            launchArgs: [
                `--user-data-dir=${path.join(profileRoot, "user-data")}`,
                `--extensions-dir=${path.join(profileRoot, "extensions")}`,
                "--disable-gpu",
                "--skip-welcome",
            ],
        });
    } finally {
        await fs.rm(profileRoot, { recursive: true, force: true });
    }

    if (exitCode !== 0) {
        throw new Error(`VS Code extension-host test failed with exit code ${exitCode}.`);
    }
}

main().catch((error) => {
    console.error(error);
    process.exitCode = 1;
});
