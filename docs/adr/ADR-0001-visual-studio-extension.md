# ADR-0001: Visual Studio extension (`src/DataGuard.VisualStudio`)

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4, recommendation 22

## Context

`src/DataGuard.VisualStudio` is a `net472` VSIX for Visual Studio 2022. It has no `ProjectReference`
to the product projects. It runs the bundled CLI (`cli\dataguard.exe`) as a child process, and every
command line it builds carries `--ide-safe` (`CliArgumentBuilder.cs:13-21`). It then publishes the
SARIF output to the Error List. A solution trust gate stops it from running in untrusted solutions. At
VSIX build time the target `IncludeAnalyzersInVsix` also copies `DataGuard.Analyzers.dll` and
`DataGuard.CodeFixes.dll` into the VSIX (`DataGuard.VisualStudio.csproj`).

Because the extension calls the CLI under `--ide-safe` and does no semantic or database work in the
IDE process, it does **not** break the "light IDE, heavy CI" principle (report §4).

## Original goal said

- `research/muc_tieu/1.md:40`: "Sản phẩm nên là gì: NuGet-distributed Roslyn Analyzer, không phải VS
  extension". (The product should be a NuGet-distributed Roslyn analyzer, not a VS extension.)
- `research/muc_tieu/1.md:44`: "VS extension không chạy được trong CI/command-line build ... nếu cài
  dưới dạng VS extension, nó chỉ áp dụng ở cấp độ solution trong riêng Visual Studio". (An analyzer
  installed as a VS extension does not run in CI or command-line builds.)
- `research/muc_tieu/1.md:51`: "Không làm VS extension ở giai đoạn đầu — có thể cân nhắc sau này chỉ để
  làm trải nghiệm gõ-code-live (live squiggle) như một lớp bổ sung tùy chọn, không phải kênh phân phối
  chính." (No VS extension in the first phase. It may come later only as an optional live-squiggle
  layer, never as the main distribution channel.)

## What exists

| Item | Measurement |
|---|---|
| Production code | 4,846 LOC in 35 `.cs` files |
| Tests | `tests/DataGuard.VisualStudio.Tests`: 2,305 LOC, 16 files, 123 test methods (`CliArgumentBuilderTests`, `CliProcessRegistryTests`, `CliRunSessionLiveTests`, `DataGuardPackageTests`, `InfoBarManagerTests`, `NavigationTests`, `ProcessTerminatorClassificationTests`, `ProgressPumpTests`, `ProgressStreamReaderTests`, `RuleInventoryTests`, `SarifErrorListPublisherTests`, `SolutionLifetimeWatcherTests`, `SolutionTrustGateTests`, `VsixAnalyzerPackagingTests`) |
| Solution membership | `DataGuard.sln` only. Not in `DataGuard.CrossPlatform.slnf` (Windows only). |
| CI | `ci.yml`: `visual-studio-build-and-test` and `visual-studio-vsix-package` (both `windows-latest`). |
| Release / publish | `release.yml` `visual-studio-package` (feeds `sign-packages` and `create-github-release`); `build_release.yml` `package-visualstudio`; `installers.yml` `visualstudio`; `marketplace.yml` `visual-studio-package` and `publish-visualstudio` (uses `VS_MARKETPLACE_PAT`). |
| Docs | `docs/03-components/tooling/visual-studio-extension.md` (+ `.vi.md`) |

## Risks it adds

- **Maintenance:** five workflows need Windows runners. The extension depends on the VS SDK
  (`Microsoft.VisualStudio.SDK` 17.14, `Microsoft.VSSDK.BuildTools` 18.5) and on `net472`, a second
  target framework family next to `net9.0`.
- **Attack surface:** it starts a process from inside the IDE. This is mitigated by `--ide-safe`, the
  trust gate and the zero-`ProjectReference` design. The publish job holds a Marketplace PAT.
- **Licence:** the VSIX bundles the CLI, and the CLI carries `Oracle.ManagedDataAccess.Core`. The
  Oracle redistribution terms therefore travel with the VSIX. The shipped `THIRD-PARTY-NOTICES.md`
  copy is checked byte-for-byte by `scripts/check-license-consistency.py`.
- **Release size and signing:** the VSIX contains a `win-x64` self-contained single-file CLI
  (`dotnet publish ... --self-contained true /p:PublishSingleFile=true`, `DataGuard.VisualStudio.csproj:121`)
  plus the analyzers. That inner publish runs with `RestoreLockedMode=false`, so it does not get the
  locked-restore guarantee the rest of the build has. Authenticode signing of the VSIX is still a
  deferred owner item (plan Phase 6.2).
- **Two delivery paths for the analyzers:** the VSIX copy and the NuGet package can drift in version.
  The VSIX copy is the IDE-only setup that `1.md:44` warns about. The NuGet analyzer is still the path
  that runs in CI, so no gate is lost.

## Options

1. **Keep:** first-class channel, with feature work alongside the CLI.
2. **Freeze:** keep it building, tested and released. Only fixes, security work and changes forced by
   CLI contract changes. No new features.
3. **Extract:** move it to a separate repository that consumes released CLI artifacts, with its own
   VSIX build, signing and Marketplace pipeline.
4. **Remove:** not available in this plan. It would need a governance manifest and an owner decision.

## Recommendation

**`freeze`**

- The extension already is the "optional live layer later" that `1.md:51` allows. It delegates all
  real work to the CLI under `--ide-safe`, so the CI gate stays in the NuGet analyzer and the CLI, as
  the goals require.
- It has 123 tests and dedicated CI jobs, so keeping it green is cheap. Extracting it would duplicate
  the signing, SBOM and attestation pipeline in a second repository.
- New work on it competes with the MVP core, which the report found was not wired. Freeze until the
  Phase 6.3 end-to-end gate (`dataguard validate` on four providers) is green, then revisit.
- Revisit as `extract` if the Windows CI cost or the next VS SDK major upgrade becomes a recurring
  burden.

## Owner decision

`pending`. (Fill in: decision, date, name.)

## Consequences

- Docs label the extension "frozen" and link this ADR. The Marketplace listing does not promise new
  features.
- Feature requests for the IDE go to the CLI or analyzer first. The extension only displays results.
- Possible follow-up (separate ADR): stop embedding the analyzers in the VSIX and rely on the NuGet
  package. That would remove the version-drift risk.

## Tóm tắt (VI)

Extension Visual Studio (4.846 LOC, 2.305 LOC test với 123 test, 2 job CI Windows và 4 workflow phát
hành) đi ngược khuyến nghị của 1.md là không làm VS extension làm kênh chính. Dù vậy, nó chỉ gọi CLI
qua `--ide-safe` nên vẫn giữ nguyên tắc "IDE nhẹ", đúng kiểu "lớp bổ sung tùy chọn sau này" mà 1.md
cho phép. Khuyến nghị: **freeze**. Giữ build, test và phát hành, chỉ sửa lỗi và bảo mật, không thêm
tính năng cho đến khi cổng e2e của lõi MVP xanh. Nếu chi phí CI Windows hoặc nâng cấp VS SDK trở thành
gánh nặng thì xem xét extract. Quyết định của owner: pending.
