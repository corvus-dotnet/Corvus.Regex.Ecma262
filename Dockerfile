# The benchmark harness as an image (the results contract, docs/plan/engines-for-every-port.md section 3): the
# library's benchmarks built in release, with Node 24 for the V8 rows, and tools/benchmarks/run.sh as the entry point.
#
#   docker run --rm -v $PWD/workloads.json:/workloads.json:ro -v $PWD/texts:/texts:ro -v $PWD/out:/out \
#     ghcr.io/corvus-dotnet/corvus-regex-ecma262-dotnet:latest
#
# writes /out/industry.json (and industry.md, industry-v8.json beside it). Mount the test data at /data to skip the
# clone; set JOB=short for a quick look.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
ARG NODE_VERSION=24.22.1
# The SHA-256s of node-v24.22.1-linux-x64.tar.xz and node-v24.22.1-linux-arm64.tar.xz, from
# nodejs.org/dist/v24.22.1/SHASUMS256.txt: the one for the architecture being built is taken.
ARG NODE_SHA256_X64=6c172a9ab6718f7b801fb5a7bd486af2ee5e32758086a04b84fcce2aa77095e6
ARG NODE_SHA256_ARM64=dbabf7a1cbf947e6059ffad2c27b80041db97dea1e553b62afdee7f13fca336e
RUN apt-get update && apt-get install -y --no-install-recommends curl git python3 ca-certificates xz-utils \
    && rm -rf /var/lib/apt/lists/* \
    && case "$(uname -m)" in x86_64) arch=x64 sum=$NODE_SHA256_X64 ;; aarch64) arch=arm64 sum=$NODE_SHA256_ARM64 ;; \
         *) echo "no Node is pinned for $(uname -m)"; exit 1 ;; esac \
    && curl -sSfL -o /tmp/node.tar.xz "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-$arch.tar.xz" \
    && echo "$sum  /tmp/node.tar.xz" | sha256sum -c - && tar -xJf /tmp/node.tar.xz -C /opt && rm /tmp/node.tar.xz \
    && ln -s "/opt/node-v${NODE_VERSION}-linux-$arch/bin/node" /usr/local/bin/node
WORKDIR /src
COPY . .
ENV MSBUILDDISABLENODEREUSE=1 DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true
RUN dotnet build Solutions/Corvus.Regex.Ecma262.Benchmarks/Corvus.Regex.Ecma262.Benchmarks.csproj -c Release -m:4 -nr:false \
    && dotnet build Solutions/Corvus.Regex.Ecma262.DatasetRunner/Corvus.Regex.Ecma262.DatasetRunner.csproj -c Release -f net10.0 -m:4 -nr:false
ARG COMMIT=unknown
ENV CORVUS_REGEX_BASE_PATH=/Corvus.Regex.Ecma262 CORVUS_REGEX_COMMIT=${COMMIT}
ENTRYPOINT ["/bin/bash", "-c", "tools/benchmarks/run.sh /workloads.json /texts /out/industry.json \"$( [ -d /data ] && echo /data || echo '' )\" \"${JOB:-default}\""]
