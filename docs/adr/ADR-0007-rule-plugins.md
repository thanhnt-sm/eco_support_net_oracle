# ADR-0007: Runtime rule plugins (`DataGuard.Core.Plugins`)

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`. Phase 4.2 of the remediation plan was not merged at this commit.
- **Origin:** remediation plan Phase 6.1, red-team report §4 and §3.3, recommendation 22

## Context

`src/DataGuard.Core/Plugins` loads third-party `IContractRule` implementations at run time.

- `PluginAdmission.cs` (417 LOC) admits a plugin only if all of these hold:
  - its manifest SHA-256 matches the assembly bytes;
  - every managed dependency is declared and hash-checked;
  - its assembly identity and references are within the host's allowed set;
  - its rule IDs do not collide with reserved IDs;
  - a provenance verifier accepts it (the default overload requires one).
- `RulePluginManager.cs` (428 LOC) loads the verified bytes, never the path again, into a collectible
  `AssemblyLoadContext` with MEF 2. It blocks native libraries. Its own comment says the context gives
  "lifecycle/type-resolution isolation, not a sandbox".

Who reaches it:

- **At `5603818`:** only the library API (`ValidationPipeline.WithPlugins` in
  `PublicApi/PublicApiSurface.cs`). The CLI does not load plugins (report §3.3, "CLI không nạp
  plugin").
- **After plan Phase 4.2:** the CLI gains `--plugins-dir`, with admission enforced and refused under
  `--ide-safe`. 4.2 also fixes the metadata bug below and adds a test that builds and loads a real
  plugin. From then on plugins can be loaded from the CLI.

Known defects at `5603818` (report §3.3 and §7):

- `RulePluginManager.cs:141` reads `ExportMetadataAttribute` instead of `ExportRuleAttribute`, so every
  plugin gets `RuleId = ""`, and grouping by ID keeps only one plugin.
- No test loads a plugin successfully.
- Plugins can be filtered out because of `AssemblyVersion 0.0.0.0` when MinVer has no tag.

## Original goal said

- `research/muc_tieu/3.md:30`: "Cấu trúc này vừa sạch về pháp lý cho hồ sơ Anthropic, vừa là kiến trúc
  plugin tốt cho khả năng mở rộng sang PostgreSQL/MySQL sau này — mỗi vendor một adapter riêng, core
  không đổi." (A good plugin architecture for extending to PostgreSQL/MySQL later: one adapter per
  vendor, core unchanged.) The "plugin" here means separately packaged, compile-time vendor adapters,
  not loading arbitrary rule assemblies at run time.
- `research/muc_tieu/2.md:24`: the supply-chain warning. A tool that runs with high privilege in CI is
  an attractive target, and loading third-party code into it raises that risk directly.

No goal asks for user-supplied rules loaded at run time.

## What exists

| Item | Measurement |
|---|---|
| Production code | 845 LOC, 2 files |
| Tests | `PluginAdmissionTests` (17 tests, 434 LOC): hash-bound load, provenance required, symlink and native rejection. `PublicApiAndPipelineTests` and `ConcurrentValidationExecutionTests` touch plugin paths. No end-to-end plugin load test at `5603818`. |
| Dependencies | `System.Composition.Hosting` (MEF 2), declared in `DataGuard.Core.csproj` |
| Solution membership | Part of `DataGuard.Core`, so it is in `DataGuard.CrossPlatform.slnf`. |
| CI | `ci.yml` `build-and-test`. |
| Release | Ships in `DataGuard.Core` and every CLI artifact. |
| Docs | `docs/03-components/core/plugins.md` (+ `.vi.md`) |

## Risks it adds

- **Attack surface:** running third-party code in a process that may hold database credentials and
  CI secrets is the highest-impact surface in the product. The admission design is strong, but the
  load context is "not a sandbox". Admission is the only barrier.
- **Correctness:** at `5603818` the feature has never worked end to end (metadata bug). A security
  control that has never run in a real load has not been validated.
- **Two pipelines:** plugins attach only to `ValidationPipeline`, while the CLI uses
  `ProviderRuleCatalog`. Phase 4.2 unifies them, and this ADR depends on that.
- **Maintenance:** MEF 2 plus custom admission and load-context code, about 850 LOC, to support an
  extension model with no known plugin authors.
- **Licence:** a plugin is loaded into the same process as `GPL-3.0-only` code. The §7 additional
  permission in `docs/legal/ADDITIONAL-PERMISSIONS.md` covers database drivers, IDEs and host
  programs. It does not mention third-party rule plugins. Whether a proprietary rule plugin may be
  combined with DataGuard is therefore an open question. It belongs with the owner's legal-review item
  (plan Phase 6.2) before plugins are advertised.

## Options

1. **Keep:** make plugins a supported, documented extension point, CLI included (Phase 4.2 as
   planned).
2. **Freeze:** keep the code, fix the defects in Phase 4.2, and keep CLI loading off by default and
   refused under `--ide-safe`. No new extension features such as plugin discovery, remote feeds or
   signing services.
3. **Extract:** take runtime plugins out of Core. Extensibility becomes compile-time only: custom
   rules are added by referencing `DataGuard.Core` and composing a `ValidationPipeline` in code.
4. **Remove:** not available in this plan.

## Recommendation

**`freeze`**. Re-evaluate when Phase 4.2 merges.

- The goals asked for vendor adapters, not runtime plugins. Runtime loading widens the attack surface
  that expert 3 warned about. That argues against growing the feature.
- The admission work is one of the report's acknowledged strengths (§5: hash-bound load, mandatory
  provenance verifier, symlink and native blocking, 17 tests). Phase 4.2 already pays the cost of
  making it correct. Throwing that away before it has run once would waste the work.
- When 4.2 lands, the owner chooses between:
  - `keep`, if the round-trip test passes, CLI loading stays opt-in and IDE-safe refuses it, and there
    is a real plugin use case;
  - `extract` to compile-time-only extensibility, if no use case appears.

## Owner decision

`pending`. (Fill in: decision, date, name. Re-check after Phase 4.2.)

## Consequences

- No new plugin features while frozen. Phase 4.2's fixes (metadata, `--plugins-dir`, round-trip test)
  are defect repairs and are allowed under freeze.
- After 4.2, `docs/03-components/core/plugins.md` and `docs/03-components/tooling/cli.md` document
  `--plugins-dir` as opt-in, with the admission requirements and the IDE-safe refusal.
- An `extract` decision would remove `System.Composition.Hosting` from Core.

## Tóm tắt (VI)

Plugins (845 LOC, 17 test admission) cho phép nạp rule của bên thứ ba lúc chạy, với kiểm tra hash,
provenance verifier bắt buộc, chặn symlink và thư viện native. Tại commit `5603818`, CLI chưa nạp
plugin, và lỗi đọc metadata (`ExportMetadataAttribute`) làm mọi plugin có `RuleId` rỗng. Phase 4.2 sẽ
sửa lỗi này và thêm `--plugins-dir` cho CLI (bị từ chối khi `--ide-safe`), nên sau đó plugin nạp được
từ CLI. Mục tiêu gốc (3.md) chỉ nói "kiến trúc plugin" theo nghĩa mỗi vendor một adapter, không phải
nạp code tùy ý lúc chạy, và chuyên gia 3 đã cảnh báo về bề mặt tấn công. Khuyến nghị: **freeze**, cho
phép Phase 4.2 sửa lỗi, rồi đánh giá lại: keep nếu có use case thật và test round-trip pass, extract
(chỉ mở rộng lúc biên dịch) nếu không. Quyết định của owner: pending.
