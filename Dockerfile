# DataGuard.Cli — multi-arch build (linux/amd64 + linux/arm64).
# Pattern follows the official dotnet-docker samples:
#   https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile
#
# The build stage cross-compiles the app for TARGETARCH via
# `dotnet publish -a $TARGETARCH`. The final stage contains no RUN steps,
# so per-platform images are assembled from the multi-arch runtime base
# without emulation.

# ---------- Build stage ----------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
# BuildKit platform args are only visible to RUN when re-declared in the stage.
ARG TARGETARCH
# Release version to bake into the binary (e.g. 1.2.3); the csproj files
# hardcode 0.1.0-alpha.1 which would otherwise end up in the image.
ARG VERSION=0.1.0-ci
WORKDIR /source

# Copy project files and lockfiles for optimal layer caching on deterministic restore.
# Directory.Build.props is auto-imported by MSBuild — it MUST be present
# during restore so the restore graph matches the publish graph. NuGet.config
# carries the nuget.org-only source mapping used by every other restore.
# The list is the CLI's full ProjectReference closure: Core holds no database
# driver, each adapter (SQL Server: SqlClient + ScriptDOM) brings its own.
COPY --link Directory.Build.props NuGet.config ./
COPY --link src/DataGuard.Core/DataGuard.Core.csproj src/DataGuard.Core/packages.lock.json src/DataGuard.Core/
COPY --link src/DataGuard.Contracts/DataGuard.Contracts.csproj src/DataGuard.Contracts/packages.lock.json src/DataGuard.Contracts/
COPY --link src/DataGuard.SqlClassification/DataGuard.SqlClassification.csproj src/DataGuard.SqlClassification/packages.lock.json src/DataGuard.SqlClassification/
COPY --link src/DataGuard.Analyzers/DataGuard.Analyzers.csproj src/DataGuard.Analyzers/packages.lock.json src/DataGuard.Analyzers/
COPY --link src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj src/DataGuard.SqlServer.Adapter/packages.lock.json src/DataGuard.SqlServer.Adapter/
COPY --link src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj src/DataGuard.Oracle.Adapter/packages.lock.json src/DataGuard.Oracle.Adapter/
COPY --link src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj src/DataGuard.MySql.Adapter/packages.lock.json src/DataGuard.MySql.Adapter/
COPY --link src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj src/DataGuard.PostgreSql.Adapter/packages.lock.json src/DataGuard.PostgreSql.Adapter/
COPY --link src/DataGuard.Cli/DataGuard.Cli.csproj src/DataGuard.Cli/packages.lock.json src/DataGuard.Cli/

# Restore the CLI project, not the whole solution: DataGuard.sln also lists
# the test projects, which are intentionally not part of the image build and
# would make the restore fail (MSB3202). Project-level restore pulls in all
# ProjectReferences (Core, adapters, analyzers) transitively.
# Directory.Build.props sets RuntimeIdentifiers (linux-x64;linux-arm64), so restore
# populates assets for both architectures deterministically in locked mode.
RUN dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj --locked-mode

# Copy the rest of the source and publish the CLI.
# Note: --arch $TARGETARCH relies on the SDK normalizing "amd64" -> "x64"
# (verified on SDK 9.0.x; arm64 stays arm64). Do not "fix" this to
# linux-$TARGETARCH — that RID does not exist.
COPY --link . .
RUN dotnet publish src/DataGuard.Cli/DataGuard.Cli.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --arch $TARGETARCH \
    -p:Version=$VERSION

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:9.0@sha256:ee9e6309cef467e134056f9115b31fe3a43ef2959e5b1f42bce0cc97f1b3db3f AS final
WORKDIR /app

# Non-root user baked into .NET 9 runtime images (UID 1654).
USER $APP_UID

COPY --link --from=build /app/publish .

ARG ORG_SOURCE="thanhnt-sm/eco_support_net_oracle"
LABEL org.opencontainers.image.source="https://github.com/${ORG_SOURCE}"
LABEL org.opencontainers.image.description="DataGuard CLI — contract validation for Entity to Stored Procedure/Raw SQL"
# Licence surface for local builds. Published images take org.opencontainers.image.* from
# docker/metadata-action in release.yml, which overrides these; release.yml must set the same value.
LABEL org.opencontainers.image.licenses="GPL-3.0-only"
# Custom key: metadata-action overwrites image.description, so the pointer to the section 7 permission
# and third-party notices (both also copied into /app) cannot live there.
LABEL io.dataguard.licence-notices="/app/ADDITIONAL-PERMISSIONS.md /app/THIRD-PARTY-NOTICES.md (Oracle ODP.NET and Microsoft SNI keep their own terms)"

ENTRYPOINT ["dotnet", "DataGuard.Cli.dll"]
