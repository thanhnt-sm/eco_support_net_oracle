# Brainstorm Report: DataGuard CLI Distribution & Resolution within Visual Studio VSIX

**Date:** 2026-10-02
**Context:** The user queried whether installing the Visual Studio Extension (VSIX) overwrites the system's DataGuard CLI. While it does not (it extracts to an isolated `%LocalAppData%` extension directory), the `CliLocator.cs` prioritizes the bundled CLI over the system `PATH` CLI. This divergence raises architectural questions regarding distribution, versioning, and developer experience (DX).

## 1. Problem Statement & Requirements
*   **Current State:** The VSIX bundles its own `dataguard.exe` and prioritizes it (unless a custom absolute path is set). It does not overwrite global installations (e.g., `dotnet tool install`).
*   **Core Challenge:** Ensuring the Visual Studio Extension always uses a compatible CLI version without causing fragmentation, unexpected behavior (VS using v1.2 while Terminal uses v1.5), or unnecessary bloat in the VSIX package.
*   **Requirements:**
    *   Maintain brutal reliability (KISS): VSIX must not fail due to a missing CLI.
    *   Avoid redundancy (DRY): Prevent scenarios where developers are confused by differing outputs between Terminal and IDE.
    *   Future-proof maintainability (YAGNI): Don't over-engineer dynamic downloads if a simple bundled approach suffices for current scale.

## 2. Evaluated Approaches

### Approach A: Fully Isolated Bundling (Current Architecture)
The VSIX carries its own CLI and prefers it. 
*   **Pros:** 
    *   **Guaranteed Compatibility:** The VSIX and CLI are deployed as a single, tested unit.
    *   **Zero-Dependency:** Works out-of-the-box on a fresh machine.
*   **Cons:** 
    *   **Version Drift:** A developer updating the global CLI via terminal will not see those changes in VS unless the VSIX is also updated.
    *   **Package Bloat:** VSIX size increases with the embedded executable.

### Approach B: System-First Resolution (BYOC - Bring Your Own CLI)
Modify `CliLocator.cs` to check standard install locations (e.g., `~/.dotnet/tools/dataguard.exe`) and system `PATH` *before* falling back to the bundled CLI.
*   **Pros:**
    *   **Single Source of Truth:** Developers manage one CLI version. Updates via `dotnet tool` immediately reflect in VS.
*   **Cons:**
    *   **Breaking Changes Risk:** If the user updates the system CLI to a major version that breaks the VSIX contract, the IDE integration fails.

### Approach C: Version-Pinned Bootstrapper (Dynamic Download)
Remove the bundled CLI. On first run, the VSIX checks if a compatible version exists locally. If not, it downloads the exact required version into an isolated cache.
*   **Pros:**
    *   **Small VSIX:** Drastically reduces `.vsix` file size.
    *   **Perfect Compatibility:** VSIX controls the exact version it binds to.
*   **Cons:**
    *   **Complexity:** Over-engineered (violates YAGNI for simple tools). Requires internet on first run, handling network failures, proxies, and checksum validations.

## 3. Recommended Solution & Rationale
**Recommendation: Maintain Approach A (Isolated Bundling) but enhance DX with a "Version Drift Warning".**

*   **Rationale:** The current implementation (Approach A) is the safest (KISS). Breaking the IDE because a developer updated their terminal CLI (Approach B) is a poor DX. However, silent version drift is dangerous. We should keep the bundled CLI as the primary engine but add a lightweight background check: if `CliLocator` detects a system CLI on `PATH` that differs significantly from the bundled version, log a warning or surface a VS InfoBar prompting the user to either update the VSIX or explicitly set the custom path setting.

## 4. Implementation Considerations & Risks
*   **Risk:** Performance hit if checking the system CLI version takes too long. 
*   **Mitigation:** The version check must be asynchronous and cached.

## 5. Success Metrics & Validation
*   No VSIX crashes due to incompatible CLI contracts.
*   Developers are actively informed if their terminal CLI and IDE CLI diverge.

## 6. Next Steps
1. Decide whether to retain strict bundling (current) or pivot to system-first.
2. If retaining bundling, plan the implementation of the "Version Drift Warning" UI/log.