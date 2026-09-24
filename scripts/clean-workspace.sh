#!/usr/bin/env bash
# Cleans workspace caches, build outputs, and temporary artifacts across DataGuard.
# Supported modes: --pre (PreBuild), --post (PostBuild), --deep (Deep)
set -euo pipefail
shopt -s nullglob

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -z "$ROOT" || "$ROOT" == "/" || ! -f "$ROOT/DataGuard.sln" ]]; then
    echo "Refusing to clean from unsafe or unverified repository root: $ROOT" >&2
    exit 1
fi
cd "$ROOT"
MODE="pre"
SKIP_VSCODE=0
SKIP_VISUALSTUDIO=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --mode|-Mode|-m)
            shift
            case "${1:-}" in
                pre|PreBuild) MODE="pre" ;;
                post|PostBuild) MODE="post" ;;
                deep|Deep) MODE="deep" ;;
                *) echo "Unknown mode: ${1:-}" >&2; exit 1 ;;
            esac
            ;;
        --mode=*|-Mode=*)
            val="${1#*=}"
            case "$val" in
                pre|PreBuild) MODE="pre" ;;
                post|PostBuild) MODE="post" ;;
                deep|Deep) MODE="deep" ;;
                *) echo "Unknown mode: $val" >&2; exit 1 ;;
            esac
            ;;
        --pre|-pre|pre|PreBuild)
            MODE="pre"
            ;;
        --post|-post|post|PostBuild)
            MODE="post"
            ;;
        --deep|-deep|deep|Deep)
            MODE="deep"
            ;;
        --skip-vscode|-skip-vscode)
            SKIP_VSCODE=1
            ;;
        --skip-visualstudio|-skip-visualstudio)
            SKIP_VISUALSTUDIO=1
            ;;
        *)
            echo "Unknown argument: $1. Usage: $0 [--pre|--post|--deep] [--mode PreBuild|PostBuild|Deep] [--skip-vscode] [--skip-visualstudio]" >&2
            exit 1
            ;;
    esac
    shift
done

echo "================================================================================"
echo " DataGuard Workspace Cache Cleanup (Bash) - Mode: $MODE"
echo "================================================================================"

CRITICAL_FAILURES=()

check_critical() {
    for path in "$@"; do
        if [ -e "$path" ]; then
            CRITICAL_FAILURES+=("$path")
            echo "  [!] Critical locked path could not be deleted: $path" >&2
        fi
    done
}

case "$MODE" in
    pre)
        echo "Purging test results, coverage caches, and stale logs..."
        TMP_DIR="${TMPDIR:-/tmp}"
        if [[ -n "$TMP_DIR" && "$TMP_DIR" != "/" && "$TMP_DIR" == /* ]]; then
            if [ -L "$TMP_DIR/DataGuard" ]; then
                rm -f -- "$TMP_DIR/DataGuard" 2>/dev/null || true
            elif [ -d "$TMP_DIR/DataGuard" ]; then
                find -P "$TMP_DIR/DataGuard" -mindepth 1 -maxdepth 1 -type d -mmin +15 -exec rm -rf -- {} + 2>/dev/null || true
                rmdir "$TMP_DIR/DataGuard" 2>/dev/null || true
            fi
            find -P "$TMP_DIR" -maxdepth 1 -name "dataguard-*" -mmin +15 -exec rm -rf -- {} + 2>/dev/null || true
        fi
        find . -type d -name "nupkg" -not -path "*/.*/*" -prune -exec rm -rf -- {} + 2>/dev/null || true
        rm -rf artifacts/nupkg

        find . -type d -name "TestResults" -not -path "*/.*/*" -prune -exec rm -rf -- {} + 2>/dev/null || true
        rm -rf coverage .coverage .testcontainers
        find . -type f \( -name "*.trx" -o -name "*.cobertura.xml" -o -name "*.log" -o -name "*.nupkg" -o -name "*.snupkg" -o -name "*.tsbuildinfo" \) -not -path "*/.*/*" -delete 2>/dev/null || true

        echo "Cleaning intermediate bundled CLI staging and compiler outputs..."
        if [[ "$SKIP_VISUALSTUDIO" -ne 1 ]]; then
            rm -rf -- src/DataGuard.VisualStudio/obj/cli src/DataGuard.VisualStudio/cli || true
            check_critical "src/DataGuard.VisualStudio/obj/cli" "src/DataGuard.VisualStudio/cli"
        fi
        if [[ "$SKIP_VSCODE" -ne 1 ]]; then
            rm -rf -- src/DataGuard.VSCode/out src/DataGuard.VSCode/dist src/DataGuard.VSCode/server || true
            check_critical "src/DataGuard.VSCode/out" "src/DataGuard.VSCode/dist" "src/DataGuard.VSCode/server"
        fi

        echo "Cleaning previous artifacts, source VSIXes, and sensitive reports..."
        rm -f artifacts/*.vsix artifacts/*.sha256 artifacts/*/*.vsix artifacts/*/*.sha256 artifacts/*.sarif artifacts/*summary*.json artifacts/*report*.json artifacts/*scan*.json
        rm -f src/DataGuard.VSCode/*.vsix src/DataGuard.VSCode/*.vsix.sha256
        rm -f src/DataGuard.VisualStudio/*.vsix src/DataGuard.VisualStudio/*.vsix.sha256
        ;;

    post)
        echo "Purging intermediate uncompressed CLI staging while retaining final artifacts..."
        TMP_DIR="${TMPDIR:-/tmp}"
        if [[ -n "$TMP_DIR" && "$TMP_DIR" != "/" && "$TMP_DIR" == /* ]]; then
            if [ -L "$TMP_DIR/DataGuard" ]; then
                rm -f -- "$TMP_DIR/DataGuard" 2>/dev/null || true
            elif [ -d "$TMP_DIR/DataGuard" ]; then
                find -P "$TMP_DIR/DataGuard" -mindepth 1 -maxdepth 1 -type d -mmin +15 -exec rm -rf -- {} + 2>/dev/null || true
                rmdir "$TMP_DIR/DataGuard" 2>/dev/null || true
            fi
            find -P "$TMP_DIR" -maxdepth 1 -name "dataguard-*" -mmin +15 -exec rm -rf -- {} + 2>/dev/null || true
        fi
        if [[ "$SKIP_VISUALSTUDIO" -ne 1 ]]; then
            rm -rf -- src/DataGuard.VisualStudio/obj/cli src/DataGuard.VisualStudio/cli || true
            check_critical "src/DataGuard.VisualStudio/obj/cli" "src/DataGuard.VisualStudio/cli"
        fi
        if [[ "$SKIP_VSCODE" -ne 1 ]]; then
            rm -rf -- src/DataGuard.VSCode/out src/DataGuard.VSCode/dist src/DataGuard.VSCode/server || true
            check_critical "src/DataGuard.VSCode/out" "src/DataGuard.VSCode/dist" "src/DataGuard.VSCode/server"
        fi
        rm -rf .testcontainers

        echo "Purging sensitive scan reports from artifacts folder..."
        rm -f artifacts/*.sarif artifacts/*summary*.json artifacts/*report*.json artifacts/*scan*.json

        echo "Purging source VSIX copies in source folders..."
        rm -f src/DataGuard.VSCode/*.vsix src/DataGuard.VSCode/*.vsix.sha256
        rm -f src/DataGuard.VisualStudio/*.vsix src/DataGuard.VisualStudio/*.vsix.sha256
        ;;
    deep)
        echo "Shutting down build servers and executing dotnet clean..."
        TMP_DIR="${TMPDIR:-/tmp}"
        if [[ -n "$TMP_DIR" && "$TMP_DIR" != "/" && "$TMP_DIR" == /* ]]; then
            if [ -L "$TMP_DIR/DataGuard" ]; then
                rm -f -- "$TMP_DIR/DataGuard" 2>/dev/null || true
            elif [ -d "$TMP_DIR/DataGuard" ]; then
                find -P "$TMP_DIR/DataGuard" -mindepth 1 -maxdepth 1 -type d -mmin +15 -exec rm -rf -- {} + 2>/dev/null || true
                rmdir "$TMP_DIR/DataGuard" 2>/dev/null || true
            fi
            find -P "$TMP_DIR" -maxdepth 1 -name "dataguard-*" -mmin +15 -exec rm -rf -- {} + 2>/dev/null || true
        fi
        dotnet build-server shutdown 2>/dev/null || true
        dotnet clean DataGuard.sln -c Release -v quiet 2>/dev/null || echo "Warning: dotnet clean Release returned non-zero exit code" >&2
        dotnet clean DataGuard.sln -c Debug -v quiet 2>/dev/null || echo "Warning: dotnet clean Debug returned non-zero exit code" >&2
        dotnet nuget locals http-cache --clear 2>/dev/null || true
        dotnet nuget locals temp --clear 2>/dev/null || true
        echo "Wiping all bin and obj folders..."
        find . -type d \( -name "bin" -o -name "obj" \) -not -path "*/.*/*" -prune -exec rm -rf -- {} + 2>/dev/null || true
        echo "Wiping node_modules and npm build outputs..."
        rm -rf src/DataGuard.VSCode/node_modules
        rm -rf src/DataGuard.VSCode/dist src/DataGuard.VSCode/out src/DataGuard.VSCode/server
        rm -rf src/DataGuard.VSCode/.vscode-test

        echo "Wiping IDE and benchmark caches..."
        rm -rf .vs .format .cache BenchmarkDotNet.Artifacts

        echo "Wiping test results, nupkg, and test hives..."
        find . -type d -name "TestResults" -not -path "*/.*/*" -prune -exec rm -rf -- {} + 2>/dev/null || true
        find . -type d -name "nupkg" -not -path "*/.*/*" -prune -exec rm -rf -- {} + 2>/dev/null || true
        rm -rf coverage .coverage sbom .testcontainers
        rm -rf artifacts/nuget artifacts/nupkg artifacts/vscode artifacts/visualstudio
        rm -f artifacts/*.vsix artifacts/*.sha256 artifacts/*/*.vsix artifacts/*/*.sha256 artifacts/*.sarif artifacts/*summary*.json artifacts/*report*.json artifacts/*scan*.json
        find . -type f \( -name "*.trx" -o -name "*.cobertura.xml" -o -name "*.log" -o -name "*.tsbuildinfo" \) -not -path "*/.*/*" -delete 2>/dev/null || true
        ;;
esac

echo "Cleanup completed successfully!"
if [[ ${#CRITICAL_FAILURES[@]} -gt 0 ]]; then
    echo "Error: Critical locked paths could not be deleted:" >&2
    for f in "${CRITICAL_FAILURES[@]}"; do
        echo "  - $f" >&2
    done
    exit 1
fi
