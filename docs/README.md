# DataGuard Documentation Hub

> **DataGuard** — Contract validation for .NET Entity ↔ Stored Procedure / Raw SQL.

## Documentation Map

| Section | Description | Link |
|---------|-------------|------|
| **Directory Tree** | Full workspace inventory with descriptions | [EN](00-directory-tree/directory-tree.md) · [VI](00-directory-tree/directory-tree.vi.md) |
| **Product Overview** | What DataGuard is, why it exists | [EN](01-overview/product-overview.md) · [VI](01-overview/product-overview.vi.md) |
| **Feature Showcase** | All features and capabilities | [EN](01-overview/feature-showcase.md) · [VI](01-overview/feature-showcase.vi.md) |
| **Pain Points Solved** | .NET/C# developer problems solved | [EN](01-overview/pain-points-solved.md) · [VI](01-overview/pain-points-solved.vi.md) |
| **Quickstart** | 5-minute getting started | [EN](01-overview/quickstart.md) |
| **System Architecture** | Full architecture with diagrams | [EN](02-architecture/system-architecture.md) · [VI](02-architecture/system-architecture.vi.md) |
| **Design Philosophy** | Design principles and philosophy | [EN](02-architecture/design-philosophy.md) · [VI](02-architecture/design-philosophy.vi.md) |
| **Component Model** | Component dependency graph | [EN](02-architecture/component-model.md) · [VI](02-architecture/component-model.vi.md) |
| **Tech Stack** | Technology stack evaluation | [EN](02-architecture/tech-stack.md) |
| **Core Components** | Abstractions, Rules, Sources, Security, Baseline, Reporting, Validation, Plugins, Telemetry, AutoDetection, Assessment, Public API | [EN](03-components/core/) · [VI](03-components/core/) |
| **Database Adapters** | Oracle, SQL Server, MySQL, PostgreSQL | [EN](03-components/adapters/) · [VI](03-components/adapters/) |
| **Tooling** | CLI, Analyzers, CodeFixes, VS Code, Visual Studio | [EN](03-components/tooling/) · [VI](03-components/tooling/) |
| **Contracts** | Attributes and naming conventions | [EN](03-components/contracts/) · [VI](03-components/contracts/) |
| **Diagrams** | Data flow, activity, sequence, state machine, lifecycle | [EN](04-diagrams/) · [VI](04-diagrams/) |
| **Operations** | Installation, configuration, playbook, runbook, logs, best practices | [EN](05-operations/) · [VI](05-operations/) |
| **Roadmap** | Future directions and upgrade path | [EN](06-roadmap/) · [VI](06-roadmap/) |
| **Testing** | QA and test strategy | [EN](07-testing/test-strategy.md) · [VI](07-testing/test-strategy.vi.md) |
| **Developers** | Contributor guide | [EN](08-developers/contributor-guide.md) · [VI](08-developers/contributor-guide.vi.md) |

## Architecture Decision Records

Component decisions measured against the original goals ([index](adr/README.md)):

- [ADR-0000](adr/ADR-0000-adr-process.md): ADR process and template
- [ADR-0001](adr/ADR-0001-visual-studio-extension.md): Visual Studio extension (`freeze`)
- [ADR-0002](adr/ADR-0002-vscode-extension-and-language-server.md): VS Code extension and Language Server (`freeze`)
- [ADR-0003](adr/ADR-0003-observability-packages.md): Observability packages (`extract`)
- [ADR-0004](adr/ADR-0004-host-and-health.md): Host and Core/Health (`extract`)
- [ADR-0005](adr/ADR-0005-assessment-and-osv-client.md): Assessment and OSV client (`freeze`)
- [ADR-0006](adr/ADR-0006-telemetry-http-export.md): Telemetry HTTP export (`extract`)
- [ADR-0007](adr/ADR-0007-rule-plugins.md): Rule plugins (`freeze`, revisit after Phase 4.2)
- [ADR-0008](adr/ADR-0008-auto-detection.md): AutoDetection and `init --wizard` (`freeze`)
- [ADR-0009](adr/ADR-0009-mysql-and-postgresql-adapters.md): MySQL and PostgreSQL adapters (`keep`, preview)
- [ADR-0010](adr/ADR-0010-telemetry-vs-observability.md): Core/Telemetry vs Observability (one stack: Core/Telemetry)

## Quick Links

- [README](../README.md) · [README.vi](../README.vi.md)
- [CHANGELOG](../CHANGELOG.md)
- [CONTRIBUTING](../CONTRIBUTING.md) · [CONTRIBUTING.vi](../CONTRIBUTING.vi.md)
- [SECURITY](../SECURITY.md) · [SECURITY.vi](../SECURITY.vi.md)
- [CLI Reference](03-components/tooling/cli.md)
- [Configuration Guide](05-operations/configuration-guide.md)
- [OpenSSF Best Practices Cheat Sheet](guides/openssf-best-practices-answers.md)
