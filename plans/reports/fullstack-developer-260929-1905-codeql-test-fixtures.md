# CodeQL test-fixture fixes for PR #24 (12 alerts)

Branch `feat/vs-extension-hardening` @ 8a2df13. Ownership honoured: only
`tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs` and
`tests/DataGuard.Core.Tests/PhantomIdentifierRuleRegexTests.cs` changed. No commit.
Queries, `.github/codeql/`, and `packages.lock.json` untouched.

## Query shapes (`.github/codeql/queries/`)

Both queries use `regexpMatch`, which in CodeQL is an **anchored full-string match**.

- `hardcoded-connection-strings.ql` (`dataguard/hardcoded-connection-string`): `StringLiteral sl` where
  `sl.getValue().regexpMatch("(?i)(server|data source|initial catalog|password|pwd|user id)=[^;]+")`
  minus `regexpMatch("(?i)localhost|(localdb)")` minus `regexpMatch("(?i)(password|pwd|user id)=\*\*\*")`.
  Effective shape: a literal that is **entirely** one `key=value` segment with no `;`.
  That is why `"Server=x"`, `"Server=env"`, `"Data Source=from-config"` fired while the neighbouring
  `"Data Source=attacker.example;Integrated Security=true"` (line 34) and
  `"Server=127.0.0.1,1;Connect Timeout=1"` (line 133) did not.
- `sql-injection-patterns.ql` (`dataguard/sql-injection-pattern`): `AddExpr ae` where one operand is a
  `StringLiteral` whose whole value matches `(?i)select|insert|update|delete|exec`.
  Effective shape: `"SELECT" + ...` with a **bare** keyword literal. Line 81's
  `"SELECT Id FROM Users WHERE Name IN ('" + ...` is not matched (literal is not exactly a keyword).

## Convention found in existing tests

`CredentialManagerFullTests.cs` (lines 66-92, 114-161, 232-287) uses credential-free two-segment strings
`"Server=env;Database=Db"` / `"Server=cfg;Database=Db"` / `"Server=x;Database=Db"` for env-vs-config
comparisons; `AuditAndConfigTests`, `AutoDetectionEngineTests`, `ConnectionDiscoveryTests`,
`LiveQuerySchemaProviderTests` all use multi-segment `;` strings. No shared builder/helper exists
(`TestConnectionStrings`, `WithPassword` etc. absent), so none was invented; adopted the two-segment
const convention instead.

## Per-alert change

`IdeSafePolicyTests.cs` — two class consts added (with comment):
`EnvConnection = "Server=env;Database=Db"`, `ConfigConnection = "Server=cfg;Database=Db"`.

| old line | old literal | new |
|---|---|---|
| 80 | `[InlineData("Server=x", ...)]` | `[InlineData(EnvConnection, ...)]` |
| 135 | `envValue : "Data Source=from-config"` | `envValue : ConfigConnection` |
| 171 | `ConnectionString: "Data Source=from-config"` | `ConnectionString: ConfigConnection` |
| 173, 175 | `environmentConnection: "Server=env"` / `.Be("Server=env")` | `EnvConnection` |
| 182, 184 | `ConnectionString: "Server=env"` / `environmentConnection: "Server=env"` | `EnvConnection` |
| 194 | `ConnectionString: "Server=env"` | `EnvConnection` |
| 206 | `ConnectionString: "Data Source=from-config"` | `ConfigConnection` |
| 251, 252 | `ConnectionString: "Server=env"` / `environmentConnection: "Server=env"` | `EnvConnection` |

Assertion intent preserved: env-vs-config tests (168-177, 204-213) still use two distinguishable values;
tests where merged config already equals the env value (180-201, 249-261) stay equal. Line 133's
`"Server=127.0.0.1,1;Connect Timeout=1"` left as-is (unflagged, intentional quick-fail address).

`PhantomIdentifierRuleRegexTests.cs:40` —
`"SELECT" + new string(' ', 200_000) + "x"` → `string.Concat("SELECT", new string(' ', 200_000), "x")`,
plus a one-line comment that it is a hostile regex payload, not SQL construction. 200 000-space payload,
5 s bound and empty-violations assertion unchanged. (Comment placed as first statement in the block to
satisfy StyleCop SA1515.)

## Verification

- CodeQL CLI not installed locally (`codeql: command not found`); authoritative verification is CI.
- Static proxy of both query shapes over the two files (anchored regex on every string literal; bare
  keyword literal adjacent to `+`): **0 matches** post-change.
- `dotnet test tests/DataGuard.Core.Tests -c Release --filter "FullyQualifiedName~IdeSafePolicy|FullyQualifiedName~PhantomIdentifierRuleRegex"`:
  **Passed 57 / Failed 0 / Skipped 0**.
- `dotnet build DataGuard.sln -c Release -m:1`: **0 Warning(s), 0 Error(s)** (first attempt hit a transient
  CS2012 file lock on `DataGuard.VisualStudio.Tests.dll` from a concurrent agent's build; retry clean).
- `dotnet format whitespace DataGuard.sln --verify-no-changes`: exit 0.
- Line endings: both files `ASCII text` (LF), `git diff --check` clean.
- `packages.lock.json` git status identical before/after my builds (9 pre-existing `M` entries, not touched).

## Concerns / findings for the query owner (not acted on — out of scope)

1. `AuditAndConfigTests.cs:361` `ConnectionString: "Server=db"` full-matches the same query; pre-existing,
   outside ownership, will surface if that line ever enters a PR diff.
2. Query quality: because every `regexpMatch` is anchored, the `localhost`/`(localdb)` and
   `Password=***` exclusions are dead (they only match a literal that is *entirely* `localhost` etc.),
   and real credentialed strings like `"Server=x;Password=y"` are never flagged while credential-free
   `"Server=env"` is. Likely intended `regexpFind`/`.*...*` wrapping. Same for `sql-injection-patterns.ql`:
   only a bare-keyword literal operand is matched. Left untouched per instruction.

**Status:** DONE
**Summary:** All 12 alert sites rewritten to the established two-segment connection-string convention and `string.Concat`; targeted tests 57/57 green, solution build 0 warnings, format clean, LF preserved.
**Concerns/Blockers:** No local CodeQL CLI, so zero-result proof is by static proxy + CI. Two out-of-scope query-quality findings above.
