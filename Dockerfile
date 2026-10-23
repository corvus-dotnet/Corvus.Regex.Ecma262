# The benchmark harness as an image (the results contract, docs/plan/engines-for-every-port.md section 3): the
# library's benchmarks built in release, with Node 24 for the V8 rows, and tools/benchmarks/run.sh as the entry point.
#
#   docker run --rm -v $PWD/workloads.json:/workloads.json:ro -v $PWD/texts:/texts:ro -v $PWD/out:/out \
#     ghcr.io/corvus-dotnet/corvus-regex-ecma262-dotnet:latest
#
# writes /out/industry.json (and industry.md, industry-v8.json beside it). Mount the test data at /data to skip the
# clone; set JOB=short for a quick look.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
ARG NODE_VERSION=24.22.0
# The SHA-256 of node-v24.22.0-linux-x64.tar.xz, from nodejs.org/dist/v24.22.0/SHASUMS256.txt.
ARG NODE_SHA256=cc2f601cee82d9a5a233302fe307abc600727035fd7d2c3a3571755989e95008
RUN apt-get update && apt-get install -y --no-install-recommends curl git python3 ca-certificates xz-utils \
    && rm -rf /var/lib/apt/lists/* \
    && curl -sSfL -o /tmp/node.tar.xz "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz" \
    && echo "${NODE_SHA256}  /tmp/node.tar.xz" | sha256sum -c - && tar -xJf /tmp/node.tar.xz -C /opt && rm /tmp/node.tar.xz \
    && ln -s "/opt/node-v${NODE_VERSION}-linux-x64/bin/node" /usr/local/bin/node
WORKDIR /src
COPY . .
ENV MSBUILDDISABLENODEREUSE=1 DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true
RUN dotnet build Solutions/Corvus.Regex.Ecma262.Benchmarks/Corvus.Regex.Ecma262.Benchmarks.csproj -c Release -m:4 -nr:false \
    && dotnet build Solutions/Corvus.Regex.Ecma262.DatasetRunner/Corvus.Regex.Ecma262.DatasetRunner.csproj -c Release -f net10.0 -m:4 -nr:false
ARG COMMIT=unknown
ENV CORVUS_REGEX_BASE_PATH=/Corvus.Regex.Ecma262 CORVUS_REGEX_COMMIT=${COMMIT}
ENTRYPOINT ["/bin/bash", "-c", "tools/benchmarks/run.sh /workloads.json /texts /out/industry.json \"$( [ -d /data ] && echo /data || echo '' )\" \"${JOB:-default}\""]
