# Support

## How to get help

| Channel | Use for |
|---|---|
| [GitHub Discussions](https://github.com/thanhnt-sm/eco_support_net_oracle/discussions) | Questions, usage help, "how do I…" |
| [Bug report](https://github.com/thanhnt-sm/eco_support_net_oracle/issues/new?template=bug_report.yml) | Reproducible defects (CLI, rules, analyzers, extensions) |
| [Feature request](https://github.com/thanhnt-sm/eco_support_net_oracle/issues/new?template=feature_request.yml) | New rules, providers, workflow improvements |
| [SECURITY.md](SECURITY.md) | Vulnerabilities — **never** open a public issue for these |

## Before filing a bug

1. Run `dataguard version` and include the output.
2. Check the [exit codes table](README.md#exit-codes) — exit 1/2 may be the
   documented contract, not a bug.
3. Redact all connection strings and credentials from anything you post.

## Commercial / enterprise support

This repository is MIT-licensed open source. Enterprise and banking
deployments should read `docs/marketplace-publishing.md` and the banking
profile notes in `plans/2026-08-21-review-handoff.md` (offline-first, zero
telemetry, credential zero-trust).
