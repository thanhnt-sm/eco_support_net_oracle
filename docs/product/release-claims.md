# Release claim controls

Draft, 2026-10-18. This task approves no publication. [Marketplace copy](marketplace-copy.md) is unpublished wording, pending artifact and host verification.

| Claim | EN / VI approved draft scope | Evidence and limitations | Release disposition |
|---|---|---|---|
| SA-CLM-001 | Inspect supported .NET/database contracts / Kiểm tra hợp đồng .NET/database được hỗ trợ | SA-REQ-001, SA-CAP-001–003; source-map symbols and named tests; provider-specific gaps | Source-backed draft; exact artifact/test run and support matrix required |
| SA-CLM-002 | Carry diagnostic evidence into CLI/editor workflows / Đưa bằng chứng chẩn đoán vào CLI/editor | SA-REQ-003,006; SA-CAP-004; SARIF and extension source; availability differs by host | Host/version/artifact acceptance pending |
| SA-CLM-003 | Export descriptors to TypeScript / Xuất descriptor sang TypeScript | SA-REQ-002, SA-CAP-005; ContractExport.cs and ValidateCommand early return | CLI capability only; never “validated end-to-end React contract” |
| SA-CLM-004 | Beta brand direction / Định hướng thương hiệu Beta | StackAccord accepted direction; Visual Studio display metadata updated locally, technical identity unchanged | Display-name source preparation, not installed acceptance or Marketplace publication |

For each eventual release append immutable SHA, artifact hash/version, exact run, provider/mode/host, exception list, reviewer, date and review trigger. None is supplied by merely linking a test file. Merged PR #74 aligns current source metadata to 0.4.0 and the VS Code engine to ^1.91.0. This establishes source configuration only: ordinary CI binary versions can still be development versions without the manual release override. Manual release-identity checks passed for the exact preparation below, while installed-editor acceptance remains pending; earlier benchmark/coverage results are not new acceptance evidence.

Prohibited without new evidence: provider parity, guaranteed prevention, automatic safe repairs, full React coverage, unmeasured latency, production-ready observability, no false positives, banking certification, enterprise compliance, current package availability inferred from source. Preserve actual publisher/package/CLI identities. Before distribution verify existing nightly-user upgrade, settings retention, uninstall and duplicate-analyzer behavior on actual hosts.

Legal authority remains the existing LICENSE and docs/legal files, including historical-version and Contracts exceptions; this document neither changes license terms nor interprets customer obligations. Marketplace pause remains in force.

## Verified build-only preparation / Chuẩn bị build-only đã kiểm chứng

On 2026-10-19, [manual preparation run 39176474395](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/39176474395) for source `121b05d166c359c1d520d035dca40e86ea9cca1f` completed five jobs successfully; both publication jobs were skipped. The Windows rejection harness and strict `RequireReleaseIdentity` package inspection passed. Independent artifact-byte inspection recorded manifest/catalog/registration `0.4.0`, all six checked DataGuard DLL/CLI file versions `0.4.0.0`, product version `0.4.0+121b05d166c359c1d520d035dca40e86ea9cca1f`, and legal bytes matching source. Artifact ID: `12154623150`; VSIX SHA-256: `e2d7555bf4f5b15bf5f85cdf4b3758d401d78a218c0bb3f2a22470f6a73464ac`.

This evidence applies to those exact prepared bytes. It does not prove installed-editor diagnostics, quick fixes, upgrade behavior, Marketplace publication, or acceptance of a later rebuilt artifact. Ordinary PR/nightly artifacts without the explicit override can still carry development binary versions. No release tag was created by this documentation integration.

Tiếng Việt: Run build-only trên đã đạt năm job; hai job công bố bị skip. Harness từ chối và kiểm tra release identity nghiêm ngặt trên Windows đạt; kiểm tra byte độc lập ghi nhận phiên bản manifest/catalog/registration 0.4.0, file version 0.4.0.0 và product version gắn đúng SHA nguồn, cùng nội dung pháp lý khớp nguồn. Bằng chứng chỉ áp dụng cho artifact/hash nêu trên; chưa chứng minh cài editor, diagnostic/quickfix, upgrade hoặc phát hành Marketplace. Không coi gói rebuild khác là đã nghiệm thu.
