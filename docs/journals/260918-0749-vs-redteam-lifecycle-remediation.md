---
date: 2026-09-18
title: "VS extension red-team lifecycle remediation"
status: completed
---

# VS extension red-team lifecycle remediation

## Decision

- Failed CLI termination cleanup has one shared 120-second timeout spanning process exit and stream draining. On expiry it closes redirected streams and releases the command reservation—the approved Option A tradeoff accepts a potentially surviving child process rather than permanently locking the extension.
- An `AlreadyExited` process now drains before normal SARIF processing, avoiding a stream-close-then-await race.
- The isolated VS verification script serializes custom CLI-path commands and uses its configured timeout for both expected-message waits.
- Health-host integration tests bind port `0`, parse Kestrel's actual loopback address, and clean up the child even when startup discovery fails.

## Verification

- `dotnet test DataGuard.sln -c Release` with `DOTNET_ROLL_FORWARD=LatestMajor`: 783 passed, 0 failed.
- `tools/verify-vs-cli-launch.ps1`: exit 0; isolated deployment, missing-CLI, temporary-directory, global-tool, and custom-path checks all passed.
- The pre-existing global `DataGuard.Cli` was restored at version `0.2.3-alpha.0.21` after isolated verification.
- Adversarial review found no remaining unaccepted P0/P1/P2 defects.

## Remaining

No documentation contract changes. No commit or push was performed.
