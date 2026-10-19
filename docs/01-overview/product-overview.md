# StackAccord — Product overview

**Find code–database mismatches earlier.** StackAccord is the brand direction for the existing DataGuard tooling; package, CLI, extension and repository identities have not changed. The beta listing is unpublished. [Tiếng Việt](product-overview.vi.md).

## Current value

Source contains .NET/database contract checks for supported patterns: parameters, result shapes, nullability, length and dialect; snapshots/baselines, diagnostics, CLI and IDE integration. The rule matrix in the [README](../../README.md) is generated from source; a fixed rule/test count is not current acceptance evidence.

| Mode | Evidence | Limits |
|---|---|---|
| Full | Live metadata through the selected adapter | Visibility, types and supported syntax constrain scope; not every procedure/query is supported |
| Snapshot | Saved metadata | Depends on origin, age and completeness; does not establish current database state |
| Manual | Attribute-declared contracts | Not live metadata; only supplied information can be checked |

Descriptors extracted from source/assemblies are inputs; descriptor acquisition is separate from ground-truth mode. Unknown, acquisition errors and skipped work need explicit review, not a successful-validation interpretation.

## CLI, IDE and frontend

IDE analyzers/generators perform local checks; CLI `dataguard validate` runs the provider-, mode- and configuration-selected pipeline. CLI availability does not prove every action exists in each editor. Review quickfix diffs and revalidate; automatic safe repair is not guaranteed.

The CLI exports descriptors to TypeScript, but that export path returns before later validation. It is not verification of System.Text.Json payloads, runtime validation or React/API compatibility. See [capability details](feature-showcase.md).

## Who should evaluate it

.NET maintainers using stored procedures/raw SQL and platform engineers needing CI evidence can evaluate synthetic fixtures or disposable resources. Incremental value, noise and setup cost need pilot measurement; this documentation work establishes neither customer demand nor enterprise/banking certification.

## Status and next steps

Four adapters have different scope. Source presence does not establish a released artifact or accepted host. Architecture is not a performance measurement; budgets remain targets. Marketplace publication remains paused.

- [Source/verified-artifact installation](../05-operations/installation-guide.md)
- [Capability states](../product/capabilities.md), [source/tests](../product/source-map.md)
- [Mission and vision](../product/strategy.md), [roadmap](../product/roadmap.md)
- [Security/performance/enterprise evaluation](../product/evaluation.md), [research](../product/research-backlog.md)
- License terms and exceptions remain in [LICENSE](../../LICENSE) and [legal documentation](../legal/ADDITIONAL-PERMISSIONS.md); branding does not change them.
