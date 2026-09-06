FROM catthehacker/ubuntu:act-latest

# setup-dotnet emits ::add-path::/usr/share/dotnet; keep the action runtime
# available even when act applies that path update to later steps.
RUN mkdir -p /usr/share/dotnet \
    && ln -sf /opt/acttoolcache/node/24.19.0/x64/bin/node /usr/share/dotnet/node \
    && ln -sf /opt/acttoolcache/node/24.19.0/x64/bin/npm /usr/share/dotnet/npm \
    && ln -sf /opt/acttoolcache/node/24.19.0/x64/bin/npx /usr/share/dotnet/npx
