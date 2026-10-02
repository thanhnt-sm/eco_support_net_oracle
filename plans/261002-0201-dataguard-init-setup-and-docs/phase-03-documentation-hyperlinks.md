# Phase 3: Documentation Hyperlinks

## Overview
Enhance the Output Window to parse log lines and convert diagnostic codes (e.g., `[DG010]`) into clickable hyperlinks. Update the project repository documentation with Markdown/Mermaid diagrams mapping the VS Extension workflows, avoiding the complexity of an in-IDE Markdown viewer.

## Requirements
- **Functional**:
  - Output Window text parsing for `\[DG\d{3}\]` regex.
  - Apply `IVsOutputWindowPane` extensions to append formatted text and hyperlinked URLs.
  - Clickable links open the default system web browser to the official docs.
  - Add Mermaid flowcharts to `docs/`.
- **Non-functional**:
  - Text parsing must be fast enough not to stutter log streaming.

## Architecture
- Use `IVsOutputWindowPane` methods or cast to `IVsTextView` to add text markers (hyperlinks).
- Maintain a mapping of error codes to documentation URLs.

## Tests (TDD)
1. Write `LogParserTests.cs` to ensure regex correctly extracts codes without false positives.
2. Write `HyperlinkWriterTests.cs` to verify formatting logic.

## Implementation Steps
1. Create `src/DataGuard.VisualStudio/UI/HyperlinkOutputWriter.cs`.
2. Implement Regex matching for `\[DG\d{3}\]`.
3. Output regular text chunks standardly, but when an error code is found, use `OutputTaskItemString` or text markers to make it a clickable web link.
4. Add `docs/04-architecture/vs-extension-workflows.md` containing a Mermaid diagram mapping the Solution Load -> InfoBar -> Init Command -> CLI execution flow.
## Success Criteria
- [ ] Verifiable acceptance criteria
- [ ] Test validation commands

## Related Files
- Modify: `src/DataGuard.VisualStudio/DataGuardPackage.Output.cs`
- Create: `src/DataGuard.VisualStudio/UI/HyperlinkOutputWriter.cs`
- Create: `docs/04-architecture/vs-extension-workflows.md`
- Create: `tests/DataGuard.VisualStudio.Tests/LogParserTests.cs`
