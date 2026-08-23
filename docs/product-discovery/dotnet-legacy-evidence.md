# .NET Legacy Evidence

**Scope.** Primary Microsoft Learn and NuGet documentation only. Retrieved **2026-08-23**. This is evidence for assessing existing legacy .NET projects; it does not prescribe product features or a migration outcome.

## Evidence records

### 1. Microsoft lifecycle and support

- **Source:** [Microsoft .NET Framework lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework) (Microsoft Learn; retrieved 2026-08-23).
- **Version applicability:** .NET Framework 2.0–4.8.1; the retirement statement specifically applies to 4.5.2, 4.6, and 4.6.1.
- **Quote / fact evidence:** “Microsoft .NET Framework follows the Component Lifecycle Policy.” The release table lists .NET Framework 4.6.2 with end date **2027-01-13**, and lists 4.8 and 4.8.1 without an end date. It also states: “.NET Framework 4.5.2, 4.6, and 4.6.1 retired on April 26, 2022.”
- **Applicability:** A legacy-project assessment must identify its exact target framework rather than infer support status from the family name; the published dates distinguish 4.6.2 from the retired 4.6.1 and earlier releases.

### 2. Framework / SDK compatibility boundaries

- **Source:** [.NET Standard overview](https://learn.microsoft.com/en-us/dotnet/standard/net-standard) (Microsoft Learn; retrieved 2026-08-23).
- **Version applicability:** .NET Framework 4.6.1–4.8.1; .NET Core 2.0 SDK and later tooling for the documented .NET Standard 1.5+ mappings.
- **Quote / fact evidence:** “If you want to share code between .NET Framework and any other .NET implementation, such as .NET Core, your library should target .NET Standard 2.0.” The compatibility table lists .NET Framework 4.6.1–4.8.1 for netstandard2.0; its note says those versions “apply to .NET Core 2.0 SDK and later versions of the tooling.” The same source says “.NET Framework doesn't support .NET Standard 2.1.”
- **Applicability:** netstandard2.0 is the documented common-library boundary for these Framework targets; a package or project that requires netstandard2.1 is not compatible with .NET Framework.

### 3. NuGet package compatibility and packages.config migration

- **Source:** [Migrating from packages.config to PackageReference](https://learn.microsoft.com/en-us/nuget/consume-packages/migrate-packages-config-to-package-reference) (NuGet / Microsoft Learn; retrieved 2026-08-23).
- **Version applicability:** NuGet PackageReference; Visual Studio 2017 version 15.7+ for the migrator; legacy packages.config projects. PackageReference is unavailable in Visual Studio 2015 and earlier.
- **Quote / fact evidence:** “Visual Studio 2017 Version 15.7 and later supports migrating a project from the packages.config management format to the PackageReference format.” The documented limitations say: “Some packages may not be fully compatible with PackageReference.” For package asset compatibility: “With PackageReference, install.ps1 and uninstall.ps1 PowerShell scripts are not executed”; content assets are “not supported with PackageReference and are ignored”; and root lib assemblies without a TFM subfolder are ignored because NuGet seeks a folder matching the project TFM.
- **Applicability:** Migration requires package-by-package asset review, especially packages relying on install scripts, content, XDT transforms, or non-TFM lib layout; a successful reference conversion alone is not evidence of equivalent behavior.

### 4. MSBuild project-format migration options

- **Source:** [Organize projects for .NET Framework and .NET](https://learn.microsoft.com/en-us/dotnet/core/porting/project-structure) (Microsoft Learn; retrieved 2026-08-23).
- **Version applicability:** Existing .NET Framework projects plus .NET projects; combined multi-target project option requires Visual Studio 2019 or later to open existing projects.
- **Quote / fact evidence:** Microsoft documents two layouts: a single multi-targeted project and retaining separate projects. For the former: “a single project can compile for different frameworks” and can handle “different compilation options and dependencies per targeted framework.” It notes replacement of packages.config and the old project file with a .NET csproj, where packages use PackageReference. For the latter, keeping projects separate “lowers the possibility of creating new bugs in existing projects because no code churn is required.”
- **Applicability:** Project-file cutover and framework retargeting are separable choices: side-by-side projects are a documented option when preserving legacy project behavior or older tooling access matters.

### 5. Roslyn / analyzer diagnostic configuration

- **Source:** [Configure code analysis rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options) (Microsoft Learn; retrieved 2026-08-23).
- **Version applicability:** .NET SDK projects; code analysis is enabled by default for targets .NET 5+; a project targeting another .NET implementation can enable it with EnableNETAnalyzers=true when using the .NET 5+ SDK.
- **Quote / fact evidence:** “You can configure the severity level for any rule” with dotnet_diagnostic.<rule ID>.severity = warning. Microsoft states that for severity error, “Violations appear as build errors and cause builds to fail”; warning does not fail a build unless warnings are treated as errors. It further states: “If you have the .NET 5+ SDK but your project targets a different .NET implementation, you can manually enable code analysis” by setting EnableNETAnalyzers to true.
- **Applicability:** Diagnostic adoption on a legacy target is an explicit SDK/MSBuild configuration decision, and each rule’s severity can change build outcome; therefore existing diagnostic IDs and severity configuration are compatibility-relevant inputs, not cosmetic editor settings.

## Evidence limits

These sources establish vendor-documented support, targeting, NuGet asset, project-format, and analyzer behavior. They do not establish that any particular repository, package version, Visual Studio installation, or custom MSBuild target satisfies those conditions; that requires repository-specific inspection.
