---
phase: 2
title: "SPDX expression semantics (OR/AND/WITH), fail closed — TDD"
status: completed
priority: P2
effort: "2h"
dependencies: []
---

# Phase 2: SPDX expression semantics

## Overview
`spdx_allowed` (`scripts/check-nuget-licences.py:59-62`) strips the operators and requires **every** token to be allowed. So `MIT OR GPL-3.0-or-later` (jszip, already in the dev tree) fails even though the licensee may pick MIT. That is a second recurrence vector. Replace it with a small recursive-descent evaluator that applies SPDX precedence and fails closed on malformed input.

Conditional: run this phase only if validation Q1 = yes. If Q1 = no, delete this phase file and drop gates J2/A2/J3.

## Requirements
- Functional (SPDX spec Annex D): `WITH` binds tightest, then `AND`, then `OR`. Parentheses group.
  - `A OR B` → allowed if A or B is allowed.
  - `A AND B` → allowed only if both are.
  - `id WITH exc` → allowed only if both `id` and `exc` are in `[spdx]`. This keeps today's strictness; exceptions are never implied.
  - Empty, unbalanced parentheses, dangling or doubled operators, unknown tokens → `False`.
- Non-functional: stdlib only, no new dependency. Operators stay case-insensitive (today: `t.upper() in SPDX_OPERATORS`). Id matching stays case-sensitive. Signature `spdx_allowed(expression: str, spdx: set[str]) -> bool` stays unchanged. The only caller is `evaluate` (`:157`); confirm with `xd://lsp` references before editing.

## Tests first (RED) — `scripts/tests/test_check_nuget_licences.py`, class `SpdxExpressionTests` (`:63`)
Keep the existing assertions at `:66-69` unchanged. They stay true under the new semantics. Add one new test method per behaviour, each asserting a consumer-visible outcome:
1. `test_or_passes_when_one_alternative_is_allowed`: `spdx_allowed("MIT OR GPL-3.0-or-later", {"MIT"})` is True; `"(GPL-3.0-or-later OR MIT)"` is True.
2. `test_or_fails_when_no_alternative_is_allowed`: `"GPL-3.0-only OR SSPL-1.0"` with `{"MIT"}` is False.
3. `test_and_binds_tighter_than_or`: `"MIT OR Foo AND Bar"` with `{"MIT"}` is True (parsed as `MIT OR (Foo AND Bar)`); `"(MIT OR Foo) AND Bar"` with `{"MIT"}` is False; `"MIT AND Zlib"` with `{"MIT"}` is False.
4. `test_with_requires_licence_and_exception`: `"Apache-2.0 WITH LLVM-exception"` is False with `{"Apache-2.0"}` and True with `{"Apache-2.0","LLVM-exception"}`.
5. `test_malformed_expressions_fail_closed`: `"(MIT"`, `"MIT)"`, `"MIT OR"`, `"OR MIT"`, `"MIT OR OR Apache-2.0"`, `"MIT Apache-2.0"`, `"()"`, `"WITH"`, `"MIT OR )"`, `"MIT AND ("`, `"MIT WITH )"` are all False with `{"MIT","Apache-2.0"}`.
6. `EvaluateTests` addition: an npm row `("npm","jszip","3.10.1","expression","(MIT OR GPL-3.0-or-later)")` produces no offender with the `{"MIT","Apache-2.0"}` set.

RED: `python3 -m unittest scripts.tests.test_check_nuget_licences -v` (or `discover -s scripts/tests -p 'test_check_nuget_licences.py'`). Tests 1, 3 (first assertion) and 6 fail. Tests 2, 4 and 5 may already pass: they guard the rewrite against over-acceptance and are required to stay green.

## Implementation Steps
1. `xd://lsp` references on `spdx_allowed` → expect only `evaluate` plus the tests.
2. Write the RED tests above. Run them and record the failures.
3. Rewrite `spdx_allowed`:
   - Tokenise: pad `(`/`)` with spaces, then `split()`.
   - `parse_or` → `parse_and` (`OR` loop) → `parse_with` (`AND` loop) → `atom` (`(` expr `)` | id [`WITH` id]).
   - Lexing / parsing rules: an `id` must be a non-operator token and cannot be `(` or `)`.
   - Recursion depth bound: pass `depth: int = 0`; if `depth > 10`, raise `_SpdxSyntaxError` to fail closed on adversarial recursion.
   - Any leftover token, missing `)`, or operator/parenthesis where an id is expected raises `_SpdxSyntaxError`. Catch it and return `False`.
   - Update the docstring from "every id must be allowed" to the precedence rule. Update the module docstring line 11 ("every SPDX token is in the `[spdx]` section") to match.
4. Update the `allowed-licences.txt` header line 3-4 wording ("every id in its licence expression is listed") to "the expression is satisfied by listed ids (OR = any alternative, AND/WITH = all)".
5. GREEN: the full `scripts/tests` suite. The count must be baseline 92 + the newly added methods, with 0 failures.
6. Judge J2 (`risk_security_triage` on the diff), then advisor A2, then J3.

## Success Criteria
- [x] RED output recorded, showing the OR cases failing on the old implementation.
- [x] GREEN: `Ran 92+N tests … OK`. The old assertions `:66-69` are unchanged.
- [x] Real gate on the `main` lockfile and on `/tmp/pr49-package-lock.json` → still OK (no accidental new offenders, no silent passes).
- [x] Fail-closed proof: run the CLI with a temp lockfile whose single prod package has `license: "MIT OR"`. Assert `exit 1`, stdout contains `licence gate: checked`, stderr contains `- npm <name> <ver>: [expression] MIT OR`, and neither stdout nor stderr contains `ERROR:`.

## Risk Assessment
- Over-acceptance (most severe): a parser bug turns AND into OR → test 3 (AND cases), test 4 and test 5 guard against it. J2 is a dedicated security triage of this point.
- `GPL-2.0+` or `LicenseRef-*` tokens: treated as plain ids (exact match). Behaviour unchanged.
- OR legally means "we redistribute under the chosen alternative". Allowed alternatives are permissive, so the notices obligation is the allowed licence's. No NOTICES change is needed unless such a package becomes production; the gate output is the trigger to review it then.
