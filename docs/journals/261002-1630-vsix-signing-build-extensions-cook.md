# Cook of VSIX Signing Integration into build-extensions: All gates green, TDD test suite verified, code review approved

**Date**: 2026-10-02 16:30
**Severity**: Low
**Component**: Visual Studio VSIX Packaging, Code Signing, Build Scripts, Integrity Hashing, Documentation
**Status**: Resolved on branch `feat/vsix-signing-build-extensions`; ready for merge

**Plan:** `plans/261002-0600-vsix-signing-build-extensions/plan.md`
**Mode:** `/ck:cook plans/261002-0600-vsix-signing-build-extensions --TDD` · Tester subagent (`TesterSubagent`, 72/72 tests passed) → Code reviewer subagent (`CodeReviewerSubagent2`, score 10/10, approved) → Finalize.

---

## 1. Executive Summary

The VSIX digital signing integration plan (`plans/261002-0600-vsix-signing-build-extensions`) has been implemented under strict TDD mode (3.T -> 3.I -> 3.V), validated across both PowerShell 7 (pwsh) and Windows PowerShell 5.1, reviewed by an independent code review subagent, and signed off.

All 4 phases are complete:
1. **Phase 1: Tooling Resolution & Test Fixture Helpers**:
   - Implemented `Resolve-VsixSignTool` priority detection: `OpenVsixSignTool` in `PATH` / `~/.dotnet/tools` -> Visual Studio SDK `VsixSignTool.exe` via `vswhere` -> Actionable error guidance.
   - Built `scripts/tests/vsix-signature/helpers.psm1` supporting `New-TestCodeSigningCert` and `Remove-TestCodeSigningCert` with `-DeleteKey` cleanup.
2. **Phase 2: Tests-first Fixtures & Verification Engine**:
   - Generated `scripts/tests/vsix-signature/fixtures/minimal-unsigned.vsix` (<1KB minimal OPC package).
   - Developed `scripts/verify-vsix-signature.ps1` utilizing `System.IO.Packaging.PackageDigitalSignatureManager` (`WindowsBase`) validating OPC signature presence, cryptographic validity, optional thumbprint matching, and optional subject matching.
   - Built automated test runner `scripts/tests/vsix-signature/run.ps1` covering 4 core scenarios: unsigned package rejection, valid signed package approval, tampered package rejection, and thumbprint mismatch rejection.
3. **Phase 3: Secure Signing Implementation & build-extensions Integration**:
   - Implemented `scripts/sign-vsix.ps1` supporting PFX files (`-CertificatePath`), password environments (`-CertificatePasswordEnv`), direct thumbprints (`-CertificateThumbprint`), and timestamping servers (`-TimestampServer`, `-NoTimestamp`).
   - Hardened credential security: Eliminated CLI password passing (`-p`/`/p`) to prevent process table / event 4688 leaks. When a PFX is supplied, it is safely imported into `CurrentUser\My` in-session, signed via thumbprint (`--sha1`), and cleanly purged in a `finally` block with `-DeleteKey` and `.Reset()`.
   - Updated `scripts/build-extensions.ps1` and `scripts/build-extensions.bat` preserving full backwards compatibility for 1-click unsigned builds while enforcing the **Integrity Invariant** (SHA-256 is always recalculated and written to `.sha256` after the signing step completes).
4. **Phase 4: Red-Team Validation & Documentation**:
   - Comprehensive test suite covering invalid passwords, non-existent files, and thumbprint mismatches.
   - Updated bilingual operational documentation in `docs/05-operations/extension-build-guide.vi.md` and `docs/05-operations/extension-build-guide.md`.
   - Verified repo gates (`check-license-consistency.py`, `check-workflow-policy.py`).

---

## 2. Test & Verification Evidence

- **VSIX Signature Test Suite (`run.ps1`)**: 4/4 tests passed on both pwsh 7 and Windows PowerShell 5.1.
- **Integration Test Suite (`test_phase3_integration.ps1`)**: 6/6 tests passed on both pwsh 7 and Windows PowerShell 5.1.
- **Tooling Test Suite (`test_phase1_tooling.ps1`)**: 3/3 tests passed on both pwsh 7 and Windows PowerShell 5.1.
- **Pytest Suite (`scripts/tests/`)**: 44/44 passed.
- **SPDX Licence Consistency Gate**: Scanned 194 files, 6 tracked copies identical, 0 errors.
- **Workflow Policy Gate**: Passed.
- **Total Tests Passed**: 72 passed, 0 failed.

---

## 3. Code Review Findings & Remediation

- **Cycle 1 Review (Score 6/10)**:
  - Finding P0: Password passed on CLI arguments (`-p`/`/p`).
  - Finding P1: Potential private key container residue on .NET Framework.
- **Remediation**:
  - Refactored `scripts/sign-vsix.ps1` to import PFX into `CurrentUser\My` and sign exclusively by thumbprint (`--sha1`/`/sha1`), avoiding CLI password exposure.
  - Added robust `finally` teardown using `Remove-Item -DeleteKey` and `.Reset()`.
- **Cycle 2 Review (Score 10/10)**:
  - Approved with 0 critical or major findings.
