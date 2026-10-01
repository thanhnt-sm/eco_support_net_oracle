#!/usr/bin/env bash
# ==============================================================================
# DataGuard.Analyzers packaging check (licence migration D4).
#
# The analyzer package must be build-time only: the analyzer DLLs live under
# analyzers/dotnet/cs, there is no lib/ folder, and a consumer that references the
# package (plus DataGuard.Contracts for the attributes) builds green WITHOUT
# DataGuard.Analyzers.dll / DataGuard.SqlClassification.dll landing in its output.
# DataGuard.Contracts.dll does land there (it is the MIT package the consumer
# references on purpose).
#
# Not a test_*.py on purpose: it runs `dotnet pack`-produced artifacts, and the CI
# unit-test job (`unittest discover -s scripts/tests`) has no .NET SDK step yet.
#
# Usage:  scripts/verify-analyzer-packaging.sh <dir-with-nupkgs>
#   e.g.  dotnet pack DataGuard.CrossPlatform.slnf -c Release -o "$TMP/nupkg"
#         scripts/verify-analyzer-packaging.sh "$TMP/nupkg"
# Exit 0 = every assertion holds. Needs unzip + dotnet; uses a private NUGET_PACKAGES
# cache so a stale ~/.nuget/packages entry with the same version cannot mask a change.
# The consumer also needs nuget.org (System.Composition.Hosting is a nuspec dependency).
# ==============================================================================
set -euo pipefail

NUPKG_DIR="${1:?usage: $0 <dir-with-nupkgs>}"
[ -d "$NUPKG_DIR" ] || { echo "not a directory: $NUPKG_DIR" >&2; exit 2; }
NUPKG_DIR="$(cd "$NUPKG_DIR" && pwd)"

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

echo "== consumer build"
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

export NUGET_PACKAGES
NUGET_PACKAGES=$(native_path "$WORK/gp")
if dotnet build "$(native_path "$WORK/consumer")" -c Debug -v:d -nologo > "$WORK/build.log" 2>&1; then
  echo "  ok   consumer builds (attribute resolves from DataGuard.Contracts)"
else
  tail -n 30 "$WORK/build.log"; echo "  FAIL consumer build"; FAILS=$((FAILS + 1))
fi
# The layout change must not stop the compiler from loading the analyzer: csc gets it as /analyzer:.
check "csc loads analyzers/dotnet/cs/DataGuard.Analyzers.dll"   "$(grep -E '/analyzer:.*analyzers[/\]dotnet[/\]cs[/\]DataGuard\.Analyzers\.dll' "$WORK/build.log" | head -n 1 | wc -l | tr -d ' ')" 1

OUT_DIR=$(ls -d "$WORK"/consumer/bin/Debug/*/ 2>/dev/null | head -n 1 || true)
if [ -z "$OUT_DIR" ]; then
  echo "  FAIL no consumer output directory"; FAILS=$((FAILS + 1))
else
  check "output DataGuard.Analyzers|SqlClassification DLLs" \
    "$(ls "$OUT_DIR" | grep -cE '^DataGuard\.(Analyzers|SqlClassification)\.dll$' || true)" 0
  check "output DataGuard.Contracts.dll (via the MIT Contracts package)" \
    "$(ls "$OUT_DIR" | grep -cE '^DataGuard\.Contracts\.dll$' || true)" 1
fi

if [ "$FAILS" -eq 0 ]; then echo "PASS: analyzer packaging is build-time only"; else echo "FAIL: $FAILS assertion(s)"; exit 1; fi
