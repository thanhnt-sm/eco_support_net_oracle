# Pinned by digest (Scorecard Pinned-Dependencies): act-latest is a floating
# tag. To re-resolve, query the registry API for the current
# Docker-Content-Digest of catthehacker/ubuntu:act-latest and update below.
FROM catthehacker/ubuntu:act-latest@sha256:c58e2b364da03b0c804c7d660f2ecbedf2f221a382b9baa0b344b0144780ff43

# setup-dotnet emits ::add-path::/usr/share/dotnet; keep the action runtime
# available even when act applies that path update to later steps.
RUN mkdir -p /usr/share/dotnet \
    && ln -sf /opt/acttoolcache/node/24.19.0/x64/bin/node /usr/share/dotnet/node \
    && ln -sf /opt/acttoolcache/node/24.19.0/x64/bin/npm /usr/share/dotnet/npm \
    && ln -sf /opt/acttoolcache/node/24.19.0/x64/bin/npx /usr/share/dotnet/npx
