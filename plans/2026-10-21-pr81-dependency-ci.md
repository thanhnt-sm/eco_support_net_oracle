# PR81 dependency CI reconciliation

## Baseline and scope

PR81 starts at `68eb4a911eb3df3c0a6544f09ff05b14f72374c0`, based on main `39a030e1a99b75e92d8bf68f8bdc62428ac5923e`. Its dependency group updates AWSSDK.SecretsManager `4.0.100.20`, FsCheck.Xunit `3.5.0`, OpenTelemetry `1.20.1` with instrumentation `1.20.0`, Oracle.ManagedDataAccess.Core `23.26.400`, and the Windows test collector. This follow-up preserves the valid dependency updates and restores only the existing net472 collector compatibility pin.

The work belongs to `fix/pr81-dependency-ci`; it does not integrate PR83's fourteen source-maintenance commits or the separate DG020 ownership patch. The only changed-file overlap with PR83 is Core.Tests.csproj: dependency references versus additional linked test sources. This is source inspection, not proof that every possible merge order has been built. The combined state requires normal CI after merging either PR.

## Confirmed failures and correction

1. Full-solution locked restore at the original PR head fails NU1004 in seven projects because referenced Oracle/CLI/OpenTelemetry dependencies changed without propagating their lock graphs. The observability benchmark outside the solution has the same independent locked-restore failure. The fix regenerates affected locks through NuGet for the full solution and that benchmark; locked-mode verification remains enabled.
2. Windows CI job `118998415289` in run `39531565305` passes 283 net472 tests with one existing skip, but coverage validation fails. Its collector diagnostic output reports a `System.Reflection.Metadata, Version=10.0.0.0` FileLoadException/manifest mismatch during instrumentation-eligibility probing, then 116 selected modules and zero instrumented modules. The existing project comment already requires coverlet.collector `6.0.4` until a real Windows collector run verifies an upgrade. Restore that project-only pin and its generated lock entry; keep collector `10.1.0` for the net9 test projects. No binding redirects, runtime defaults, report exclusions or coverage thresholds are changed.

The failed Marketplace packaging run `39531565349` stops at dependency restore; the two packaging jobs are skipped by dependency ordering. A green test count without a valid nonempty coverage report does not satisfy the Windows gate. No artifact download is necessary for this diagnosis: repository-scoped job metadata and decoded log reads provide the evidence.

## Verification contract

The regression oracles are the original locked-restore failures and the real Windows empty-coverage failure. Verify full-solution locked restore, benchmark locked restore, full solution compilation, focused FsCheck/observability tests, license checks and the unchanged canonical pre-push gates. Observe new hosted Windows coverage validation after the non-force update to the same PR branch. Linux compilation cannot certify the net472 collector's runtime behavior.

No gate or audit is disabled; no hand-edited lock content hashes, global collector ignore rule or broad package rollback is used. Default full validation stays unchanged. Standard Release-configuration compilation is verification, not an installer/release build or workflow dispatch. The user retains manual merge and installer-build control. The agent performs no PR merge, release dispatch, Marketplace publication, artifact retrieval or Library transfer; existing hosted CI artifact handling is unchanged.

## Tiếng Việt

PR81 có hai nguyên nhân độc lập: lock graph transitive chưa đồng bộ gây NU1004; collector 10.1.0 trong test net472 không nạp đúng System.Reflection.Metadata 10 nên không instrument module nào, dù 283 test vẫn pass. Tạo lại lockfile bằng NuGet và giữ pin 6.0.4 riêng cho net472; không hoàn tác các nâng cấp AWS/FsCheck/OTel/Oracle hợp lệ, không giảm gate. Test collector trên Windows phải được xác nhận bằng CI mới; build Linux không thay bằng chứng này. PR83 và patch ownership DG020 vẫn độc lập. Người dùng tự merge và chạy build file cài.

## Local verification checkpoint

Full-solution locked restore and the observability benchmark locked restore pass after NuGet-generated propagation. Full solution Release build passes with zero warnings/errors; license verification accepts 337 NuGet packages and nine production npm packages. A focused test permission request was aborted without stdout, session or exit code, so no focused test result is claimed. The unchanged canonical pre-push suite and new hosted Windows collector result remain required; this checkpoint is not release readiness.
