# StackAccord product strategy

Draft decision baseline, 2026-10-18. [Tiếng Việt](strategy.vi.md) · [Evidence](source-map.md) · [Roadmap](roadmap.md)

**Find code–database mismatches earlier.** StackAccord helps .NET developers inspect supported contracts between code and relational databases, explain discrepancies, and bring evidence into editor and CI workflows. “Keep your stack in agreement” is the supporting brand line, not a guarantee of compatibility.

## Mission and vision

Our mission is to help developers maintain and change applications through explicit code/data contracts, useful diagnostics and structured evidence. Our long-term vision is shared, reviewable checks across .NET, React, APIs and databases for humans and AI agents. The cross-stack vision is planned work, not a description of the current release.

## Audience and problem

Primary users are maintainers of database-heavy .NET applications, including teams using stored procedures and teams with React frontends. Champions are technical leads and platform engineers; buyers may be engineering managers; security, architecture and procurement teams may approve enterprise adoption. These roles and their willingness to adopt remain discovery hypotheses.

The proposed differentiator is a source-linked evidence chain from database column/procedure through .NET mapping and wire field to consumer. Its value must be demonstrated against existing tools on real, sanitized incidents. Do not rebuild general migration linting, client generation or interaction testing when integration provides the same value. See [research and experiments](research-backlog.md).

## Product boundaries

Current source contains .NET/database analysis, snapshots, reporting, CLI/IDE integration and descriptor-to-TypeScript export. Providers have different capabilities and evidence; four adapters do not mean equal support. Export is not proof that validation ran. React-aware compatibility, an agent protocol and enterprise assurance remain planned. The [capability ledger](capabilities.md) separates implementation, verification and release status.

Architectural direction: a deterministic parser/IR/rule core with thin CLI, editor, CI and future agent adapters. Snapshot-first analysis and explicit unknown states precede richer integration. Build-aware execution requires a separately approved trust boundary; opening a repository must not imply permission to execute its build. See [evaluation](evaluation.md).

Non-goals are a general React linter, migration executor, autonomous database repair, blanket security scanner, compliance certification or a new hosted control plane without demonstrated demand. Proposed freeze/extract recommendations in existing ADRs need explicit owner disposition before expansion; this strategy does not mark them accepted.

## Brand and governance

The product name is **StackAccord**; the Visual Studio display name in source is **StackAccord – .NET Database Contracts (Beta)**. Do not shorten the brand to Accord. Indexed name research is not trademark or availability clearance. Navy/teal is an optional visual direction; no logo has been delivered.

The proposed repository slug is `stackaccord`, but the current repository remains `thanhnt-sm/eco_support_net_oracle`. Preserve `DataGuard.*`, `dataguard`, `.dataguard.yml`, `DATAGUARD_*`, VSIX identity `DataGuard.VisualStudio` and VS Code identity `thanhnt-sm.dataguard-vscode`. These are compatibility identifiers, not pending product-name substitutions. Same-ID installed-user upgrade checks remain separate acceptance work. See the [branding compatibility policy](branding.md).

Product owner owns claims and scope; engineering leads own implementation evidence; QA owns independent corpora; security owns threat-model review; release engineering owns artifact/host acceptance; legal owns name and license review. These are roles, not assigned people. Publication remains paused. No legal text, technical metadata or production source is changed by this strategy.
