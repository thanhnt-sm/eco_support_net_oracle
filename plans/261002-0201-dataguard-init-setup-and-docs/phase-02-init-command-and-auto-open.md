# Phase 2: Init Command and Auto-Open

## Overview
Implement the "Initialize DataGuard" command and heavily upgrade the UX of the generated file. It delegates to the `dataguard init` CLI command to generate a **Minimal** `.dataguard.yml` file, leveraging YAML Schema for IntelliSense. After successful generation, the extension automatically opens the file in the text editor so the user can immediately fill in the highlighted connection string.

## Requirements
- **Functional**:
  - New VS command `Tools > DataGuard > Initialize DataGuard`.
  - Command invokes `RunCliAsync("init")`.
  - Auto-open `.dataguard.yml` in the editor upon success (exit code 0).
  - **[Red-Team UX]** Modify CLI `init` logic to generate a *Minimal* YAML instead of a full dump.
  - **[Red-Team UX]** Ensure the generated YAML includes `# yaml-language-server: $schema=...` for Visual Studio IntelliSense.
  - **[Red-Team UX]** Place the Connection String block at the very top with a large `TODO` comment and a commented-out boilerplate example.
- **Non-functional**:
  - Prevent UI thread blocking.
  - Handle failure cases (e.g., CLI not found, permission errors) gracefully.

## Architecture
- Register command in `Menus.vsct`.
- Add execution handler in `DataGuardPackage.Commands.cs`.
- Use `VsShellUtilities.OpenDocument` to display the file.

## Tests (TDD)
1. Write `InitCommandTests.cs` (mocking process execution and VS shell).
2. Test: CLI succeeds -> File is opened.
3. Test: CLI fails -> File is not opened, error logged.

## Implementation Steps
1. Add `InitCommand` to `src/DataGuard.VisualStudio/Commands/Menus.vsct` under `DataGuardGroup`.
2. Define `InitCommand` ID in `DataGuardPackage.Commands.cs` (or command enums).
3. Create `RunInitAsync` method inside `DataGuardPackage.Commands.cs`.
4. Ensure `RunInitAsync` checks for pre-existing file to avoid destructive overwrites (fail-safe).
5. Await CLI execution. If exit code `0`, call `VsShellUtilities.OpenDocument` on the main thread for `.dataguard.yml`.
6. **[Red-Team UX]** Update the CLI codebase (e.g., in `dg-init` or equivalent generator) to output the new Minimal YAML template with the schema header.
## Success Criteria
- [ ] Verifiable acceptance criteria
- [ ] Test validation commands

## Related Files
- Modify: `src/DataGuard.VisualStudio/Commands/Menus.vsct`
- Modify: `src/DataGuard.VisualStudio/DataGuardPackage.Commands.cs`
- Modify: `src/DataGuard.Cli/Commands/InitCommand.cs` (or equivalent CLI generator file)
- Create: `tests/DataGuard.VisualStudio.Tests/InitCommandTests.cs`
