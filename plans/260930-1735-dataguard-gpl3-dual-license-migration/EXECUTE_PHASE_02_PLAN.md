# Phase 2: Legal text va metadata migration

## Context
DataGuard is transitioning from an MIT license to a dual license (GPL-3.0-only + Commercial) starting from v0.4.0. Phase 2 is the core execution step for this: it involves replacing all "MIT" surface areas with GPL-3.0-only, updating package metadata (NuGet/npm/VSIX), embedding third-party notices in all distribution channels, updating README/FAQ/docs, and adjusting analyzer packaging. The goal is a complete, legally accurate migration without breaking existing consumers.

## Approach
1. **License & Legal Files Creation**:
   - Create root `LICENSE` file containing the verbatim GPL-3.0 text from gnu.org.
   - Save the old MIT license to `docs/legal/MIT-v0.1.0-v0.3.0.txt`.
   - Create `docs/legal/ADDITIONAL-PERMISSIONS.md` based on the FSF template, explicitly naming Oracle ODP.NET, Microsoft SNI, Visual Studio, and VS Code. Include a banner stating the text has not been reviewed by a lawyer.
   - Create `docs/legal/THIRD-PARTY-NOTICES.md` with verbatim Oracle FDHUT and Microsoft SNI licenses. Include tracked copies for VS and VS Code extensions.

2. **Metadata Consolidation**:
   - Update `Directory.Build.props` to include `PackageLicenseExpression=GPL-3.0-only`, `Authors`, and `Copyright`.
   - Remove redundant `PackageLicenseExpression` and `Authors` tags from 9 individual `.csproj` files.
   - Explicitly override `PackageLicenseExpression=MIT` in `DataGuard.Contracts.csproj`.
   - Add `LABEL org.opencontainers.image.licenses="GPL-3.0-only"` to the `Dockerfile`.

3. **Analyzer Packaging Adjustment**:
   - In `DataGuard.Analyzers.csproj`, change `IncludeBuildOutput=true` to `IncludeBuildOutput=false` and ensure `DevelopmentDependency=true`.
   - Adjust `DataGuard.SqlClassification.csproj` and `DataGuard.LanguageServer.csproj` to `IsPackable=false`.
   - Ensure `DataGuard.Contracts` is correctly referenced as a dependency for the analyzer.

4. **Distribution Channel Notices**:
   - Update `DataGuard.Cli.csproj` with `<None>` items to copy `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `ADDITIONAL-PERMISSIONS.md` to the publish directory.
   - Update `DataGuard.VisualStudio.csproj` to include the notices as VSIX `<Content>`.
   - Ensure `.vscodeignore` allows `*.md` root files for VS Code VSIX.

5. **Documentation Updates**:
   - Update `README.md` and `README.vi.md` to add a "Dual licence & FAQ" section. State that GPL-3.0 doesn't prohibit commercial use, copyleft applies on distribution, and include the email placeholder for commercial licenses. Emphasize the lack of lawyer review.
   - Update `SUPPORT.md`, `CHANGELOG.md` (add nightly MIT snapshot), and relevant files in `docs/` to reflect the new license.

6. **SPDX Consistency Gate**:
   - Implement `scripts/check-license-consistency.py` (using Python stdlib) to enforce `\bMIT\b` absence across a defined `FILE_LIST`, utilizing an `ALLOWED_MENTIONS` allow-list.
   - Write comprehensive unit tests in `scripts/tests/test_check_license_consistency.py`.

## Critical files & anchors
- `Directory.Build.props`: Lines 25-34 to centralize license and author metadata.
- `src/DataGuard.Contracts/DataGuard.Contracts.csproj`: Must retain the MIT license expression.
- `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj`: Lines 11-14 to adjust `IncludeBuildOutput` for correct analyzer packaging.
- `scripts/check-license-consistency.py`: New Python script to serve as the strict SPDX enforcement gate.
- `docs/legal/*`: New directory for managing all licensing variations and notices.

## Verification
- **Unit Tests**: Run `python3 -m unittest discover -s scripts/tests -v`. All 9 new license consistency cases must PASS.
- **Gate Execution**: Run `python3 scripts/check-license-consistency.py`. Must exit 0 with no unexpected MIT mentions.
- **NuGet Packaging**: Run `dotnet pack DataGuard.CrossPlatform.slnf -c Release`. Verify exactly 13 `.nupkg` files are generated. Extract nuspecs and confirm 12 use `GPL-3.0-only` and Contracts uses `MIT`.
- **Artifact Inspection**: Publish CLI and build VSIX. Verify `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `ADDITIONAL-PERMISSIONS.md` exist in the outputs (`unzip -l`). Check Docker image label.
- **Analyzer Integration**: Create a temporary consumer project referencing the new `DataGuard.Analyzers` and `DataGuard.Contracts` packages. Ensure it compiles (`dotnet build`) and that the analyzer DLLs are *not* copied to the consumer's `bin` output.

## Assumptions & contingencies
- **Assumption**: The FSF template for Section 7 additional permissions is sufficient for the dual-license strategy despite lacking formal legal review.
- **Contingency**: If the analyzer packaging changes (`IncludeBuildOutput=false`) break consumer builds or quick-fixes (detected during Verification), the change will be reverted. The limitation will be documented in the FAQ and ADR as an accepted risk, and the phase will proceed.