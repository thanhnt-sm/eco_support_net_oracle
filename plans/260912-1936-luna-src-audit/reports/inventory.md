---
type: scout
date: 2026-09-12
baseline: 93bf7288324dd746669ad09c5e2a592adc772748
---
# Inventory và coverage audit GPT Luna

Baseline lấy bằng `git ls-files src docs plans research`: 307 file tracked; `git status --short` sạch, không có untracked trong bốn cây tại thời điểm bắt đầu. Thư mục audit được tạo sau baseline nên không tự tính vào coverage.

## Tổng hợp

| Cây | Tracked | Đọc text/source | Generated/binary được phân loại |
|---|---:|---:|---:|
| src | 89 | 89 | 0 |
| docs | 143 | 136 | 7 raw logs lịch sử |
| plans | 25 | 25 | 0 |
| research | 50 | 30 | 20 bytecode .pyc |
| **Tổng** | **307** | **280** | **27** |

Coverage ở đây là coverage **file của đợt scout**, không phải code/test coverage. Lockfiles được kiểm tra nội dung/graph; không coi từng dòng checksum là yêu cầu tính năng. Không dịch ngược bytecode. Raw logs cũ không dùng làm kết quả test hiện tại.

## Ownership và phương pháp

| Worker GPT Luna medium | File sở hữu | Báo cáo |
|---|---:|---|
| Core engine | 13 | [Core engine](core-engine.md) |
| Core services | 23 | [Core services](core-services.md) |
| Adapters | 17 | [Adapters](adapters.md) |
| Tooling | 14 | [Tooling](tooling.md) |
| CLI/IDE | 22 | [CLI/IDE](cli-ide.md) |
| Docs | 143 | [Docs](docs.md) |
| Plans/research | 75 | [Plans/research](plans-research.md) |
| Verification | Build/test, không sở hữu source | [Verification](verification.md) |

Agent chính đối chiếu ledger với danh sách baseline, đọc lại các finding quan trọng, loại trùng và kiểm tra kết luận sai. Đọc chéo caller/test không làm tăng số file sở hữu. Mỗi source file có đúng một owner.

Generated local paths `**/bin/**`, `**/obj/**`, `**/node_modules/**`, `**/out/**`, `TestResults/` và cache/logs gitignored nằm ngoài baseline source. `src/DataGuard.VSCode/.vscodeignore` được tính đầy đủ dù là dotfile. Tracked .pyc không bị bỏ khỏi inventory và không bị xóa.

## Ledger baseline

“Đã đọc” là kết quả worker sau đọc theo lô/chunk; “phân loại” là loại trừ diễn giải nội dung có chủ đích, không phải file bỏ sót. Phân loại tài liệu hiện hành/lịch sử chi tiết trong báo cáo docs và plans/research.

| File | Owner | Trạng thái |
|---|---|---|
| [docs/00-directory-tree/directory-tree.md](../../../docs/00-directory-tree/directory-tree.md) | docs | Đã đọc |
| [docs/00-directory-tree/directory-tree.vi.md](../../../docs/00-directory-tree/directory-tree.vi.md) | docs | Đã đọc |
| [docs/01-overview/feature-showcase.md](../../../docs/01-overview/feature-showcase.md) | docs | Đã đọc |
| [docs/01-overview/feature-showcase.vi.md](../../../docs/01-overview/feature-showcase.vi.md) | docs | Đã đọc |
| [docs/01-overview/pain-points-solved.md](../../../docs/01-overview/pain-points-solved.md) | docs | Đã đọc |
| [docs/01-overview/pain-points-solved.vi.md](../../../docs/01-overview/pain-points-solved.vi.md) | docs | Đã đọc |
| [docs/01-overview/product-overview.md](../../../docs/01-overview/product-overview.md) | docs | Đã đọc |
| [docs/01-overview/product-overview.vi.md](../../../docs/01-overview/product-overview.vi.md) | docs | Đã đọc |
| [docs/01-overview/quickstart.md](../../../docs/01-overview/quickstart.md) | docs | Đã đọc |
| [docs/02-architecture/component-model.md](../../../docs/02-architecture/component-model.md) | docs | Đã đọc |
| [docs/02-architecture/component-model.vi.md](../../../docs/02-architecture/component-model.vi.md) | docs | Đã đọc |
| [docs/02-architecture/design-philosophy.md](../../../docs/02-architecture/design-philosophy.md) | docs | Đã đọc |
| [docs/02-architecture/design-philosophy.vi.md](../../../docs/02-architecture/design-philosophy.vi.md) | docs | Đã đọc |
| [docs/02-architecture/system-architecture.md](../../../docs/02-architecture/system-architecture.md) | docs | Đã đọc |
| [docs/02-architecture/system-architecture.vi.md](../../../docs/02-architecture/system-architecture.vi.md) | docs | Đã đọc |
| [docs/02-architecture/tech-stack.md](../../../docs/02-architecture/tech-stack.md) | docs | Đã đọc |
| [docs/03-components/adapters/mysql-adapter.md](../../../docs/03-components/adapters/mysql-adapter.md) | docs | Đã đọc |
| [docs/03-components/adapters/mysql-adapter.vi.md](../../../docs/03-components/adapters/mysql-adapter.vi.md) | docs | Đã đọc |
| [docs/03-components/adapters/oracle-adapter.md](../../../docs/03-components/adapters/oracle-adapter.md) | docs | Đã đọc |
| [docs/03-components/adapters/oracle-adapter.vi.md](../../../docs/03-components/adapters/oracle-adapter.vi.md) | docs | Đã đọc |
| [docs/03-components/adapters/postgresql-adapter.md](../../../docs/03-components/adapters/postgresql-adapter.md) | docs | Đã đọc |
| [docs/03-components/adapters/postgresql-adapter.vi.md](../../../docs/03-components/adapters/postgresql-adapter.vi.md) | docs | Đã đọc |
| [docs/03-components/adapters/sqlserver-adapter.md](../../../docs/03-components/adapters/sqlserver-adapter.md) | docs | Đã đọc |
| [docs/03-components/adapters/sqlserver-adapter.vi.md](../../../docs/03-components/adapters/sqlserver-adapter.vi.md) | docs | Đã đọc |
| [docs/03-components/contracts/contract-attributes.md](../../../docs/03-components/contracts/contract-attributes.md) | docs | Đã đọc |
| [docs/03-components/contracts/contract-attributes.vi.md](../../../docs/03-components/contracts/contract-attributes.vi.md) | docs | Đã đọc |
| [docs/03-components/core/abstractions.md](../../../docs/03-components/core/abstractions.md) | docs | Đã đọc |
| [docs/03-components/core/abstractions.vi.md](../../../docs/03-components/core/abstractions.vi.md) | docs | Đã đọc |
| [docs/03-components/core/assessment.md](../../../docs/03-components/core/assessment.md) | docs | Đã đọc |
| [docs/03-components/core/assessment.vi.md](../../../docs/03-components/core/assessment.vi.md) | docs | Đã đọc |
| [docs/03-components/core/auto-detection.md](../../../docs/03-components/core/auto-detection.md) | docs | Đã đọc |
| [docs/03-components/core/auto-detection.vi.md](../../../docs/03-components/core/auto-detection.vi.md) | docs | Đã đọc |
| [docs/03-components/core/baseline.md](../../../docs/03-components/core/baseline.md) | docs | Đã đọc |
| [docs/03-components/core/baseline.vi.md](../../../docs/03-components/core/baseline.vi.md) | docs | Đã đọc |
| [docs/03-components/core/plugins.md](../../../docs/03-components/core/plugins.md) | docs | Đã đọc |
| [docs/03-components/core/plugins.vi.md](../../../docs/03-components/core/plugins.vi.md) | docs | Đã đọc |
| [docs/03-components/core/public-api.md](../../../docs/03-components/core/public-api.md) | docs | Đã đọc |
| [docs/03-components/core/public-api.vi.md](../../../docs/03-components/core/public-api.vi.md) | docs | Đã đọc |
| [docs/03-components/core/reporting.md](../../../docs/03-components/core/reporting.md) | docs | Đã đọc |
| [docs/03-components/core/reporting.vi.md](../../../docs/03-components/core/reporting.vi.md) | docs | Đã đọc |
| [docs/03-components/core/rules-engine.md](../../../docs/03-components/core/rules-engine.md) | docs | Đã đọc |
| [docs/03-components/core/rules-engine.vi.md](../../../docs/03-components/core/rules-engine.vi.md) | docs | Đã đọc |
| [docs/03-components/core/security.md](../../../docs/03-components/core/security.md) | docs | Đã đọc |
| [docs/03-components/core/security.vi.md](../../../docs/03-components/core/security.vi.md) | docs | Đã đọc |
| [docs/03-components/core/sources.md](../../../docs/03-components/core/sources.md) | docs | Đã đọc |
| [docs/03-components/core/sources.vi.md](../../../docs/03-components/core/sources.vi.md) | docs | Đã đọc |
| [docs/03-components/core/telemetry.md](../../../docs/03-components/core/telemetry.md) | docs | Đã đọc |
| [docs/03-components/core/telemetry.vi.md](../../../docs/03-components/core/telemetry.vi.md) | docs | Đã đọc |
| [docs/03-components/core/validation.md](../../../docs/03-components/core/validation.md) | docs | Đã đọc |
| [docs/03-components/core/validation.vi.md](../../../docs/03-components/core/validation.vi.md) | docs | Đã đọc |
| [docs/03-components/tooling/analyzers.md](../../../docs/03-components/tooling/analyzers.md) | docs | Đã đọc |
| [docs/03-components/tooling/analyzers.vi.md](../../../docs/03-components/tooling/analyzers.vi.md) | docs | Đã đọc |
| [docs/03-components/tooling/cli.md](../../../docs/03-components/tooling/cli.md) | docs | Đã đọc |
| [docs/03-components/tooling/cli.vi.md](../../../docs/03-components/tooling/cli.vi.md) | docs | Đã đọc |
| [docs/03-components/tooling/code-fixes.md](../../../docs/03-components/tooling/code-fixes.md) | docs | Đã đọc |
| [docs/03-components/tooling/code-fixes.vi.md](../../../docs/03-components/tooling/code-fixes.vi.md) | docs | Đã đọc |
| [docs/03-components/tooling/visual-studio-extension.md](../../../docs/03-components/tooling/visual-studio-extension.md) | docs | Đã đọc |
| [docs/03-components/tooling/visual-studio-extension.vi.md](../../../docs/03-components/tooling/visual-studio-extension.vi.md) | docs | Đã đọc |
| [docs/03-components/tooling/vscode-extension.md](../../../docs/03-components/tooling/vscode-extension.md) | docs | Đã đọc |
| [docs/03-components/tooling/vscode-extension.vi.md](../../../docs/03-components/tooling/vscode-extension.vi.md) | docs | Đã đọc |
| [docs/04-diagrams/activity-diagrams.md](../../../docs/04-diagrams/activity-diagrams.md) | docs | Đã đọc |
| [docs/04-diagrams/activity-diagrams.vi.md](../../../docs/04-diagrams/activity-diagrams.vi.md) | docs | Đã đọc |
| [docs/04-diagrams/component-lifecycle.md](../../../docs/04-diagrams/component-lifecycle.md) | docs | Đã đọc |
| [docs/04-diagrams/data-flow.md](../../../docs/04-diagrams/data-flow.md) | docs | Đã đọc |
| [docs/04-diagrams/data-flow.vi.md](../../../docs/04-diagrams/data-flow.vi.md) | docs | Đã đọc |
| [docs/04-diagrams/sequence-diagrams.md](../../../docs/04-diagrams/sequence-diagrams.md) | docs | Đã đọc |
| [docs/04-diagrams/sequence-diagrams.vi.md](../../../docs/04-diagrams/sequence-diagrams.vi.md) | docs | Đã đọc |
| [docs/04-diagrams/state-machine.md](../../../docs/04-diagrams/state-machine.md) | docs | Đã đọc |
| [docs/04-diagrams/state-machine.vi.md](../../../docs/04-diagrams/state-machine.vi.md) | docs | Đã đọc |
| [docs/05-operations/best-practices.md](../../../docs/05-operations/best-practices.md) | docs | Đã đọc |
| [docs/05-operations/best-practices.vi.md](../../../docs/05-operations/best-practices.vi.md) | docs | Đã đọc |
| [docs/05-operations/configuration-guide.md](../../../docs/05-operations/configuration-guide.md) | docs | Đã đọc |
| [docs/05-operations/configuration-guide.vi.md](../../../docs/05-operations/configuration-guide.vi.md) | docs | Đã đọc |
| [docs/05-operations/installation-guide.md](../../../docs/05-operations/installation-guide.md) | docs | Đã đọc |
| [docs/05-operations/installation-guide.vi.md](../../../docs/05-operations/installation-guide.vi.md) | docs | Đã đọc |
| [docs/05-operations/log-guide.md](../../../docs/05-operations/log-guide.md) | docs | Đã đọc |
| [docs/05-operations/log-guide.vi.md](../../../docs/05-operations/log-guide.vi.md) | docs | Đã đọc |
| [docs/05-operations/playbook.md](../../../docs/05-operations/playbook.md) | docs | Đã đọc |
| [docs/05-operations/playbook.vi.md](../../../docs/05-operations/playbook.vi.md) | docs | Đã đọc |
| [docs/05-operations/runbook.md](../../../docs/05-operations/runbook.md) | docs | Đã đọc |
| [docs/05-operations/runbook.vi.md](../../../docs/05-operations/runbook.vi.md) | docs | Đã đọc |
| [docs/06-roadmap/future-directions.md](../../../docs/06-roadmap/future-directions.md) | docs | Đã đọc |
| [docs/06-roadmap/future-directions.vi.md](../../../docs/06-roadmap/future-directions.vi.md) | docs | Đã đọc |
| [docs/06-roadmap/upgrade-path.md](../../../docs/06-roadmap/upgrade-path.md) | docs | Đã đọc |
| [docs/06-roadmap/upgrade-path.vi.md](../../../docs/06-roadmap/upgrade-path.vi.md) | docs | Đã đọc |
| [docs/07-testing/test-strategy.md](../../../docs/07-testing/test-strategy.md) | docs | Đã đọc |
| [docs/07-testing/test-strategy.vi.md](../../../docs/07-testing/test-strategy.vi.md) | docs | Đã đọc |
| [docs/08-developers/contributor-guide.md](../../../docs/08-developers/contributor-guide.md) | docs | Đã đọc |
| [docs/08-developers/contributor-guide.vi.md](../../../docs/08-developers/contributor-guide.vi.md) | docs | Đã đọc |
| [docs/COMPONENT_INTERACTION.md](../../../docs/COMPONENT_INTERACTION.md) | docs | Đã đọc |
| [docs/DATA_FLOW.md](../../../docs/DATA_FLOW.md) | docs | Đã đọc |
| [docs/FIX_PLAN.md](../../../docs/FIX_PLAN.md) | docs | Đã đọc |
| [docs/PERFORMANCE.md](../../../docs/PERFORMANCE.md) | docs | Đã đọc |
| [docs/PRODUCT.md](../../../docs/PRODUCT.md) | docs | Đã đọc |
| [docs/README.md](../../../docs/README.md) | docs | Đã đọc |
| [docs/README.vi.md](../../../docs/README.vi.md) | docs | Đã đọc |
| [docs/RISKS_GAPS.md](../../../docs/RISKS_GAPS.md) | docs | Đã đọc |
| [docs/SOLUTION.md](../../../docs/SOLUTION.md) | docs | Đã đọc |
| [docs/STAGE_FLOW.md](../../../docs/STAGE_FLOW.md) | docs | Đã đọc |
| [docs/USAGE.md](../../../docs/USAGE.md) | docs | Đã đọc |
| [docs/architecture.md](../../../docs/architecture.md) | docs | Đã đọc |
| [docs/architecture/agent-config.md](../../../docs/architecture/agent-config.md) | docs | Đã đọc |
| [docs/architecture/agent-config.vi.md](../../../docs/architecture/agent-config.vi.md) | docs | Đã đọc |
| [docs/architecture/system_architecture.md](../../../docs/architecture/system_architecture.md) | docs | Đã đọc |
| [docs/architecture/system_architecture.vi.md](../../../docs/architecture/system_architecture.vi.md) | docs | Đã đọc |
| [docs/architecture/tech_stack_evaluation.md](../../../docs/architecture/tech_stack_evaluation.md) | docs | Đã đọc |
| [docs/architecture/tech_stack_evaluation.vi.md](../../../docs/architecture/tech_stack_evaluation.vi.md) | docs | Đã đọc |
| [docs/assess.md](../../../docs/assess.md) | docs | Đã đọc |
| [docs/cli.md](../../../docs/cli.md) | docs | Đã đọc |
| [docs/contributing.md](../../../docs/contributing.md) | docs | Đã đọc |
| [docs/developers/contributor_deep_dive.md](../../../docs/developers/contributor_deep_dive.md) | docs | Đã đọc |
| [docs/developers/contributor_deep_dive.vi.md](../../../docs/developers/contributor_deep_dive.vi.md) | docs | Đã đọc |
| [docs/enterprise-banking-profile.md](../../../docs/enterprise-banking-profile.md) | docs | Đã đọc |
| [docs/golden-standard/PATTERNS.md](../../../docs/golden-standard/PATTERNS.md) | docs | Đã đọc |
| [docs/golden-standard/TEMPLATE_CHECKLIST.md](../../../docs/golden-standard/TEMPLATE_CHECKLIST.md) | docs | Đã đọc |
| [docs/marketplace-publishing.md](../../../docs/marketplace-publishing.md) | docs | Đã đọc |
| [docs/mcp.md](../../../docs/mcp.md) | docs | Đã đọc |
| [docs/operations/playbook_and_runbook.md](../../../docs/operations/playbook_and_runbook.md) | docs | Đã đọc |
| [docs/operations/playbook_and_runbook.vi.md](../../../docs/operations/playbook_and_runbook.vi.md) | docs | Đã đọc |
| [docs/operations/release_guide.md](../../../docs/operations/release_guide.md) | docs | Đã đọc |
| [docs/operations/release_guide.vi.md](../../../docs/operations/release_guide.vi.md) | docs | Đã đọc |
| [docs/overview/vibe_coder_guide.md](../../../docs/overview/vibe_coder_guide.md) | docs | Đã đọc |
| [docs/overview/vibe_coder_guide.vi.md](../../../docs/overview/vibe_coder_guide.vi.md) | docs | Đã đọc |
| [docs/product-discovery/baseline.md](../../../docs/product-discovery/baseline.md) | docs | Đã đọc |
| [docs/product-discovery/capability-matrix.md](../../../docs/product-discovery/capability-matrix.md) | docs | Đã đọc |
| [docs/product-discovery/code-capabilities.md](../../../docs/product-discovery/code-capabilities.md) | docs | Đã đọc |
| [docs/product-discovery/dependency-map.md](../../../docs/product-discovery/dependency-map.md) | docs | Đã đọc |
| [docs/product-discovery/dotnet-legacy-evidence.md](../../../docs/product-discovery/dotnet-legacy-evidence.md) | docs | Đã đọc |
| [docs/product-discovery/opportunity-backlog.md](../../../docs/product-discovery/opportunity-backlog.md) | docs | Đã đọc |
| [docs/product-discovery/problem-market-evidence.md](../../../docs/product-discovery/problem-market-evidence.md) | docs | Đã đọc |
| [docs/product-discovery/raw/01-git-baseline.txt](../../../docs/product-discovery/raw/01-git-baseline.txt) | docs | Phân loại log lịch sử; không dùng làm runtime evidence mới |
| [docs/product-discovery/raw/02-restore.txt](../../../docs/product-discovery/raw/02-restore.txt) | docs | Phân loại log lịch sử; không dùng làm runtime evidence mới |
| [docs/product-discovery/raw/03-build.txt](../../../docs/product-discovery/raw/03-build.txt) | docs | Phân loại log lịch sử; không dùng làm runtime evidence mới |
| [docs/product-discovery/raw/04-build-analyzers.txt](../../../docs/product-discovery/raw/04-build-analyzers.txt) | docs | Phân loại log lịch sử; không dùng làm runtime evidence mới |
| [docs/product-discovery/raw/05-format.txt](../../../docs/product-discovery/raw/05-format.txt) | docs | Phân loại log lịch sử; không dùng làm runtime evidence mới |
| [docs/product-discovery/raw/06-test.txt](../../../docs/product-discovery/raw/06-test.txt) | docs | Phân loại log lịch sử; không dùng làm runtime evidence mới |
| [docs/product-discovery/raw/07-coverage-gate.txt](../../../docs/product-discovery/raw/07-coverage-gate.txt) | docs | Phân loại log lịch sử; không dùng làm runtime evidence mới |
| [docs/product-discovery/release-evidence.md](../../../docs/product-discovery/release-evidence.md) | docs | Đã đọc |
| [docs/product-discovery/source-inventory.md](../../../docs/product-discovery/source-inventory.md) | docs | Đã đọc |
| [docs/sitemap_and_component_registry.md](../../../docs/sitemap_and_component_registry.md) | docs | Đã đọc |
| [docs/sitemap_and_component_registry.vi.md](../../../docs/sitemap_and_component_registry.vi.md) | docs | Đã đọc |
| [docs/testing/qa_test_strategy.md](../../../docs/testing/qa_test_strategy.md) | docs | Đã đọc |
| [docs/testing/qa_test_strategy.vi.md](../../../docs/testing/qa_test_strategy.vi.md) | docs | Đã đọc |
| [plans/2026-08-18-agentize/plan.md](../../../plans/2026-08-18-agentize/plan.md) | plans-research | Đã đọc |
| [plans/2026-08-18-agentize/release-checklist.md](../../../plans/2026-08-18-agentize/release-checklist.md) | plans-research | Đã đọc |
| [plans/2026-08-20-ci-cd-upgrade.md](../../../plans/2026-08-20-ci-cd-upgrade.md) | plans-research | Đã đọc |
| [plans/2026-08-20-workspace-rationalization.md](../../../plans/2026-08-20-workspace-rationalization.md) | plans-research | Đã đọc |
| [plans/2026-08-21-execution-prompt.md](../../../plans/2026-08-21-execution-prompt.md) | plans-research | Đã đọc |
| [plans/2026-08-21-golden-standard-roadmap.md](../../../plans/2026-08-21-golden-standard-roadmap.md) | plans-research | Đã đọc |
| [plans/2026-08-21-redteam-review.md](../../../plans/2026-08-21-redteam-review.md) | plans-research | Đã đọc |
| [plans/2026-08-21-review-handoff.md](../../../plans/2026-08-21-review-handoff.md) | plans-research | Đã đọc |
| [plans/2026-08-21-warnings-plan.md](../../../plans/2026-08-21-warnings-plan.md) | plans-research | Đã đọc |
| [plans/2026-08-25-fix-issues-gaps.md](../../../plans/2026-08-25-fix-issues-gaps.md) | plans-research | Đã đọc |
| [plans/260820-marketplace-extensions/plan.md](../../../plans/260820-marketplace-extensions/plan.md) | plans-research | Đã đọc |
| [plans/260820-marketplace-extensions/reports/marketplace-redteam.md](../../../plans/260820-marketplace-extensions/reports/marketplace-redteam.md) | plans-research | Đã đọc |
| [plans/260820-marketplace-extensions/research/market-positioning.md](../../../plans/260820-marketplace-extensions/research/market-positioning.md) | plans-research | Đã đọc |
| [plans/260820-marketplace-extensions/research/marketplace-publishing.md](../../../plans/260820-marketplace-extensions/research/marketplace-publishing.md) | plans-research | Đã đọc |
| [plans/ACTIVE_SESSION_REGISTER.md](../../../plans/ACTIVE_SESSION_REGISTER.md) | plans-research | Đã đọc |
| [plans/ARCHIVE-ecosupport-history.md](../../../plans/ARCHIVE-ecosupport-history.md) | plans-research | Đã đọc |
| [plans/HANDOVER-deepseek-v4-pro.md](../../../plans/HANDOVER-deepseek-v4-pro.md) | plans-research | Đã đọc |
| [plans/adr/001-v4-architecture.md](../../../plans/adr/001-v4-architecture.md) | plans-research | Đã đọc |
| [plans/adr/002-core-dependency-scope.md](../../../plans/adr/002-core-dependency-scope.md) | plans-research | Đã đọc |
| [plans/adr/003-dotnet-developer-platform-scope.md](../../../plans/adr/003-dotnet-developer-platform-scope.md) | plans-research | Đã đọc |
| [plans/implementation-plan.md](../../../plans/implementation-plan.md) | plans-research | Đã đọc |
| [plans/master-plan.md](../../../plans/master-plan.md) | plans-research | Đã đọc |
| [plans/reports/agentize-agentization-map.md](../../../plans/reports/agentize-agentization-map.md) | plans-research | Đã đọc |
| [plans/reports/agentize-decisions.md](../../../plans/reports/agentize-decisions.md) | plans-research | Đã đọc |
| [plans/reports/research-260823-0647-cross-agent-claude-skills-reuse.md](../../../plans/reports/research-260823-0647-cross-agent-claude-skills-reuse.md) | plans-research | Đã đọc |
| [research/benchmarks/triage_benchmark.py](../../../research/benchmarks/triage_benchmark.py) | plans-research | Đã đọc |
| [research/data/niche_seed_registry.json](../../../research/data/niche_seed_registry.json) | plans-research | Đã đọc |
| [research/muc_tieu/1.md](../../../research/muc_tieu/1.md) | plans-research | Đã đọc |
| [research/muc_tieu/2.md](../../../research/muc_tieu/2.md) | plans-research | Đã đọc |
| [research/muc_tieu/2.txt](../../../research/muc_tieu/2.txt) | plans-research | Đã đọc |
| [research/muc_tieu/3.md](../../../research/muc_tieu/3.md) | plans-research | Đã đọc |
| [research/muc_tieu/4.md](../../../research/muc_tieu/4.md) | plans-research | Đã đọc |
| [research/muc_tieu/5.md](../../../research/muc_tieu/5.md) | plans-research | Đã đọc |
| [research/niche_ecosystem_survey/criticality_model.py](../../../research/niche_ecosystem_survey/criticality_model.py) | plans-research | Đã đọc |
| [research/niche_ecosystem_survey/survey_report_2026.md](../../../research/niche_ecosystem_survey/survey_report_2026.md) | plans-research | Đã đọc |
| [research/python_prototype/__init__.py](../../../research/python_prototype/__init__.py) | plans-research | Đã đọc |
| [research/python_prototype/__pycache__/__init__.cpython-312.pyc](../../../research/python_prototype/__pycache__/__init__.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/agents/__init__.py](../../../research/python_prototype/agents/__init__.py) | plans-research | Đã đọc |
| [research/python_prototype/agents/__pycache__/__init__.cpython-312.pyc](../../../research/python_prototype/agents/__pycache__/__init__.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/agents/__pycache__/doc_bridge_agent.cpython-312.pyc](../../../research/python_prototype/agents/__pycache__/doc_bridge_agent.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/agents/__pycache__/patch_synthesizer.cpython-312.pyc](../../../research/python_prototype/agents/__pycache__/patch_synthesizer.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/agents/__pycache__/triage_agent.cpython-312.pyc](../../../research/python_prototype/agents/__pycache__/triage_agent.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/agents/doc_bridge_agent.py](../../../research/python_prototype/agents/doc_bridge_agent.py) | plans-research | Đã đọc |
| [research/python_prototype/agents/patch_synthesizer.py](../../../research/python_prototype/agents/patch_synthesizer.py) | plans-research | Đã đọc |
| [research/python_prototype/agents/triage_agent.py](../../../research/python_prototype/agents/triage_agent.py) | plans-research | Đã đọc |
| [research/python_prototype/cli/__init__.py](../../../research/python_prototype/cli/__init__.py) | plans-research | Đã đọc |
| [research/python_prototype/cli/__pycache__/__init__.cpython-312.pyc](../../../research/python_prototype/cli/__pycache__/__init__.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/cli/__pycache__/main.cpython-312.pyc](../../../research/python_prototype/cli/__pycache__/main.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/cli/main.py](../../../research/python_prototype/cli/main.py) | plans-research | Đã đọc |
| [research/python_prototype/core/__init__.py](../../../research/python_prototype/core/__init__.py) | plans-research | Đã đọc |
| [research/python_prototype/core/__pycache__/__init__.cpython-312.pyc](../../../research/python_prototype/core/__pycache__/__init__.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/core/__pycache__/client.cpython-312.pyc](../../../research/python_prototype/core/__pycache__/client.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/core/__pycache__/config.cpython-312.pyc](../../../research/python_prototype/core/__pycache__/config.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/core/__pycache__/exceptions.cpython-312.pyc](../../../research/python_prototype/core/__pycache__/exceptions.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/core/__pycache__/telemetry.cpython-312.pyc](../../../research/python_prototype/core/__pycache__/telemetry.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/core/client.py](../../../research/python_prototype/core/client.py) | plans-research | Đã đọc |
| [research/python_prototype/core/config.py](../../../research/python_prototype/core/config.py) | plans-research | Đã đọc |
| [research/python_prototype/core/exceptions.py](../../../research/python_prototype/core/exceptions.py) | plans-research | Đã đọc |
| [research/python_prototype/core/telemetry.py](../../../research/python_prototype/core/telemetry.py) | plans-research | Đã đọc |
| [research/python_prototype/mcp/__init__.py](../../../research/python_prototype/mcp/__init__.py) | plans-research | Đã đọc |
| [research/python_prototype/mcp/__pycache__/__init__.cpython-312.pyc](../../../research/python_prototype/mcp/__pycache__/__init__.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/mcp/__pycache__/server.cpython-312.pyc](../../../research/python_prototype/mcp/__pycache__/server.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/mcp/server.py](../../../research/python_prototype/mcp/server.py) | plans-research | Đã đọc |
| [research/python_prototype/mcp/tools/__pycache__/ecosystem_tools.cpython-312.pyc](../../../research/python_prototype/mcp/tools/__pycache__/ecosystem_tools.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/mcp/tools/__pycache__/security_auditor.cpython-312.pyc](../../../research/python_prototype/mcp/tools/__pycache__/security_auditor.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/mcp/tools/ecosystem_tools.py](../../../research/python_prototype/mcp/tools/ecosystem_tools.py) | plans-research | Đã đọc |
| [research/python_prototype/mcp/tools/security_auditor.py](../../../research/python_prototype/mcp/tools/security_auditor.py) | plans-research | Đã đọc |
| [research/python_prototype/radar/__init__.py](../../../research/python_prototype/radar/__init__.py) | plans-research | Đã đọc |
| [research/python_prototype/radar/__pycache__/__init__.cpython-312.pyc](../../../research/python_prototype/radar/__pycache__/__init__.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/radar/__pycache__/health_analyzer.cpython-312.pyc](../../../research/python_prototype/radar/__pycache__/health_analyzer.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/radar/__pycache__/models.cpython-312.pyc](../../../research/python_prototype/radar/__pycache__/models.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/radar/__pycache__/niche_scanner.cpython-312.pyc](../../../research/python_prototype/radar/__pycache__/niche_scanner.cpython-312.pyc) | plans-research | Phân loại generated binary; không dịch ngược |
| [research/python_prototype/radar/health_analyzer.py](../../../research/python_prototype/radar/health_analyzer.py) | plans-research | Đã đọc |
| [research/python_prototype/radar/models.py](../../../research/python_prototype/radar/models.py) | plans-research | Đã đọc |
| [research/python_prototype/radar/niche_scanner.py](../../../research/python_prototype/radar/niche_scanner.py) | plans-research | Đã đọc |
| [src/DataGuard.Analyzers/Analyzers.cs](../../../src/DataGuard.Analyzers/Analyzers.cs) | tooling | Đã đọc |
| [src/DataGuard.Analyzers/DataGuard.Analyzers.csproj](../../../src/DataGuard.Analyzers/DataGuard.Analyzers.csproj) | tooling | Đã đọc |
| [src/DataGuard.Analyzers/IsExternalInit.cs](../../../src/DataGuard.Analyzers/IsExternalInit.cs) | tooling | Đã đọc |
| [src/DataGuard.Analyzers/packages.lock.json](../../../src/DataGuard.Analyzers/packages.lock.json) | tooling | Đã đọc |
| [src/DataGuard.Analyzers/stylecop.json](../../../src/DataGuard.Analyzers/stylecop.json) | tooling | Đã đọc |
| [src/DataGuard.Cli/DataGuard.Cli.csproj](../../../src/DataGuard.Cli/DataGuard.Cli.csproj) | cli-ide | Đã đọc |
| [src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs](../../../src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs) | cli-ide | Đã đọc |
| [src/DataGuard.Cli/Program.cs](../../../src/DataGuard.Cli/Program.cs) | cli-ide | Đã đọc |
| [src/DataGuard.Cli/packages.lock.json](../../../src/DataGuard.Cli/packages.lock.json) | cli-ide | Đã đọc |
| [src/DataGuard.CodeFixes/CodeFixProviders.cs](../../../src/DataGuard.CodeFixes/CodeFixProviders.cs) | tooling | Đã đọc |
| [src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj](../../../src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj) | tooling | Đã đọc |
| [src/DataGuard.CodeFixes/packages.lock.json](../../../src/DataGuard.CodeFixes/packages.lock.json) | tooling | Đã đọc |
| [src/DataGuard.CodeFixes/stylecop.json](../../../src/DataGuard.CodeFixes/stylecop.json) | tooling | Đã đọc |
| [src/DataGuard.Contracts/ContractAttributes.cs](../../../src/DataGuard.Contracts/ContractAttributes.cs) | tooling | Đã đọc |
| [src/DataGuard.Contracts/DataGuard.Contracts.csproj](../../../src/DataGuard.Contracts/DataGuard.Contracts.csproj) | tooling | Đã đọc |
| [src/DataGuard.Contracts/NameConventions.cs](../../../src/DataGuard.Contracts/NameConventions.cs) | tooling | Đã đọc |
| [src/DataGuard.Contracts/packages.lock.json](../../../src/DataGuard.Contracts/packages.lock.json) | tooling | Đã đọc |
| [src/DataGuard.Contracts/stylecop.json](../../../src/DataGuard.Contracts/stylecop.json) | tooling | Đã đọc |
| [src/DataGuard.Core/Abstractions/Contracts.cs](../../../src/DataGuard.Core/Abstractions/Contracts.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Assessment/AssessmentContracts.cs](../../../src/DataGuard.Core/Assessment/AssessmentContracts.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/AssessmentEngine.cs](../../../src/DataGuard.Core/Assessment/AssessmentEngine.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/Internal/AssessmentReportWriter.cs](../../../src/DataGuard.Core/Assessment/Internal/AssessmentReportWriter.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/Internal/BuildCiPack.cs](../../../src/DataGuard.Core/Assessment/Internal/BuildCiPack.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/Internal/DependencyHealthPack.cs](../../../src/DataGuard.Core/Assessment/Internal/DependencyHealthPack.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/Internal/InventoryPack.cs](../../../src/DataGuard.Core/Assessment/Internal/InventoryPack.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/Internal/PackagesConfigReader.cs](../../../src/DataGuard.Core/Assessment/Internal/PackagesConfigReader.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/Internal/ProjectInventoryReader.cs](../../../src/DataGuard.Core/Assessment/Internal/ProjectInventoryReader.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/Internal/SecretsPack.cs](../../../src/DataGuard.Core/Assessment/Internal/SecretsPack.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/LegacySupportTable.cs](../../../src/DataGuard.Core/Assessment/LegacySupportTable.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Assessment/UpgradePlanner.cs](../../../src/DataGuard.Core/Assessment/UpgradePlanner.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs](../../../src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Baseline/BaselineManager.cs](../../../src/DataGuard.Core/Baseline/BaselineManager.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/DataGuard.Core.csproj](../../../src/DataGuard.Core/DataGuard.Core.csproj) | core-engine | Đã đọc |
| [src/DataGuard.Core/Models/Configuration.cs](../../../src/DataGuard.Core/Models/Configuration.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Plugins/RulePluginManager.cs](../../../src/DataGuard.Core/Plugins/RulePluginManager.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/PublicApi/PublicApiSurface.cs](../../../src/DataGuard.Core/PublicApi/PublicApiSurface.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Reporting/ContractEvidence.cs](../../../src/DataGuard.Core/Reporting/ContractEvidence.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Reporting/ContractExport.cs](../../../src/DataGuard.Core/Reporting/ContractExport.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Reporting/DiagnosticEmitter.cs](../../../src/DataGuard.Core/Reporting/DiagnosticEmitter.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Reporting/SarifTypes.cs](../../../src/DataGuard.Core/Reporting/SarifTypes.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Rules/ContractRules.cs](../../../src/DataGuard.Core/Rules/ContractRules.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Rules/PhantomIdentifierRule.cs](../../../src/DataGuard.Core/Rules/PhantomIdentifierRule.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Rules/RuleDependencyGraph.cs](../../../src/DataGuard.Core/Rules/RuleDependencyGraph.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Security/CredentialManager.cs](../../../src/DataGuard.Core/Security/CredentialManager.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Security/IAuditLogger.cs](../../../src/DataGuard.Core/Security/IAuditLogger.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Security/SupplyChainVerifier.cs](../../../src/DataGuard.Core/Security/SupplyChainVerifier.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs](../../../src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Sources/EfModelSource.cs](../../../src/DataGuard.Core/Sources/EfModelSource.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Sources/ManualContractSource.cs](../../../src/DataGuard.Core/Sources/ManualContractSource.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Sources/SqlKeywordMatcher.cs](../../../src/DataGuard.Core/Sources/SqlKeywordMatcher.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Sources/SqlServerParsers.cs](../../../src/DataGuard.Core/Sources/SqlServerParsers.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/Telemetry/TelemetryCollector.cs](../../../src/DataGuard.Core/Telemetry/TelemetryCollector.cs) | core-services | Đã đọc |
| [src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs](../../../src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs) | core-engine | Đã đọc |
| [src/DataGuard.Core/packages.lock.json](../../../src/DataGuard.Core/packages.lock.json) | core-engine | Đã đọc |
| [src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj](../../../src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj) | adapters | Đã đọc |
| [src/DataGuard.MySql.Adapter/MySqlDialectChecker.cs](../../../src/DataGuard.MySql.Adapter/MySqlDialectChecker.cs) | adapters | Đã đọc |
| [src/DataGuard.MySql.Adapter/MySqlLengthMismatchDetector.cs](../../../src/DataGuard.MySql.Adapter/MySqlLengthMismatchDetector.cs) | adapters | Đã đọc |
| [src/DataGuard.MySql.Adapter/MySqlStoredProcedureParser.cs](../../../src/DataGuard.MySql.Adapter/MySqlStoredProcedureParser.cs) | adapters | Đã đọc |
| [src/DataGuard.MySql.Adapter/packages.lock.json](../../../src/DataGuard.MySql.Adapter/packages.lock.json) | adapters | Đã đọc |
| [src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj](../../../src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj) | adapters | Đã đọc |
| [src/DataGuard.Oracle.Adapter/LengthMismatch.cs](../../../src/DataGuard.Oracle.Adapter/LengthMismatch.cs) | adapters | Đã đọc |
| [src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs](../../../src/DataGuard.Oracle.Adapter/OracleDialectChecker.cs) | adapters | Đã đọc |
| [src/DataGuard.Oracle.Adapter/OracleReaders.cs](../../../src/DataGuard.Oracle.Adapter/OracleReaders.cs) | adapters | Đã đọc |
| [src/DataGuard.Oracle.Adapter/packages.lock.json](../../../src/DataGuard.Oracle.Adapter/packages.lock.json) | adapters | Đã đọc |
| [src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj](../../../src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj) | adapters | Đã đọc |
| [src/DataGuard.PostgreSql.Adapter/PostgreSqlDialectChecker.cs](../../../src/DataGuard.PostgreSql.Adapter/PostgreSqlDialectChecker.cs) | adapters | Đã đọc |
| [src/DataGuard.PostgreSql.Adapter/PostgreSqlLengthMismatchDetector.cs](../../../src/DataGuard.PostgreSql.Adapter/PostgreSqlLengthMismatchDetector.cs) | adapters | Đã đọc |
| [src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs](../../../src/DataGuard.PostgreSql.Adapter/PostgreSqlStoredProcedureParser.cs) | adapters | Đã đọc |
| [src/DataGuard.PostgreSql.Adapter/packages.lock.json](../../../src/DataGuard.PostgreSql.Adapter/packages.lock.json) | adapters | Đã đọc |
| [src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj](../../../src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj) | adapters | Đã đọc |
| [src/DataGuard.SqlServer.Adapter/packages.lock.json](../../../src/DataGuard.SqlServer.Adapter/packages.lock.json) | adapters | Đã đọc |
| [src/DataGuard.VSCode/.vscodeignore](../../../src/DataGuard.VSCode/.vscodeignore) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/LICENSE](../../../src/DataGuard.VSCode/LICENSE) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/README.md](../../../src/DataGuard.VSCode/README.md) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/package-lock.json](../../../src/DataGuard.VSCode/package-lock.json) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/package.json](../../../src/DataGuard.VSCode/package.json) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/src/extension.ts](../../../src/DataGuard.VSCode/src/extension.ts) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/src/security.test.ts](../../../src/DataGuard.VSCode/src/security.test.ts) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/src/security.ts](../../../src/DataGuard.VSCode/src/security.ts) | cli-ide | Đã đọc |
| [src/DataGuard.VSCode/tsconfig.json](../../../src/DataGuard.VSCode/tsconfig.json) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/Commands/DataGuard.vsct](../../../src/DataGuard.VisualStudio/Commands/DataGuard.vsct) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj](../../../src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/DataGuardPackage.cs](../../../src/DataGuard.VisualStudio/DataGuardPackage.cs) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/LICENSE.txt](../../../src/DataGuard.VisualStudio/LICENSE.txt) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/overview.md](../../../src/DataGuard.VisualStudio/overview.md) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/packages.lock.json](../../../src/DataGuard.VisualStudio/packages.lock.json) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/source.extension.vsixmanifest](../../../src/DataGuard.VisualStudio/source.extension.vsixmanifest) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/stylecop.json](../../../src/DataGuard.VisualStudio/stylecop.json) | cli-ide | Đã đọc |
| [src/DataGuard.VisualStudio/vs-publish.json](../../../src/DataGuard.VisualStudio/vs-publish.json) | cli-ide | Đã đọc |

## Phần chưa xác minh

Không còn file text/source baseline chưa đọc theo ledger. Visual Studio runtime/build trên Windows, DB-backed assertions, release/marketplace, benchmark và coverage mới vẫn chưa được xác minh; xem [verification](verification.md). Đây không phải phạm vi bị bỏ sót trong inventory.
