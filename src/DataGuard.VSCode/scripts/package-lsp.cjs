const { createHash } = require("crypto");
const { spawnSync } = require("child_process");
const { mkdirSync, readFileSync, rmSync, writeFileSync } = require("fs");
const { join, resolve } = require("path");

const extensionRoot = resolve(__dirname, "..");
const output = join(extensionRoot, "server");
const project = resolve(extensionRoot, "..", "DataGuard.LanguageServer", "DataGuard.LanguageServer.csproj");
rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });
const publish = spawnSync("dotnet", ["publish", project, "--configuration", "Release", "--output", output, "-p:RollForward=Major"], { stdio: "inherit" });
if (publish.error) {
  console.error(`[package-lsp] Failed to execute dotnet publish: ${publish.error.message || publish.error}`);
  process.exit(1);
}
if (publish.status !== 0) process.exit(publish.status ?? 1);
const artifact = join(output, "DataGuard.LanguageServer.dll");
const sha256 = createHash("sha256").update(readFileSync(artifact)).digest("hex");
writeFileSync(join(output, "manifest.json"), JSON.stringify({ schemaVersion: 1, artifact: "DataGuard.LanguageServer.dll", sha256 }, null, 2) + "\n");
