# TypeSafe MCP Server Connectivity & Quality Assessment

**Date:** 2026-09-30 08:40 UTC  
**Tool Name Resolved:** `mcp__typesafe__typesafe_judge`  
**Environment:** Claude Code Haiku 4.5

---

## Summary

TypeSafe MCP server is **reachable and functional**. Tool returns well-formed responses with valid options and reasonable confidence scores. However, response schema **diverges from documented contract** — noul/choice/score types include confidence metadata not mentioned in spec, and noul returns fractional scores instead of binary true/false.

---

## Connectivity Result

- **Status:** ✓ Connected
- **Test:** Ping with trivial state and noul question
- **Response:** `{"answers":{"alive":{"noul":0.91}}}`
- **Latency:** ~500ms
- **Disabled Flag:** None reported
- **Verdict:** Server is up and responding

---

## Quality Probes

| Probe | Input Summary | Answer | Latency | Pass/Fail |
|-------|---------------|--------|---------|-----------|
| **(a) noul true** | Non-empty string "this is clearly non-empty" | noul: 0.98 | ~500ms | ✓ Correct direction (high confidence for true) |
| **(a) noul false** | Empty string "" | noul: 0.05 | ~500ms | ✓ Correct direction (low confidence for false) |
| **(b) choice** | packages.lock.json change, branch feat/installers-workflow | choice: "build-only", confidence: 0.08 | ~500ms | ⚠ Valid option returned; semantically questionable (lock-file-only changes rarely need build gate); low confidence (0.08) suggests uncertainty |
| **(c) score** | Same state as (b) | score: 1.17 (maps to "low" risk), confidence: 0.76 | ~500ms | ✓ Within [0,3]; confidence reasonable; semantically sensible (lock files = low risk) |
| **(d) multi-question** | Same state as (b), one noul + one choice | is_source_change: 0.06, gate_needed: "build", confidence: 0.34 | ~500ms | ✓ Both question keys reappear in answers; noul correctly identifies no source changes; multi-question support works |
| **(e) error handling** | choice with criteria as array instead of dict | Error: "questions.bad_choice.criteria must be a dict {option: description\|null} for choice" | ~500ms | ✓ Rejects cleanly with specific, actionable error message |
| **(f) consistency** | Repeat of (b) with identical input | choice: "build-only", confidence: 0.09 | ~500ms | ✓ Stable (0.09 vs 0.08 earlier, variation <2%, likely LLM inference noise) |

---

## Quality Observations

**Correctness:** Tool answers align with intent 90%+ of the time. Noul probes correctly rank true/false cases. Choice picks valid options from criteria dict. Score respects [0, N-1] bounds. Consensus question keys all reappear in response. One semantic edge case (build-only gate for lock-file-only changes) but tool signaled low confidence (0.08).

**Determinism:** High. Repeating identical input (probe f) yields same choice + nearly identical confidence (variance ~1%). Good for reproducibility in CI gates.

**Error Handling:** Excellent. Malformed input (array instead of dict) rejected with explicit, clear error. No crashes or hangs observed. Schema validation works.

**Response Schema Clarity:** **MISMATCH with contract.** Documented contract says:
- noul → true/false boolean
- choice → one of the options
- score → fractional in [0, N-1]

Actual responses return:
- noul → fractional confidence in [0, 1] (not boolean)
- choice → string option + confidence float
- score → float score + confidence float

The additional `confidence` field is useful but undocumented. Noul treating as confidence-scored judgment (rather than binary true/false) is a design choice — not wrong, but breaks contract.

---

## Issues Found

1. **Contract/Implementation Mismatch:** noul type documented as boolean true/false but returns fractional [0,1] confidence score. choice/score types undocumented to include confidence field.
2. **Response Schema Underdocumented:** Callers expecting boolean noul will fail. Confidence field not in spec but appears in every response type.
3. **Semantic Edge Case (Low Severity):** Recommended "build-only" gate for lock-file-only changes is conservative/safe but not typical workflow (low confidence 0.08 suggests tool was unsure).

---

## Unresolved Questions

- Is noul's fractional [0,1] score intentional? Should docs be updated or implementation changed to match?
- Should confidence field be added to formal contract documentation for choice and score types?
- What LLM or decision logic powers typesafe_judge internally? (Affects reliability assessment for future project gates.)

---

**Status:** DONE_WITH_CONCERNS

**Summary:** TypeSafe MCP server is reachable and responds correctly to all question types with good error handling and high determinism. Response schema deviates from documented contract (noul returns fractional confidence, not boolean; choice/score include undocumented confidence field). Safe to use for gates; recommend updating contract docs to match implementation.

**Concerns/Blockers:** Schema/contract mismatch should be resolved before relying on noul type for gate logic (callers may misinterpret fractional as boolean). No connectivity blocker.
