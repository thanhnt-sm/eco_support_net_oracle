# Installation Guide

[Tiếng Việt](installation-guide.vi.md)

Use a source build or an inspected build artifact. This guide does not establish that a DataGuard package is available on NuGet, GHCR or either Marketplace. Technical IDs remain DataGuard while [StackAccord](../product/strategy.md) is the proposed brand.

## Prerequisites

- .NET 9 SDK selected by `global.json` for source builds; framework-dependent CLI/tool packages also need the .NET 9 runtime. Self-contained RID archives carry their runtime.
- Node 22 and npm for VS Code extension builds.
- Windows and Visual Studio MSBuild/SDK tooling for the Visual Studio VSIX.
- VS Code and its locked language client both declare `^1.91.0`. The source metadata mismatch is resolved; installed-editor acceptance remains pending.
- The VS manifest targets `[17.12,19.0)`; this is a declared range, not evidence that every host was tested. The readiness runbook calls for Visual Studio 18.10.2 acceptance.

Offline validation needs an appropriate snapshot or explicitly supplied contract source. Live metadata access needs a supported provider and authorized database access; adapter presence does not prove every database version/SQL feature works.

## Build the CLI from source

Run from the repository root after cloning; record the commit you build. This project-specific command avoids the Windows-only projects in the full solution.

```bash
git clone https://github.com/thanhnt-sm/eco_support_net_oracle.git
cd eco_support_net_oracle
dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj --locked-mode
dotnet build src/DataGuard.Cli/DataGuard.Cli.csproj -c Release --no-restore
dotnet run --project src/DataGuard.Cli -c Release --no-build -- version
```

For a locally installed command, package the same source:

```bash
dotnet pack src/DataGuard.Cli/DataGuard.Cli.csproj -c Release --no-restore --output ./artifacts/local-tools
# Isolate this local tool feed from the repository's nuget.org source mapping.
cat > ./artifacts/local-tools/NuGet.config <<'XML'
<configuration>
  <packageSources><clear /><add key="local" value="." /></packageSources>
</configuration>
XML
# Use the exact version from the generated .nupkg filename.
dotnet tool install --tool-path ./artifacts/tool --configfile ./artifacts/local-tools/NuGet.config DataGuard.Cli --version <package-version>
./artifacts/tool/dataguard version
```

Replace `<package-version>` before running. These commands create local artifacts; they do not publish a package. For a downloaded tool package use its verified directory and exact version as the source instead. A public registry install is appropriate only after independently verifying that release's identity and availability.

## Versioned artifacts from GitHub Actions

If an authorized **Build Release Artifacts** run completed successfully, download its `dataguard-installers-<version>` artifact. Verify the bundle:

```bash
cd downloaded-bundle
sha256sum -c SHA256SUMS.txt
```

Review `release-manifest.json` for version, source commit, file sizes and SHA-256. Extract the CLI archive for your RID and run its `dataguard --help`. The bundle's `nuget` directory can be used as a local tool source. Artifacts have 30-day retention and may expire; this workflow does not publish them to a registry or GitHub Release. A checksum binds bytes, not product acceptance.

## IDE extensions

Follow the [extension build guide](extension-build-guide.md) to produce a VSIX or select an inspected artifact from a successful run. Do not assume a VSIX is already present in a source checkout.

```bash
code --install-extension /path/to/accepted-vscode-package.vsix
```

Replace the path with the accepted VS Code package. Use VS Code's **Install from VSIX...** command, not the Visual Studio installer. For the Visual Studio package, close Visual Studio and launch the matching VSIX through its installer. Build it with Visual Studio MSBuild as documented, not a generic `dotnet build` instruction.

Test activation, diagnostics and navigation in the target editor. Local installation is distinct from public listing availability; see [browser publication gates](../marketplace-publishing.md) and the [separate manual workflow](../marketplace-automation.md).

## Local Docker build

The Dockerfile already supplies the CLI entrypoint. Build locally rather than assuming a published `latest` tag exists:

```bash
docker build -t dataguard:local .
docker run --rm dataguard:local version
```

For validation, mount the inspected project and set its working directory; pass `validate` directly, without an extra `dataguard` argument. Review filesystem permissions and connection configuration before live use.

## Verify and integrate

After placing the accepted CLI on PATH:

```bash
dataguard version
dataguard validate --help
dataguard init --provider sqlserver
```

`init` writes a starter configuration and refuses to overwrite an existing file. Follow the [quickstart](../01-overview/quickstart.md) for snapshot-backed validation and CI. CI should provision an exact reviewed artifact/version rather than depend on an unverified registry package or mutable image tag.
