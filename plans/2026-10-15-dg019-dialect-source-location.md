# DG019 dialect context and source provenance — 2026-10-15

Base: main `63243ec` (PR59). Branch: `fix/dg019-dialect-source-location`. PR60 branch is untouched. Status: local verification complete; hosted CI pending; draft PR only, no merge/release.

## Evidence and scope

.NET SDK 9.0.319, MSBuild 17.14.60 and C# compiler 4.14.0 verified before production edits. Linux environment; Visual Studio 18.10.2 is the user's reported IDE, not available here. Read AGENTS.md, git/workspace rules, rationalization and IDE phase plans; fetched current main. Existing hooks remain enabled and unchanged.

Only synthetic identifiers/fixtures are used. The user's configured provider remains unconfirmed. The Error List screenshot distinguishes SQL-relative message coordinates from separate C# source lines; it does not show the editor/caret and therefore cannot prove a navigation defect.

Trace: ProjectCSharpSqlSource recognizes ExecuteNonQuery by name, resolves a local initializer, substitutes interpolation/nullable-int concatenation values, and anchors the descriptor at the invocation. A custom DataContext yields no provider hint. CLI provider precedence is flag → DefaultProvider → sqlserver. ProviderRuleCatalog injects ScriptDOM only for sqlserver. ScriptDOM rejects the synthetic Oracle UPDATE/DELETE target aliases. Oracle configuration has no grammar parser; no Oracle syntax validation can honestly be claimed. The SQL Server adapter's standalone RawSqlParser is a separate acquisition source; it is not used by this C# project extraction path.

DG019 used the descriptor's invocation Location regardless of parser position. Its message omitted the dialect and did not label SQL-relative coordinates. Placeholder normalization could additionally shift columns. DiagnosticEmitter converts Roslyn Location to one-based SARIF; SarifErrorListPublisher retains those coordinates and ErrorListPresenter converts once to zero-based ErrorTask/buffer coordinates. DG019 is not part of the standalone Roslyn analyzer inventory. PR59's conversion remains unchanged.

Oracle alias support is documented by Oracle 19c [UPDATE](https://docs.oracle.com/en/database/oracle/oracle-database/19/sqlrf/UPDATE.html) and [DELETE](https://docs.oracle.com/en/database/oracle/oracle-database/19/sqlrf/DELETE.html). Supported options: configure the confirmed provider and retain honest unevaluated grammar status; separately integrate and verify an Oracle grammar; or separately authorize live validation. Regex exceptions and Oracle-to-T-SQL rewriting are rejected. This change implements only diagnostic context, source provenance, normalization-offset correction and honest missing-grammar status.

## Requirement / code / test / evidence

| Requirement | Code | Test / evidence |
|---|---|---|
| Reproduce custom wrapper, aliases, interpolation and nullable-int concat privately | ProjectCSharpSqlSource | Dg019SourceLocationTests; baseline archive of main: 4 failing assertions (invocation offset rather than alias offset), no skips |
| Name selected grammar; distinguish SQL vs C# coordinates | RawSqlParseStatusRule | RawSqlParseStatusRuleTests and source-location tests |
| Preserve provider dispatch and real invalid SQL errors | ProviderRuleCatalog unchanged; RawSqlParseStatusRule | Oracle routes without T-SQL, valid T-SQL alias forms, genuine malformed UPDATE/DELETE |
| Unsupported grammar is not a pass | RawSqlParseStatusRule.MarkUnevaluated | MissingGrammar_IsExplicitlyUnevaluatedEvenForInvalidSql |
| Map source after extraction and placeholders | SqlSourceSegment; ProjectCSharpSqlSource.SourceMap; TSqlStatementParser; TSqlPhantomAnalyzer | Leading newline, LF/CRLF, post-interpolation/concat, escaped-token fallback, normalized ?/{0}/:id offsets; one-based SARIF assertions |
| Preserve safety/trust | No database or policy changes | Offline fixtures; 2,051 non-LiveDb tests passed, 5 Windows junction tests skipped |
| Independent review | Read-only independent reviewer | Requested post-hole/normalization/fallback tests and honest EOF/IDE scope; incorporated |
| EN/VI guidance | docs/guides/dg019-dialect-and-locations(.vi).md | verify_docs_sync passed |

Initial compile/style errors were fixed before behavioral tests; they are not counted as reproduction. Focused behavior coverage: 32 cases included in the passing full regression, zero failures; five unrelated Windows-only cases skipped separately. Logs are temporary local evidence under /tmp/dg019-*.log, not repository artifacts.

## Limitations and release gates

No comprehensive Oracle parser, no confirmed resolution of the user's provider configuration, and no manual Visual Studio navigation result. Source mapping is exact only for unchanged token text. Generated values and escaped/raw literals map to containing expressions/tokens; missing provenance or EOF uses acquisition location. Existing initializer/data-flow resolution is unchanged. No database was contacted. Full regression, hooks, hosted CI and final draft PR evidence will be recorded after execution; skipped/manual tests must remain distinct from passes.

## Verification evidence

- `dotnet build DataGuard.sln --configuration Release`: succeeded, zero warnings/errors, including compile-only VisualStudio and VisualStudio.Tests projects. No Windows test execution or VS UI claim.
- Canonical unchanged local gate (`TMPDIR=/var/tmp SKIP_ACT=1 ./scripts/verify_local_gates.sh`): Release builds, formatting, actionlint 1.7.12, docs inventory and coverage tests passed; full dependency audit passed. Coverage 71.01% (14,805 / 20,850 lines).
- Full non-LiveDb suite: Core 1,816 passed / 5 skipped; Analyzers 98; CodeFixes 38; GoldenCorpus 61; Observability 38. Total 2,051 passed, 0 failed, 5 skipped.
- VS Code `npm test`: 101 passed, 0 failed/skipped.
- Environment issue isolated without weakening tests: `/tmp/.git` changes fixture repository-root discovery and caused four existing path assertions to fail. `TMPDIR=/var/tmp` makes the unchanged extractor tests pass (38/38) and the full suite pass. An initial actionlint 1.7.7 lacked the repository's macos-15-intel runner label; current 1.7.12 passes unchanged workflows.
- Two existing Oracle CLI tests now assert both DG019 grammar-unavailable and DG020 describe-unavailable entries. Their deliberately unreachable local connection fixture is pre-existing; no production database or real secret was used.
- Independent reviewer found no blocking code defects after re-review. Requested post-hole/normalization/fallback/EOF tests, provider-hint status assertions, and EN/VI exit-code guidance were incorporated.
- `SKIP_ACT=1` is the existing pre-push default: local Docker/act and heavy historical secret scan are not claimed as run. LiveDb tests and manual Visual Studio 18.10.2 navigation are not claimed as passed.
