# Release claim controls

Draft, 2026-10-18. This task approves no publication. [Marketplace copy](marketplace-copy.md) is unpublished wording, pending artifact and host verification.

| Claim | EN / VI approved draft scope | Evidence and limitations | Release disposition |
|---|---|---|---|
| SA-CLM-001 | Inspect supported .NET/database contracts / Kiểm tra hợp đồng .NET/database được hỗ trợ | SA-REQ-001, SA-CAP-001–003; source-map symbols and named tests; provider-specific gaps | Source-backed draft; exact artifact/test run and support matrix required |
| SA-CLM-002 | Carry diagnostic evidence into CLI/editor workflows / Đưa bằng chứng chẩn đoán vào CLI/editor | SA-REQ-003,006; SA-CAP-004; SARIF and extension source; availability differs by host | Host/version/artifact acceptance pending |
| SA-CLM-003 | Export descriptors to TypeScript / Xuất descriptor sang TypeScript | SA-REQ-002, SA-CAP-005; ContractExport.cs and ValidateCommand early return | CLI capability only; never “validated end-to-end React contract” |
| SA-CLM-004 | Beta brand direction / Định hướng thương hiệu Beta | StackAccord accepted direction; metadata unchanged | Display name draft, not a shipped rebrand |

For each eventual release append immutable SHA, artifact hash/version, exact run, provider/mode/host, exception list, reviewer, date and review trigger. None is supplied by merely linking a test file. Existing baseline metadata is 0.3.0; separate unmerged PR #74 proposes 0.4.0. Neither that patch nor earlier benchmark/coverage results are imported as evidence for this branch.

Prohibited without new evidence: provider parity, guaranteed prevention, automatic safe repairs, full React coverage, unmeasured latency, production-ready observability, no false positives, banking certification, enterprise compliance, current package availability inferred from source. Preserve actual publisher/package/CLI identities. Before distribution verify existing nightly-user upgrade, settings retention, uninstall and duplicate-analyzer behavior on actual hosts.

Legal authority remains the existing LICENSE and docs/legal files, including historical-version and Contracts exceptions; this document neither changes license terms nor interprets customer obligations. Marketplace pause remains in force.
