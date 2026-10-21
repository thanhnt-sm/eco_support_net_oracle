# StackAccord branding and compatibility

[Tiếng Việt](branding.vi.md) · [Product strategy](strategy.md) · [Marketplace copy](marketplace-copy.md)

StackAccord is the chosen product name. Use it in current product headings, explanations, issue forms, release descriptions and SBOM product labels. The Visual Studio display name is **StackAccord – .NET Database Contracts (Beta)**; its Tools menu and Output pane use **StackAccord (Beta)**. VS Code display labels use **StackAccord**. These names describe the intended source presentation; installed-editor and upgrade acceptance require the exact packaged artifact.

## Names that stay stable

| Surface | Retained identifier | Reason |
|---|---|---|
| CLI and packages | `dataguard`, `DataGuard.Cli`, `DataGuard.*`, `DataGuard.sln` | Existing commands, package consumers, assemblies and build entrypoints |
| Configuration and environment | `.dataguard.yml`, `.dataguard-snapshot.json`, `.dataguard-baseline.json`, `DATAGUARD_*` | Existing projects, snapshots, baselines and automation must continue to work |
| IDE identity | `DataGuard.VisualStudio`, `thanhnt-sm.dataguard-vscode`, publisher `thanhnt-sm` | Same-extension updates rather than a second installation |
| IDE preferences and storage | `dataguard.*`, **Tools > Options > DataGuard**, `%APPDATA%\DataGuard\logs` | Existing settings, consent and storage paths remain discoverable |
| Protocol and diagnostics | `DG*`, provider rule IDs, `dataguard/v2`, `// DataGuard:` | Diagnostic consumers, baseline fingerprints and generated code-fix comment markers |
| Distribution and repository | `dataguard-*.zip`, `dataguard-vscode-*.vsix`, `DataGuard.VisualStudio-*.vsix`, `thanhnt-sm/eco_support_net_oracle` | Download selectors, checksums, links and release scripts |

These are deliberate compatibility identifiers. Do not substitute StackAccord into them as a cosmetic rename. A future identifier change needs its own migration design and upgrade evidence. The proposed repository slug in the strategy is a separate proposal.

## Historical and legal records

Keep the original names in `CHANGELOG.md`, dated journals, discovery/run evidence, ADRs, dated decisions and legal notices. Existing documents explicitly marked historical or archived also retain their bodies. Their names describe what was proposed, shipped or observed at the recorded time. They do not establish current availability or acceptance. The numbered current documentation and root product prose use StackAccord. Legal terms and license history remain unchanged.

## Regression audit

Run from the repository root:

```bash
python3 scripts/check_product_branding.py
python3 -m unittest discover -s scripts/tests -p 'test_product_branding.py'
./scripts/verify_docs_sync.sh
```

The audit covers root product prose, Markdown under `docs/`, Markdown/YAML under `.github/` and `packaging/` when present, including visible Mermaid labels and inline UI labels. Technical code examples, URLs and link destinations are retained. Narrow exceptions for the persisted Options category, explicit technical-identity explanations, generated comment markers and dated DLL evidence have reasons in the script; an exception never suppresses unrelated prose on the same line. Tests exercise both rejected display regressions and accepted compatibility examples, and check representative real commands, settings, fingerprints, artifact names and extension identities. Existing CI unittest discovery runs these tests; no workflow gate is changed.

This lane began at `872974c5f333ef97f142b3bf55c2fe3202816b09`. It owns root product prose, documentation and release labels. Production source and packaged extension READMEs under `src/` or `extensions/` belong to the IDE/runtime lanes and are outside this audit. Passing this audit does not establish complete installed UI branding, same-ID upgrade/settings retention, Windows/macOS host acceptance, external Marketplace listing updates, name clearance or publication approval. Marketplace publication remains paused.
