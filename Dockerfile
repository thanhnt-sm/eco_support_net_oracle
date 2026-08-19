# DataGuard.Cli — multi-arch build (linux/amd64 + linux/arm64).
# Pattern follows the official dotnet-docker samples:
#   https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile
#
# The build stage runs natively on the builder platform (BUILDPLATFORM) and
# cross-compiles the app for TARGETARCH via `dotnet publish -a $TARGETARCH`.
# The final stage contains no RUN steps, so per-platform images are assembled
# from the multi-arch runtime base without emulation.

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILDPLATFORM
# Release version to bake into the binary (e.g. 1.2.3); the csproj files
# hardcode 0.1.0-alpha.1 which would otherwise end up in the image.
ARG VERSION=0.1.0-ci
WORKDIR /source

# Copy only project files first for optimal layer caching on restore.
# Directory.Build.props is auto-imported by MSBuild — it MUST be present
# during restore so the restore graph matches the publish graph.
COPY --link Directory.Build.props .
COPY --link src/DataGuard.Core/DataGuard.Core.csproj src/DataGuard.Core/
COPY --link src/DataGuard.Analyzers/DataGuard.Analyzers.csproj src/DataGuard.Analyzers/
COPY --link src/DataGuard.SqlServer.Adapter/DataGuard.SqlServer.Adapter.csproj src/DataGuard.SqlServer.Adapter/
COPY --link src/DataGuard.Oracle.Adapter/DataGuard.Oracle.Adapter.csproj src/DataGuard.Oracle.Adapter/
COPY --link src/DataGuard.MySql.Adapter/DataGuard.MySql.Adapter.csproj src/DataGuard.MySql.Adapter/
COPY --link src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj src/DataGuard.PostgreSql.Adapter/
COPY --link src/DataGuard.Cli/DataGuard.Cli.csproj src/DataGuard.Cli/

# Restore the CLI project, not the whole solution: DataGuard.sln also lists
# the test projects, which are intentionally not part of the image build and
# would make the restore fail (MSB3202). Project-level restore pulls in all
# ProjectReferences (Core, adapters, analyzers) transitively.
RUN dotnet restore src/DataGuard.Cli/DataGuard.Cli.csproj

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
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

# Non-root user baked into .NET 9 runtime images (UID 1654).
USER $APP_UID

COPY --link --from=build /app/publish .

LABEL org.opencontainers.image.source="https://github.com/thanhnt-sm/eco_support_net_oracle"
LABEL org.opencontainers.image.description="DataGuard CLI — contract validation for Entity to Stored Procedure/Raw SQL"

ENTRYPOINT ["dotnet", "DataGuard.Cli.dll"]