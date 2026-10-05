# ADR-0002: VS Code extension and Language Server (`src/DataGuard.VSCode`, `src/DataGuard.LanguageServer`)

- **Status:** Proposed
- **Date:** 2026-10-05
- **Measured at:** commit `5603818`
- **Origin:** remediation plan Phase 6.1, red-team report §4, recommendation 22

## Context

`src/DataGuard.VSCode` is a TypeScript extension (`dataguard-vscode` 0.3.0). It runs the `dataguard`
CLI as a child process, and every command carries `--ide-safe` (`src/DataGuard.VSCode/src/command-args.ts:11`, constant
`IDE_SAFE_FLAG`). It refuses untrusted and virtual workspaces (`package.json`, `capabilities`). It
adds diagnostics, code actions, CodeLens, hover, two tree views and a dashboard webview.

`src/DataGuard.LanguageServer` is a 166-line `net9.0` executable (`IsPackable=false`).
`src/DataGuard.VSCode/scripts/package-lsp.cjs` copies it into the extension's `server/` folder together with a SHA-256
manifest, and `extension.ts` (`verifyLanguageServerArtifact`) checks that manifest before it starts
the server. The server only calls `SqlClassifier.Classify` from `DataGuard.SqlClassification`. That
classification is syntax-level, debounced at 150 ms and capped at 1 MiB per message. It publishes
`DGSQL001` / `DG017` and never touches a database or the CLI.

Both parts respect the "light IDE" principle (report §4). The Language Server is close to what the
original goals described for the IDE tier.

## Original goal said

- `research/muc_tieu/1.md:51`: "Không làm VS extension ở giai đoạn đầu — có thể cân nhắc sau này chỉ để
  làm trải nghiệm gõ-code-live (live squiggle) như một lớp bổ sung tùy chọn, không phải kênh phân phối
  chính." The red-team report applies the same argument to VS Code.
- `research/muc_tieu/2.md:8`: "Tầng IDE (nhẹ, chạy live): chỉ đánh dấu "điểm gọi SQL này chưa được
  validate" bằng syntax-level check cực nhanh (không semantic, không DB)". (IDE tier: light, live, only
  a fast syntax-level "this SQL call is not validated" marker, no semantics, no DB.)
- `research/muc_tieu/4.md:17-19`: "Vòng lặp code hàng ngày (IDE, mọi editor — VS/Rider/VS Code) → chỉ
  có một cảnh báo nhẹ, syntax-level ... → không kết nối DB, không làm chậm gõ phím". (Daily loop in
  any editor: one light syntax-level warning, no DB, no typing slowdown.)

## What exists

| Item | Measurement |
|---|---|
| Extension code | 5,478 LOC TypeScript in 32 files: 3,885 production LOC, 1,585 LOC in 15 `*.test.ts` files (95 test cases), plus one 8-line fixture |
| Language Server | 166 LOC, one file (`Program.cs`). No dedicated test project. `SqlClassifier` is covered by `SqlClassifierTests` and `SqlClassifierPropertyTests` in `DataGuard.Core.Tests`. |
| Runtime dependencies | `vscode-languageclient` ^9.0.1 (production); dev: `typescript`, `@vscode/vsce`, `@vscode/test-electron`, `@types/*` |
| Solution membership | Language Server is in `DataGuard.CrossPlatform.slnf`, so `ci.yml` `build-and-test` builds it. |
| CI | `ci.yml` `vscode-extension`: `npm ci`, `npm test` (compile and `node --test`), `npm audit --omit=dev --audit-level=high`. `npm run test:extension-host` is not run by any workflow. |
| Release / publish | `release.yml` `vscode-package`; `build_release.yml` `package-vscode`; `installers.yml` `vscode`; `marketplace.yml` `vscode-package` and `publish-vscode` (uses `VSCE_PAT`). Each runs `prepare-lsp` to bundle the server. |
| Docs | `docs/03-components/tooling/vscode-extension.md` (+ `.vi.md`) |

## Risks it adds

- **Second toolchain:** Node 22 (`.nvmrc`), npm, TypeScript and `vsce`. `CLAUDE.md` change rule 3
  forbids a second runtime or toolchain without an explicit product requirement. This ADR is where the
  owner records that requirement, or declines it.
- **Supply chain:** npm dependencies and `overrides` (`fast-uri`, `js-yaml`, `qs`). They are covered by
  `npm audit` in CI and by the npm part of the licence allow-list in `ci.yml`. The publish job holds a
  Marketplace PAT.
- **Duplication:** the Language Server's `DGSQL001` overlaps with `DG001`, which the NuGet analyzer
  (`UnvalidatedSqlCallGenerator`, `src/DataGuard.Analyzers/Analyzers.cs:254`) already reports in any
  editor that loads Roslyn analyzers, VS Code with C# Dev Kit included. Both call
  `DataGuard.SqlClassification`, so the SQL detection is shared, but a user may see two diagnostics
  with different IDs for the same call.
- **Runtime requirement:** the Language Server needs a .NET runtime on the developer machine.
- **Coverage gap:** the extension-host (UI) test exists but no workflow runs it.

## Options

1. **Keep:** first-class channel, with ongoing UI feature work.
2. **Freeze:** keep building, testing and publishing. Fixes and security only, no new UI features. The
   Language Server stays as the light IDE tier.
3. **Extract:** move the extension (and the Language Server, or a copy of it) to a separate
   repository that consumes released CLI artifacts.
4. **Remove:** not available in this plan.

## Recommendation

**`freeze`**. The Language Server part is in scope and may keep receiving fixes that keep it aligned
with the analyzer.

- The Language Server is the tier that `2.md:8` and `4.md:17-19` describe: syntax-level, live, no DB.
  It is the part of this component that the goals asked for.
- The extension UI (dashboard, trees, CodeLens, hover) goes beyond "one light warning". It is the
  largest TypeScript surface and the reason for the second toolchain. It already has CI coverage and
  95 tests, so freezing costs little and extracting would duplicate the publishing pipeline.
- Freeze until the Phase 6.3 end-to-end gate is green. Then the owner decides whether VS Code is a
  product channel (`keep`) or a convenience (`extract`).

## Owner decision

`pending`. (Fill in: decision, date, name.)

## Consequences

- No new UI features. Results come from the CLI's SARIF output, so improvements land in the CLI.
- Follow-up candidates (each needs its own change): run `test:extension-host` in `ci.yml`, and decide
  whether the Language Server should stay quiet when the NuGet analyzer is loaded, to avoid duplicate
  diagnostics.

## Tóm tắt (VI)

Extension VS Code (5.478 LOC TypeScript, trong đó 1.585 LOC là 95 test) và Language Server (166 LOC)
nằm ngoài kênh chính mà 1.md đề ra. Cả hai gọi CLI qua `--ide-safe` hoặc chỉ phân loại SQL ở mức cú
pháp, nên không vi phạm nguyên tắc "IDE nhẹ". Riêng Language Server chính là tầng IDE mà 2.md và 4.md mô
tả: cảnh báo nhẹ, không semantic, không DB. Rủi ro chính là toolchain thứ hai (Node/npm), chuỗi cung
ứng npm, PAT Marketplace, và chẩn đoán trùng với analyzer NuGet. Khuyến nghị: **freeze** phần UI của
extension (không thêm tính năng, chỉ sửa lỗi và bảo mật), còn Language Server là phần đúng phạm vi.
Quyết định của owner: pending.
