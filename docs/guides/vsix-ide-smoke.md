# Stage1: manual VSIX install/load smoke candidate

This harness tests installation of one exact built VSIX and package load in an empty, unique experimental profile. It does not validate SQL diagnostics, solution trust, Error List navigation, CLI behavior, or a user's Visual Studio 18.10.2 installation. Those need a separate Stage2 and explicit fixture/consent design.

## Evidence levels and current status

| Layer | Current evidence | Meaning |
| --- | --- | --- |
| Python orchestration | Local unit tests, including mocked success and failures | Verifies sequencing, target binding, byte checks, fail-closed status, cleanup scope and evidence privacy; does not execute Windows tools |
| PowerShell syntax/C# helper | Windows PowerShell parse/compile gate authored in manual workflow; not yet run | Must pass on Windows before trusting the probe |
| Installer capabilities | Actual selected installation's `VSIXInstaller.exe /?` required at runtime; not yet observed | Both `/instanceIds:` and `/rootSuffix:` plus `/quiet` must be advertised before install is attempted; unrecognized/localized help blocks |
| Real install/load | **BLOCKED: not executed** in current Linux environment | Requires an authorized disposable hosted Windows run and supported unattended IDE/session/license state |

Local candidate verification: 327 Python tests passed (one existing skip), including ten harness test methods with failure subcases; YAML parsing, actionlint 1.7.12, workflow policy, docs-sync and Python compilation passed. Independent privacy/cleanup review found no remaining specific code blocker after fixes. Docker smoke could not fetch the pinned MCR SDK base image (`Forbidden`); that route was stopped. The canonical local verifier passed with owned temporary/config directories and narrowly approved .NET runtime shared-memory writes: build/analyzer stages had zero warnings/errors; 2,819 .NET tests passed, five platform tests skipped, and union line coverage was 82.09%. Its existing `SKIP_ACT=1` path leaves hosted checks pending; Docker/act execution is not claimed. No hooks or gates were bypassed. No Windows or IDE execution has occurred.

No current result is installed-editor acceptance. A mock PASS cannot establish installer flag support, desktop availability or package load. A future PASS applies only to the recorded VSIX SHA and measured hosted host version. The [Windows runner inventory](https://github.com/actions/runner-images/tree/main/images/windows) and image-label mappings can change. The PR syntax lane does not measure an IDE version; the real smoke records its actual installation and image versions rather than assuming either VS 17 or VS 18.

## Manual lane

The candidate [workflow](../../.github/workflows/vsix-ide-smoke.yml) is `workflow_dispatch` only, `contents: read`, one disposable `windows-2025` job, 25-minute limit, no publish/release step. It does not add an IDE install to push/PR CI. It disables checkout credential persistence, uses pinned actions, installs no CLI globally, and retains only the selected JSON evidence for seven days. Cost is one Windows runner per explicit dispatch. Existing build/test/package gates remain unchanged.

A separately reviewed manual run is required for real smoke dispatch. Preparing these files or opening a PR does not dispatch the install/load workflow.

The separate [Windows contracts workflow](../../.github/workflows/vsix-harness-checks.yml) runs only on `pull_request` when the probe, orchestrator, their tests or either workflow changes. It has `contents: read`, no secrets, credential persistence, build, packaging or artifact upload. It names the two standard-library-only harness test modules explicitly, leaving the older packaging/YAML tests in existing CI. Its only commands run mocked Python contracts, parse the probe with Windows PowerShell and compile its embedded C# using `-ValidateOnly`, which returns before ROT access. It does not call the install/load orchestrator entrypoint, VSIXInstaller, MSBuild or the IDE. The manual install/load job additionally requires `github.event_name == 'workflow_dispatch'`; a PR event cannot enter that job. Static workflow boundary tests enforce these separate triggers and entrypoints. The checks execute repository test code on a disposable runner; this is not a sandbox against intentionally malicious changes to that code. Run locally applicable contracts with:

```sh
python3 -m unittest discover -s scripts/tests -p test_vsix_ide_smoke.py
python3 scripts/check-workflow-policy.py
```

Once authorized on the hosted lane, the workflow selects one complete VS installation via `vswhere`, builds with its MSBuild and `DeployExtension=false`, and runs the existing VSIX package gate. It binds the smoke to that instance ID and invokes its own IDE and VSIXInstaller. A private staged copy preserves the built VSIX SHA. The installer capability gate deliberately has no fallback to normal-profile install, MSBuild deployment, ZIP extraction or a different installer.

Each attempt creates a random `DGSmoke` + GUID suffix and checks that the exact local/roaming profile paths did not exist. It installs to that suffix, compares the installed package assembly bytes against the candidate, launches an empty IDE (no solution), and attaches only to that launched PID's version-specific DTE moniker. The canonical Options/automation category remains `DataGuard`; the visible settings label is StackAccord. The probe reads `EnableDetailedLogging` from `General`, quits its owned IDE, and requires a clean IDE exit plus an Information `End package load` entry for the package GUID with no failure HRESULT. Incomplete/unavailable ActivityLog evidence blocks the run.

The host must allow Windows PowerShell, STA COM automation, ROT attachment and normal IDE startup. A modal sign-in, license, first-run, signature or trust prompt is never seeded, clicked, accepted or bypassed. Timeout/nonzero execution returns BLOCKED; security/setup settings are never changed. Experimental profiles isolate configuration; they are **not a security sandbox**. Only repo-owned code runs on the disposable host. Environment guard variables are scope checks, not a security boundary against code already executing on the runner.

## Evidence and teardown

`artifacts/vsix-ide-smoke/evidence.json` contains only selected schema/status/phase/failure identifiers, successful phases, commit, candidate/package/installer/help hashes, measured host/image version, instance ID, random suffix, owned PID and cleanup disposition. Arbitrary process output, exception messages, account names, solution paths, screenshots and raw IDE/installer logs are never uploaded. IDE activity is read in a private temporary directory, reduced to the package success predicate, then removed after the owned IDE stops. Capability help is retained only as a hash.

Cleanup terminates only the exact launched IDE process handle when graceful quit fails. It never kills by process name or enumerates/kills unrelated services. Exact owned profile directories are removed only after IDE exit; cleanup refuses preexisting targets, symlinks/junctions/reparse points, and unexpected parents/names. Raw temporary files are removed afterward. Locked paths, a still-running owned IDE or uncertain ownership force BLOCKED. Registry/service remnants are intentionally left to hosted runner disposal; there is no wildcard registry cleanup or claim of restoring a reusable machine. No code runs on the user's machine.

Exit codes: `0` PASS (all predicates and cleanup), `2` BLOCKED (host/capability/consent/timeout/evidence/cleanup prerequisite), `1` unexpected harness failure (sanitized exception type). Build/package gate failures occur before install and fail the job; missing evidence at that point is not a smoke PASS. Retain successful phase markers together with the terminal phase/reason to locate failure.

## Primary references and limits

- [Microsoft: experimental instance](https://learn.microsoft.com/en-us/visualstudio/extensibility/the-experimental-instance?view=visualstudio) documents IDE `/RootSuffix`; it does not by itself establish VSIXInstaller suffix support.
- [Microsoft: locating Visual Studio](https://learn.microsoft.com/en-us/visualstudio/extensibility/locating-visual-studio?view=visualstudio) and [instance detection tools](https://learn.microsoft.com/en-us/visualstudio/install/tools-for-managing-visual-studio-instances?view=visualstudio) guide instance selection.
- [Microsoft: shipping VS extensions](https://learn.microsoft.com/en-us/visualstudio/extensibility/shipping-visual-studio-extensions?view=visualstudio) covers VSIX deployment. Installer command support must still be verified using the exact host executable's help, not inferred from Visual Studio Setup switches or another installer version.
- [GitHub runner inventory](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md) describes available software, not a guarantee of interactive desktop, license state, consent handling or unattended IDE viability.

If the installed VSIXInstaller cannot target this nondefault profile, this route stops BLOCKED. An alternate install/deployment route requires a separate reviewed design; do not weaken the gate to obtain green CI.
