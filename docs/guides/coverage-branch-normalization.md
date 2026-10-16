# Coverage branch normalization

`scripts/coverage_manifest.py` accepts Coverlet boolean casing and XML boolean
values (`true`/`false`, `1`/`0`), validates condition counts, and emits canonical
lowercase flags. Contradictory flags, missing branch counts and invalid percentages
fail validation before an upload list is emitted. Coverlet truncates percentages
to two decimal places; exact integer counts determine rates.

`reports[].branches` counts represented, retained class source-line records once.
Method copies are validated and receive consistent summaries, but are not counted
again. Generated `/obj/` classes remain excluded under the existing policy.
`reports[].input_branches` preserves input root counts (or null if absent), and
`sha256` identifies the untouched input. On a second normalization, input counts
refer to that normalized input, not the original collector report.

Coverlet root totals can include branches without emitted line records. Those
branches cannot be assigned invented source locations. Therefore normalized
branch rates describe a narrower, explicitly labeled scope than raw collector
rates. Preserve both reports; do not compare their percentages as a coverage gain,
or sum overlapping suites into an aggregate branch metric. Line union, source
inventory, exclusions and the existing line gate are unchanged.

The fixture `scripts/tests/fixtures/coverlet-10.1.0-branches.xml` retains actual
collector line elements inside a labeled synthetic wrapper. Tests cover casing,
condition counts, malformed inputs, generated exclusions and XML round trips.

See [the evidence and acceptance plan](../../plans/2026-10-15-branch-normalization.md).
