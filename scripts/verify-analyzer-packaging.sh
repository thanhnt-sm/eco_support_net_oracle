#!/usr/bin/env bash
# ==============================================================================
# DataGuard.Analyzers packaging check (licence migration D4) and analyzer load gate.
#
# 1. Package layout: the analyzer package must be build-time only: the analyzer
#    DLLs live under analyzers/dotnet/cs, there is no lib/ folder, and a consumer
#    that references the package (plus DataGuard.Contracts for the attributes)
#    builds green WITHOUT DataGuard.Analyzers.dll / DataGuard.SqlClassification.dll
#    landing in its output. DataGuard.Contracts.dll does land there (it is the MIT
#    package the consumer references on purpose).
# 2. Compiler line: the committed lock files of DataGuard.Analyzers and
#    DataGuard.CodeFixes must resolve Microsoft.CodeAnalysis.CSharp(.Workspaces)
#    to exactly 4.12.0, the Roslyn line VS 2022 17.12+ and the .NET 9.0.1xx SDK
#    run (scripts/assert-roslyn-lock.sh; SDK-independent).
# 3. Load gate: the consumer is compiled with SDK 9.0.100 pinned through its own
#    global.json (rollForward disable), so the gate does not float with the SDK of
#    the runner. Roslyn refuses an analyzer built against a newer compiler with
#    `warning CS9057` and reports nothing, so the build log must not contain
#    CS9057 and must contain DG017 (SELECT * in a Dapper call) and DG001.
#
# Not a test_*.py on purpose: it runs `dotnet pack`-produced artifacts. The
# lock-file half is unit-tested by scripts/tests/test_assert_roslyn_lock.py.
#
# Usage:  scripts/verify-analyzer-packaging.sh <dir-with-nupkgs>
#   e.g.  dotnet pack DataGuard.CrossPlatform.slnf -c Release -o "$TMP/nupkg"
#         scripts/verify-analyzer-packaging.sh "$TMP/nupkg"
# Exit 0 = every assertion holds. Needs unzip, python3 and the .NET SDK 9.0.100
# (CI installs it next to the floating 9.0.x); uses a private NUGET_PACKAGES cache
# so a stale ~/.nuget/packages entry with the same version cannot mask a change.
# The consumer also needs nuget.org (System.Composition.Hosting is a nuspec dependency).
# ==============================================================================
set -euo pipefail

NUPKG_DIR="${1:?usage: $0 <dir-with-nupkgs>}"
[ -d "$NUPKG_DIR" ] || { echo "not a directory: $NUPKG_DIR" >&2; exit 2; }
NUPKG_DIR="$(cd "$NUPKG_DIR" && pwd)"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# The compiler line the IDE runs; keep in step with RoslynReferenceVersionTests (<= 4.12.0.0),
# .github/dependabot.yml (ignore >= 4.13.0) and the VSIX manifest floor (VS 2022 17.12).
EXPECTED_ROSLYN="4.12.0"
# The SDK whose Roslyn is 4.12.0 (global.json of the temp consumer, rollForward disable).
CONSUMER_SDK="9.0.100"

native_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }

ANALYZERS_PKG=$(ls "$NUPKG_DIR"/DataGuard.Analyzers.*.nupkg 2>/dev/null | head -n 1 || true)
[ -n "$ANALYZERS_PKG" ] || { echo "FAIL: no DataGuard.Analyzers.*.nupkg in $NUPKG_DIR" >&2; exit 2; }
VERSION=$(basename "$ANALYZERS_PKG" .nupkg)
VERSION=${VERSION#DataGuard.Analyzers.}

FAILS=0
check() { # check <description> <actual> <expected>
  if [ "$2" = "$3" ]; then echo "  ok   $1 = $2"; else echo "  FAIL $1: got $2, expected $3"; FAILS=$((FAILS + 1)); fi
}

echo "== package layout ($(basename "$ANALYZERS_PKG"))"
LISTING=$(unzip -l "$ANALYZERS_PKG")
check "analyzers/dotnet/cs DLLs (Analyzers, Contracts, SqlClassification)" \
  "$(printf '%s\n' "$LISTING" | grep -cE 'analyzers/dotnet/cs/DataGuard\.(Analyzers|Contracts|SqlClassification)\.dll' || true)" 3
check "lib/ entries" "$(printf '%s\n' "$LISTING" | grep -c ' lib/' || true)" 0

echo "== compiler line in the committed lock files (Roslyn $EXPECTED_ROSLYN)"
if bash "$REPO_ROOT/scripts/assert-roslyn-lock.sh" "$REPO_ROOT/src/DataGuard.Analyzers/packages.lock.json" "$EXPECTED_ROSLYN" \
     Microsoft.CodeAnalysis.CSharp; then
  echo "  ok   src/DataGuard.Analyzers/packages.lock.json"
else
  echo "  FAIL src/DataGuard.Analyzers/packages.lock.json does not resolve Roslyn to $EXPECTED_ROSLYN"; FAILS=$((FAILS + 1))
fi
if bash "$REPO_ROOT/scripts/assert-roslyn-lock.sh" "$REPO_ROOT/src/DataGuard.CodeFixes/packages.lock.json" "$EXPECTED_ROSLYN" \
     Microsoft.CodeAnalysis.CSharp Microsoft.CodeAnalysis.CSharp.Workspaces; then
  echo "  ok   src/DataGuard.CodeFixes/packages.lock.json"
else
  echo "  FAIL src/DataGuard.CodeFixes/packages.lock.json does not resolve Roslyn to $EXPECTED_ROSLYN"; FAILS=$((FAILS + 1))
fi

echo "== consumer build (SDK $CONSUMER_SDK pinned, Roslyn 4.12)"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/consumer" "$WORK/gp"
cat > "$WORK/consumer/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(native_path "$NUPKG_DIR")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
# global.json is resolved from the working directory, so the build below runs inside $WORK/consumer.
# rollForward=disable: a runner with only a newer 9.0.x (Roslyn >= 4.14) fails here instead of
# silently accepting an analyzer that VS 17.12 / SDK 9.0.1xx would refuse.
cat > "$WORK/consumer/global.json" <<EOF
{ "sdk": { "version": "$CONSUMER_SDK", "rollForward": "disable" } }
EOF
cat > "$WORK/consumer/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Exe, not a class library: only an application copies its package dependencies to bin/,
         which is where a leaked DataGuard DLL would land in a real consumer. -->
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DataGuard.Analyzers" Version="$VERSION" />
    <PackageReference Include="DataGuard.Contracts" Version="$VERSION" />
  </ItemGroup>
</Project>
EOF
cat > "$WORK/consumer/Class1.cs" <<'EOF'
[DataGuard.Contracts.SkipContractCheck(Reason = "packaging check")]
public class Class1
{
    public static void Main()
    {
    }
}
EOF
# Dapper-shaped call with SELECT *: the analyzer is syntax-only (method name + SQL text), so a local
# Query<T> stub stands in for Dapper without adding a package to the consumer.
cat > "$WORK/consumer/Repo.cs" <<'EOF'
using System.Collections.Generic;
using System.Data;

public static class DapperStub
{
    public static IEnumerable<T> Query<T>(this IDbConnection connection, string sql, object? param = null) => new List<T>();
}

public class Customer
{
    public int CustomerId { get; set; }
}

public class Repo
{
    public IEnumerable<Customer> Load(IDbConnection connection)
        => connection.Query<Customer>("SELECT * FROM Customers");
}
EOF

export NUGET_PACKAGES
NUGET_PACKAGES=$(native_path "$WORK/gp")
if CONSUMER_SDK_ACTUAL=$(cd "$WORK/consumer" && dotnet --version 2>&1); then
  check "consumer SDK (global.json $CONSUMER_SDK, rollForward disable)" "$CONSUMER_SDK_ACTUAL" "$CONSUMER_SDK"
else
  echo "  FAIL .NET SDK $CONSUMER_SDK is not installed (dotnet --version: $CONSUMER_SDK_ACTUAL); CI installs it with a second setup-dotnet step"
  FAILS=$((FAILS + 1))
fi
if (cd "$WORK/consumer" && dotnet build "$(native_path "$WORK/consumer")" -c Debug -v:d -nologo > "$WORK/build.log" 2>&1); then
  echo "  ok   consumer builds (attribute resolves from DataGuard.Contracts)"
else
  tail -n 30 "$WORK/build.log"; echo "  FAIL consumer build"; FAILS=$((FAILS + 1))
fi
# The layout change must not stop the compiler from loading the analyzer: csc gets it as /analyzer:.
check "csc loads analyzers/dotnet/cs/DataGuard.Analyzers.dll"   "$(grep -E '/analyzer:.*analyzers[/\]dotnet[/\]cs[/\]DataGuard\.Analyzers\.dll' "$WORK/build.log" | head -n 1 | wc -l | tr -d ' ')" 1
# Roslyn 4.12 must accept the analyzer (no CS9057) and actually run it (DG017 on SELECT *, DG001 on the call).
# MSBuild -v:d echoes each warning more than once (csc output + summary), so these are presence checks.
reported() { if grep -q "$1" "$WORK/build.log"; then echo present; else echo absent; fi; }
check "CS9057 (analyzer newer than the compiler) lines" "$(grep -c 'CS9057' "$WORK/build.log" || true)" 0
check "DG017 (SELECT *) reported by the consumer build" "$(reported 'warning DG017')" present
check "DG001 (unvalidated SQL call) reported by the consumer build" "$(reported 'warning DG001')" present

OUT_DIR=$(ls -d "$WORK"/consumer/bin/Debug/*/ 2>/dev/null | head -n 1 || true)
if [ -z "$OUT_DIR" ]; then
  echo "  FAIL no consumer output directory"; FAILS=$((FAILS + 1))
else
  check "output DataGuard.Analyzers|SqlClassification DLLs" \
    "$(ls "$OUT_DIR" | grep -cE '^DataGuard\.(Analyzers|SqlClassification)\.dll$' || true)" 0
  check "output DataGuard.Contracts.dll (via the MIT Contracts package)" \
    "$(ls "$OUT_DIR" | grep -cE '^DataGuard\.Contracts\.dll$' || true)" 1
fi

if [ "$FAILS" -eq 0 ]; then echo "PASS: analyzer packaging is build-time only and loads under Roslyn $EXPECTED_ROSLYN"; else echo "FAIL: $FAILS assertion(s)"; exit 1; fi
