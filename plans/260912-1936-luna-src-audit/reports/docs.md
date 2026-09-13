# DataGuard docs audit (read-only)

Baseline: `93bf7288324dd746669ad09c5e2a592adc772748` (2026-09-12). Phạm vi worker: chỉ `docs/`; không chạy test/web research, không đọc secrets/session state. Kết quả thực chạy do worker riêng ghi tại [verification](verification.md).

## Coverage và phân loại

Đã đọc 136 Markdown nongenerated theo batch/chunk; 7 raw `.txt` là generated command logs và được ghi nhận nhưng không dùng làm current test evidence. Không có file nongenerated chưa đọc.

- **Current/canonical:** `00-directory-tree`, `01-overview`, `02-architecture`, `03-components`, `04-diagrams`, `05-operations`, `08-developers`, `README*`, `assess.md`, `enterprise-banking-profile.md`, `marketplace-publishing.md`.
- **Current nhưng cần source revalidation:** root product/solution/usage/flow docs, `architecture.md`, `06-roadmap`, `07-testing`, `PERFORMANCE.md`, `operations/*`, `golden-standard/*`.
- **Historical evidence:** `product-discovery/baseline.md`, `release-evidence.md`, và `product-discovery/raw/*.txt`; các số liệu test ở đây không phải kết quả hiện tại.
- **Historical/archived, đã gắn nhãn trong file:** EcoSupport Rust/Python docs (`architecture/system_architecture*`, `tech_stack_evaluation*`, `cli.md`, `contributing.md`, `developers/contributor_deep_dive*`, `mcp.md`, `overview/vibe_coder_guide*`, `sitemap_and_component_registry*`, `testing/qa_test_strategy*`). Không flag các claim bên trong như defect current.

## Capability/requirement map

CLI có validate, baseline, snapshot refresh/show/diff, init, config show/validate, oracle-check, migrate, assess, version; Core có sources/rules/dependency graph, baseline v2, concurrent validation, reporting/SARIF, plugin MEF, auto-detection, telemetry, security/audit, public API và assessment; adapters gồm Oracle/SQL Server/MySQL/PostgreSQL; tooling gồm analyzer/generator, codefix, VS Code và Visual Studio. Capability matrix release ở `docs/product-discovery/capability-matrix.md:6-22`; shipped/known-unverified ở `docs/product-discovery/release-evidence.md:40-57`.

## Findings (evidence, priority, confidence)

| ID | Evidence | Finding | Priority | Confidence |
|---|---|---|---|---|
| DOC-01 | `docs/01-overview/feature-showcase.md:72-120` | Tiêu đề nói 27 validation rules nhưng bảng thực tế chỉ liệt kê 24 IDs (DG001-016, DG098-099, MY001-003, PG001-003). Số ID thực thi cần đối chiếu engine/analyzer riêng. | P2 | High |
| DOC-02 | `docs/01-overview/feature-showcase.md:161-251` và `docs/01-overview/feature-showcase.vi.md` | EN và VI khác mức chi tiết về Full/Snapshot/Manual, Output Formats và Code Fix/IDE; khác số dòng tự nó không phải bằng chứng sai nội dung. | P2 | Medium |
| DOC-03 | `docs/01-overview/feature-showcase.md:432` và `.vi.md:336`; `docs/product-discovery/release-evidence.md:51-54` | DependencyHealth được quảng bá như known-vulnerability/health-score; release evidence nói remote advisory chưa implement. Cần giới hạn claim ở local dependency checks. | P1 | High |
| DOC-04 | `docs/PRODUCT.md:132-145`; `docs/architecture.md:395-414`; `docs/STAGE_FLOW.md:335-367` | Claim `/health/live`, `/health/ready`, `/health/startup` và “12 CodeFixProviders” cần source audit xác nhận; không coi là shipped chỉ từ docs. | P2 | Medium |
| DOC-05 | `docs/01-overview/quickstart.md:55`; `docs/USAGE.md:222,624-645,669`; `docs/06-roadmap/future-directions.md:3-16` | Version/test/coverage khác nhau giữa tài liệu. Số test cũ cần gắn commit/ngày; không lấy snapshot release evidence khác làm số liệu mới. Version ví dụ không tự chứng minh version phát hành sai. | P2 | High |
| DOC-06 | `docs/01-overview/quickstart.md:84`; `docs/enterprise-banking-profile.md:12-18,75-81` | Quickstart bật plaintext fallback trong ví dụ; banking profile tắt. Đây có thể là khác biệt profile/dev; là câu hỏi đối chiếu, chưa phải lỗi. | P3 | Medium |
| DOC-07 | `docs/05-operations/configuration-guide.md:97-119`; `docs/03-components/core/security.md:84-120` | Credential resolution order và provider behavior cần đối chiếu source; không suy ra thứ tự canonical chỉ từ docs. | P2 | Medium |
| DOC-08 | `docs/03-components/core/validation.md:145-188`; `docs/02-architecture/design-philosophy.md:196-230` | Claims “2-4x faster”, “zero-allocation”, queue drop semantics là performance/behavior claims; cần benchmark/source verification, không dùng như measured evidence. | P2 | Medium |

Không flag `sitemap_and_component_registry*` nhắc LICENSE.md vì file đã gắn ARCHIVED và là historical artifact; canonical license reconciliation chỉ là unresolved context, không phải current defect.

## Per-file read/classification ledger (143 baseline paths)

Tất cả Markdown dưới đây: **read / current hoặc historical theo nhóm**. Raw logs: **read metadata/content / excluded-generated-historical**.

```
docs/00-directory-tree/directory-tree.md
docs/00-directory-tree/directory-tree.vi.md
docs/01-overview/feature-showcase.md
docs/01-overview/feature-showcase.vi.md
docs/01-overview/pain-points-solved.md
docs/01-overview/pain-points-solved.vi.md
docs/01-overview/product-overview.md
docs/01-overview/product-overview.vi.md
docs/01-overview/quickstart.md
docs/02-architecture/component-model.md
docs/02-architecture/component-model.vi.md
docs/02-architecture/design-philosophy.md
docs/02-architecture/design-philosophy.vi.md
docs/02-architecture/system-architecture.md
docs/02-architecture/system-architecture.vi.md
docs/02-architecture/tech-stack.md
docs/03-components/adapters/mysql-adapter.md
docs/03-components/adapters/mysql-adapter.vi.md
docs/03-components/adapters/oracle-adapter.md
docs/03-components/adapters/oracle-adapter.vi.md
docs/03-components/adapters/postgresql-adapter.md
docs/03-components/adapters/postgresql-adapter.vi.md
docs/03-components/adapters/sqlserver-adapter.md
docs/03-components/adapters/sqlserver-adapter.vi.md
docs/03-components/contracts/contract-attributes.md
docs/03-components/contracts/contract-attributes.vi.md
docs/03-components/core/abstractions.md
docs/03-components/core/abstractions.vi.md
docs/03-components/core/assessment.md
docs/03-components/core/assessment.vi.md
docs/03-components/core/auto-detection.md
docs/03-components/core/auto-detection.vi.md
docs/03-components/core/baseline.md
docs/03-components/core/baseline.vi.md
docs/03-components/core/plugins.md
docs/03-components/core/plugins.vi.md
docs/03-components/core/public-api.md
docs/03-components/core/public-api.vi.md
docs/03-components/core/reporting.md
docs/03-components/core/reporting.vi.md
docs/03-components/core/rules-engine.md
docs/03-components/core/rules-engine.vi.md
docs/03-components/core/security.md
docs/03-components/core/security.vi.md
docs/03-components/core/sources.md
docs/03-components/core/sources.vi.md
docs/03-components/core/telemetry.md
docs/03-components/core/telemetry.vi.md
docs/03-components/core/validation.md
docs/03-components/core/validation.vi.md
docs/03-components/tooling/analyzers.md
docs/03-components/tooling/analyzers.vi.md
docs/03-components/tooling/cli.md
docs/03-components/tooling/cli.vi.md
docs/03-components/tooling/code-fixes.md
docs/03-components/tooling/code-fixes.vi.md
docs/03-components/tooling/visual-studio-extension.md
docs/03-components/tooling/visual-studio-extension.vi.md
docs/03-components/tooling/vscode-extension.md
docs/03-components/tooling/vscode-extension.vi.md
docs/04-diagrams/activity-diagrams.md
docs/04-diagrams/activity-diagrams.vi.md
docs/04-diagrams/component-lifecycle.md
docs/04-diagrams/data-flow.md
docs/04-diagrams/data-flow.vi.md
docs/04-diagrams/sequence-diagrams.md
docs/04-diagrams/sequence-diagrams.vi.md
docs/04-diagrams/state-machine.md
docs/04-diagrams/state-machine.vi.md
docs/05-operations/best-practices.md
docs/05-operations/best-practices.vi.md
docs/05-operations/configuration-guide.md
docs/05-operations/configuration-guide.vi.md
docs/05-operations/installation-guide.md
docs/05-operations/installation-guide.vi.md
docs/05-operations/log-guide.md
docs/05-operations/log-guide.vi.md
docs/05-operations/playbook.md
docs/05-operations/playbook.vi.md
docs/05-operations/runbook.md
docs/05-operations/runbook.vi.md
docs/06-roadmap/future-directions.md
docs/06-roadmap/future-directions.vi.md
docs/06-roadmap/upgrade-path.md
docs/06-roadmap/upgrade-path.vi.md
docs/07-testing/test-strategy.md
docs/07-testing/test-strategy.vi.md
docs/08-developers/contributor-guide.md
docs/08-developers/contributor-guide.vi.md
docs/COMPONENT_INTERACTION.md
docs/DATA_FLOW.md
docs/FIX_PLAN.md
docs/PERFORMANCE.md
docs/PRODUCT.md
docs/README.md
docs/README.vi.md
docs/RISKS_GAPS.md
docs/SOLUTION.md
docs/STAGE_FLOW.md
docs/USAGE.md
docs/architecture.md
docs/architecture/agent-config.md
docs/architecture/agent-config.vi.md
docs/architecture/system_architecture.md
docs/architecture/system_architecture.vi.md
docs/architecture/tech_stack_evaluation.md
docs/architecture/tech_stack_evaluation.vi.md
docs/assess.md
docs/cli.md
docs/contributing.md
docs/developers/contributor_deep_dive.md
docs/developers/contributor_deep_dive.vi.md
docs/enterprise-banking-profile.md
docs/golden-standard/PATTERNS.md
docs/golden-standard/TEMPLATE_CHECKLIST.md
docs/marketplace-publishing.md
docs/mcp.md
docs/operations/playbook_and_runbook.md
docs/operations/playbook_and_runbook.vi.md
docs/operations/release_guide.md
docs/operations/release_guide.vi.md
docs/overview/vibe_coder_guide.md
docs/overview/vibe_coder_guide.vi.md
docs/product-discovery/baseline.md
docs/product-discovery/capability-matrix.md
docs/product-discovery/code-capabilities.md
docs/product-discovery/dependency-map.md
docs/product-discovery/dotnet-legacy-evidence.md
docs/product-discovery/opportunity-backlog.md
docs/product-discovery/problem-market-evidence.md
docs/product-discovery/raw/01-git-baseline.txt
docs/product-discovery/raw/02-restore.txt
docs/product-discovery/raw/03-build.txt
docs/product-discovery/raw/04-build-analyzers.txt
docs/product-discovery/raw/05-format.txt
docs/product-discovery/raw/06-test.txt
docs/product-discovery/raw/07-coverage-gate.txt
docs/product-discovery/release-evidence.md
docs/product-discovery/source-inventory.md
docs/sitemap_and_component_registry.md
docs/sitemap_and_component_registry.vi.md
docs/testing/qa_test_strategy.md
docs/testing/qa_test_strategy.vi.md
```

## Compact totals

- Baseline paths: **143** (136 Markdown + 7 raw logs).
- Nongenerated unread: **0**.
- Historical/archived paths: **19** (7 product-discovery evidence/raw + 12 explicitly archived EcoSupport docs; counts overlap by policy grouping only).
- High-confidence findings: **5**; medium-confidence/provisional: **3**.
- Tests run by this audit: **none**.
