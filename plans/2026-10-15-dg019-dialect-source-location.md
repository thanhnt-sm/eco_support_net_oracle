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

No comprehensive Oracle parser and no manual Visual Studio navigation result. The supplied configuration selects sqlserver; the application database remains unconfirmed. Source mapping is exact only for unchanged token text. Generated values and escaped/raw literals map to containing expressions/tokens; missing provenance or EOF uses acquisition location. Existing initializer/data-flow resolution is unchanged. No database was contacted. Full regression, hooks, hosted CI and final draft PR evidence will be recorded after execution; skipped/manual tests must remain distinct from passes.

## Verification evidence

- `dotnet build DataGuard.sln --configuration Release`: succeeded, zero warnings/errors, including compile-only VisualStudio and VisualStudio.Tests projects. No Windows test execution or VS UI claim.
- Canonical unchanged local gate (`TMPDIR=/var/tmp SKIP_ACT=1 ./scripts/verify_local_gates.sh`): Release builds, formatting, actionlint 1.7.12, docs inventory and coverage tests passed; full dependency audit passed. Coverage 71.01% (14,805 / 20,850 lines).
- Full non-LiveDb suite: Core 1,816 passed / 5 skipped; Analyzers 98; CodeFixes 38; GoldenCorpus 61; Observability 38. Total 2,051 passed, 0 failed, 5 skipped.
- VS Code `npm test`: 101 passed, 0 failed/skipped.
- Environment issue isolated without weakening tests: `/tmp/.git` changes fixture repository-root discovery and caused four existing path assertions to fail. `TMPDIR=/var/tmp` makes the unchanged extractor tests pass (38/38) and the full suite pass. An initial actionlint 1.7.7 lacked the repository's macos-15-intel runner label; current 1.7.12 passes unchanged workflows.
- Two existing Oracle CLI tests now assert both DG019 grammar-unavailable and DG020 describe-unavailable entries. Their deliberately unreachable local connection fixture is pre-existing; no production database or real secret was used.
- Independent reviewer found no blocking code defects after re-review. Requested post-hole/normalization/fallback/EOF tests, provider-hint status assertions, and EN/VI exit-code guidance were incorporated.
- `SKIP_ACT=1` is the existing pre-push default: local Docker/act and heavy historical secret scan are not claimed as run. LiveDb tests and manual Visual Studio 18.10.2 navigation are not claimed as passed.

## Configuration follow-up: concrete user evidence

The supplied sanitized settings include `Oracle: true`, `SqlServer: false`, `DefaultProvider: sqlserver`, Snapshot mode and AutoDetectProvider true. Runtime reproduction before changes: `config validate` exited 0 and said valid; `config show` returned Oracle/SqlServer null while retaining sqlserver. The configured selection is now established; the actual application's database still requires confirmation. Typed binding rejected booleans where mappings were expected, then the fallback silently dropped those blocks and could lose other nested settings/lists. This explains selection of T-SQL without assuming an Oracle parser exists.

Visual Studio LiveProvider applies only to live Refresh Snapshot/Verify Shapes. Normal validation passes config/ide-safe but no provider override. A custom CLI path wins over the bundled binary; a newer VSIX alone cannot establish which CLI executed. No user path or project name is retained in fixtures/docs. No evidence shows current CLI/VS generators producing the malformed boolean shape.

| Requirement | Change | Test / evidence |
|---|---|---|
| Reject the reported invalid shape without dropping settings | ConfigLoader removes lossy fallback, keyed mapping errors | ConfigurationGuidanceTests full sanitized settings; four pre-fix behavior failures in /tmp/dg019-config-red.log |
| Surface provider errors and conflicts | ConfigCommands validates and prints effective provider, warns unused blocks | invalid provider/shape exit 2; valid conflicting block warning; explicit flag still wins |
| Comment generated/saved YAML | Shared Core ConfigurationYamlWriter for init and wizard | typed roundtrip, wizard output, init output equals documented full sample |
| Preserve user annotations | init refuses existing output; reads never rewrite config | init overwrite refusal leaves handwritten comments byte-for-byte intact |
| Explain real semantics honestly | EN/VI configuration guides, complete/minimal Oracle examples, USAGE and README links | docs examples parsed by production loader; Oracle syntax marked unevaluated; no credentials required |
| Separate VS live options from validation | Existing command builder behavior retained | VS argument test pins absence of provider/connection-env; CustomCliPath precedence inspected |

Focused configuration/binding/wizard suite passed 63 tests before the final conflict-warning assertion and additional coverage cases were added. Final local gates passed: 2,073 .NET tests, zero failures, five Windows-only skips; coverage 71.18% (14,848/20,860). Final-head hosted checks are pending. A brief executor disconnection rejected one command before execution; connectivity recovered, source/logs remained intact, and tests resumed afterward. Earlier hosted results apply only to initial head 6f711cd, not to this follow-up until pushed and rerun.


### Final coverage review

The first-head Codecov report (83.48624% patch coverage, 18 uncovered lines) was reviewed as a coverage lead, not a failed CI gate. Added meaningful cases for constant interpolation, parenthesized SQL, source-segment boundaries and missing source trees, and quoted/bracketed/comment placeholders before an actual syntax error. Existing tests cover escaped/raw-token and EOF acquisition fallbacks, malformed configuration, explicit provider precedence, and generated comments with multiline/quoted scalar roundtrips. ScriptDOM's defensive no-fragment-without-error branch is not forced through a production test seam solely to raise coverage. No claim of complete branch coverage is made.

Configuration follow-up full gates passed before these final coverage tests: 2,066 .NET tests passed, five Windows-only junction cases skipped, coverage 71.15% (14,842/20,860). Final additional cases and unchanged pre-push gates are recorded in the PR with final-head CI. Initial-head CI, Marketplace Extensions and Standards audit all succeeded; those results are not substituted for final-head verification.

Final coverage cases passed in the full canonical suite (/tmp/dg019-final-gates.log). Pre-commit/pre-push hooks remain unchanged; final-head hosted evidence is maintained in draft PR61.
