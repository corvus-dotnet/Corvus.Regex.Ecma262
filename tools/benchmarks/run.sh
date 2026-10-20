#!/usr/bin/env bash
# The runnable harness of the results contract: builds this library's benchmarks, runs the Industry workloads for the
# five .NET-side engines (BenchmarkDotNet, the default job) and for V8 (Node 24), runs the ECMA-262 test data for the
# compliance figures, and writes the run as one data file in the results-page shape.
#
#   tools/benchmarks/run.sh <workloads.json> <texts dir> <out.json> [<test data dir>] [short|default]
#
# The texts are the .txt.gz files the benchmark project downloads (Inputs.cs); they are linked into the directory the
# project reads. The test data is cloned at depth 1 when no directory is given. industry.md and industry-v8.json are
# written beside <out.json>. The harness image (Dockerfile) runs this script, and the central benchmark site runs
# that image on its own runner, beside every other engine's.
set -euo pipefail
workloads="$(realpath "$1")"; texts="$(realpath "$2")"; out="$(realpath -m "$3")"; data="${4:-}"; job="${5:-default}"
here="$(cd "$(dirname "$0")/../.." && pwd)"
outdir="$(dirname "$out")"; mkdir -p "$outdir"
work="$(mktemp -d)"
export MSBUILDDISABLENODEREUSE=1 DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true

# The texts, where the benchmark project looks for them.
cache="${XDG_DATA_HOME:-$HOME/.local/share}/corvus-regex-benchmarks"; mkdir -p "$cache"
for f in "$texts"/*.txt.gz; do [ -e "$cache/$(basename "$f")" ] || ln -s "$f" "$cache/$(basename "$f")"; done

echo "== build"
dotnet build "$here/Solutions/Corvus.Regex.Ecma262.Benchmarks/Corvus.Regex.Ecma262.Benchmarks.csproj" -c Release -m:4 -nr:false >/dev/null
dotnet build "$here/Solutions/Corvus.Regex.Ecma262.DatasetRunner/Corvus.Regex.Ecma262.DatasetRunner.csproj" -c Release -f net10.0 -m:4 -nr:false >/dev/null

echo "== the .NET and Corvus engines (BenchmarkDotNet, $job job)"
( cd "$here/Solutions/Corvus.Regex.Ecma262.Benchmarks" && ./bin/Release/net10.0/Corvus.Regex.Ecma262.Benchmarks --filter '*GeneratedCount*' --job "$job" --artifacts "$work/run" > "$work/run.log" 2>&1 ) || { tail -40 "$work/run.log"; exit 1; }

echo "== the ECMA-262 test data (interpreted, both encodings)"
if [ -z "$data" ]; then git clone --quiet --depth 1 https://github.com/corvus-dotnet/ecma262-regex-test-data.git "$work/data"; data="$work/data/data"; fi
compliance="$(dotnet "$here/Solutions/Corvus.Regex.Ecma262.DatasetRunner/bin/Release/net10.0/Corvus.Regex.Ecma262.DatasetRunner.dll" "$data" "$work/wrong.tsv" | tail -1)"
echo "$compliance"

echo "== V8 (Node $(node --version))"
CORVUS_REGEX_TEXTS="$texts" node "$here/tools/benchmarks/v8/industry.mjs" "$workloads" 2 5 > "$work/v8.json" 2> "$work/v8.log"

echo "== the record"
# Inside the image there is no .git: the build passes the commit (CORVUS_REGEX_COMMIT).
commit="${CORVUS_REGEX_COMMIT:-$(git -C "$here" rev-parse --short HEAD 2>/dev/null || echo unknown)}"
python3 "$here/tools/benchmarks/industry-results.py" --run "$work/run" --commit "$commit" --date "$(date -u +%FT%TZ)" --v8 "$work/v8.json" --host ci --job "$job" --out-dir "$work/out" --base-path "${CORVUS_REGEX_BASE_PATH:-}" --run-url "${CORVUS_REGEX_RUN_URL:-}" --compliance "$compliance" >/dev/null
cp "$work/out/industry.json" "$out"; cp "$work/out/industry.md" "$outdir/industry.md"; cp "$work/v8.json" "$outdir/industry-v8.json"
echo "wrote $out"
