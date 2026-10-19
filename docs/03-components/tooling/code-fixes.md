# Code-fix providers

> Source: `src/DataGuard.CodeFixes/CodeFixProviders.cs` (`netstandard2.0`). Regression sources: `tests/DataGuard.CodeFixes.Tests/CodeFixProviderTests.cs`, `CodeFixRoundTripTests.cs`, and the verifier stubs under `Verification/`.

The five Roslyn providers below register source transformations. Registration, compilation in a fixture, or a diagnostic disappearing does not prove that an edit preserves database behavior. Review the preview and verify the resulting contract before applying broad fixes.

| Provider | Diagnostics | Current behavior |
| --- | --- | --- |
| `DataGuardCodeFixProvider` | DG001 | Adds `[SkipContractCheck]`, a type `[DataContract]`, or parameter `[SqlParameter]` declarations where the syntax supports them. |
| Same provider | DG002 | Replaces a string literal when diagnostic properties pass the metadata checks below. |
| Same provider | DG017 | Regex replacement of `SELECT *`/qualified-star patterns with `ExplicitColumns`, or a TODO comment when that property is absent. |
| `AddMaxLengthAttributeFixProvider` | DG007, DG009 | Adds/replaces a length attribute when metadata contains a digest-shaped string and positive length. |
| `SkipContractCheckFixProvider` | DG001 | Adds the same named-reason skip attribute. |
| `NamingConventionFixProvider` | DG006 | Offers PascalCase rename or a snake_case `[Column]` attribute. |
| `UseOracleCodeFixProvider` | DG012 | Finds the first syntax member invocation named `UseSqlServer` in the enclosing method/statement and changes its name to `UseOracle`. |

## Diagnostic metadata is a trust boundary

DG002 checks `DataGuard.ManifestDigest` for exactly 64 hexadecimal characters and `DataGuard.VerifiedSqlReplacement` for a nonblank string of at most 65,536 characters. DG007/DG009 require the same digest shape and a positive integer `DataGuard.VerifiedMaxLength`.

The provider does **not** load the manifest, recompute its digest, verify a signature, or bind the replacement to a database/catalog itself. The property names express the producer's intended contract; they are not proof of an independent verification step. A trusted diagnostic producer must supply correctly bound metadata. Missing/malformed metadata produces no corresponding action, but a well-formed digest alone does not authenticate a producer or establish SQL correctness. Tests using verifier stubs demonstrate the consumer checks, not an end-to-end trusted verification service.

The SQL replacement is represented as a C# string literal; this preserves that syntactic form, not the semantics of its SQL. The length action replaces existing `MaxLength`/`StringLength` attributes: review loss of other attribute arguments such as minimum length or custom messages.

## Heuristic and manual actions

- **DG001:** `[SkipContractCheck(Reason = "Dynamic SQL - manual review required")]` suppresses checks; it does not repair a contract. Manual declarations record source intent without proving routine identity, database type or direction.
- **DG017:** replacement uses a regex, not a dialect parser. `ExplicitColumns` comes from mapped CLR shape where available, including `[Column]` names; it is not a live schema result. With no columns, the TODO placeholder can leave SQL incomplete. Aliases, joins, quoting and nested SQL need manual review even when a C# fixture compiles.
- **DG006:** rename calls `NameConventions.ToPascalCase` and Roslyn `Renamer.RenameSymbolAsync`; the returned action is a `Document`, not a guarantee that all solution-wide changes are applied. The `[Column]` suggestion computes `ToSnakeCase` from the property name, without looking up the real column or checking for an existing attribute. Review references, mappings and duplicate attributes.
- **DG012:** the provider matches a method name syntactically and preserves its arguments. It does not establish the receiver's provider identity, choose the correct invocation among multiple builders, add an Oracle package, translate connection strings, or validate overload compatibility. This is not an automatic database-provider migration.

No automatic CLOB/NCLOB or general SQL-dialect conversion is registered. Unsupported transformations remain manual; comments or compilable syntax are not proof of remediation.

## Host and batch behavior

All five Roslyn providers return `WellKnownFixAllProviders.BatchFixer`. That API enables a supporting Roslyn host to offer batch actions; it does not prove every editor loads the provider or that a batch is safe. Preview edits and validate after application. These docs do not claim an installed Visual Studio/editor-host test result.

The shipped VS Code extension uses a separate TypeScript `DataGuardQuickFixProvider` in `src/DataGuard.VSCode/src/ui/quick-fix-provider.ts`. Its current remediation command displays manual-review guidance and returns `false`; it applies no automatic source edit. The VS Code light bulb is therefore not evidence that these Roslyn transformations are running. A different C# language-service integration must be verified separately.

## Verification scope

Code-fix fixture and round-trip tests cover advertised actions, selected compilation behavior and refusal when required metadata is absent. They do not exhaust SQL grammar, prove database changes safe, validate all solution-wide references, or demonstrate Windows/VS Code host availability. This documentation reconciliation changes no transformation or test code.
