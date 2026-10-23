#!/usr/bin/env bash
# The runnable harness of the results contract: builds this library's benchmarks, runs the Industry workloads for the
# five .NET-side engines and V8 (Node 24) under the interleaved protocol of every harness of the central benchmark page
# (Corvus.Regex.Ecma262.Benchmarks industry, Driver.cs; its numbers in tools/benchmarks/protocol.sh), runs the ECMA-262
# test data for the compliance figures (this library's and V8's), and writes the run as one data file in the
# results-page shape.
#
#   tools/benchmarks/run.sh <workloads.json> <texts dir> <out.json> [<test data dir>] [short|default]
#
# short is a quick look (JOB=short of protocol.sh); ROUNDS, SECONDS_EACH, PASSES and SPREAD set the protocol's numbers,
# and CORES a taskset core list for the timed run.
#
# The texts are the .txt.gz files the benchmark project downloads (Inputs.cs); they are linked into the directory the
# project reads. The test data is cloned at depth 1 when no directory is given. industry.md and industry-run.json are
# written beside <out.json>. The harness image (Dockerfile) runs this script, and the central benchmark site runs
# that image on its own runner, beside every other engine's.
set -euo pipefail
workloads="$(realpath "$1")"; texts="$(realpath "$2")"; out="$(realpath -m "$3")"; data="${4:-}"; job="${5:-default}"
[ "$job" = short ] && export JOB=short
. "$(cd "$(dirname "$0")" && pwd)/protocol.sh"
timed() { if [ -n "${CORES:-}" ]; then taskset -c "$CORES" "$@"; else "$@"; fi; }
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
# COUNT_GATE=1 (the harness-image job): every workload counted by this library's engines beside V8, one round of the
# least timing, each count checked against V8's by count-gate.py; nothing else runs.
if [ "${COUNT_GATE:-0}" = 1 ]; then
  engines=corvus-utf16,corvus-utf8,corvus-generated-utf16,corvus-generated-utf8
  ( cd "$here/Solutions/Corvus.Regex.Ecma262.Benchmarks" && CORVUS_REGEX_TEXTS="$texts" ./bin/Release/net10.0/Corvus.Regex.Ecma262.Benchmarks \
      industry "$workloads" --v8 "$here/tools/benchmarks/v8/industry.mjs" --engines "$engines,v8" --rounds 1 --seconds 0.01 \
      --passes 1 --out "$work/counts.json" 2> "$work/counts.log" ) || true
  python3 "$here/tools/benchmarks/count-gate.py" "$workloads" "$work/counts.json" ${engines//,/ }
  exit $?
fi

echo "== the ECMA-262 test data (interpreted, both encodings)"
if [ -z "$data" ]; then git clone --quiet --depth 1 https://github.com/corvus-dotnet/ecma262-regex-test-data.git "$work/data"; data="$work/data/data"; fi
# The runner exits 1 on any answer that is not right; here its counts are a figure of the page, not a gate, so neither
# that nor a runner that fails stops the timing: an engine with no counts line is shown with no compliance figure.
compliance="$( { dotnet "$here/Solutions/Corvus.Regex.Ecma262.DatasetRunner/bin/Release/net10.0/Corvus.Regex.Ecma262.DatasetRunner.dll" "$data" "$work/wrong.tsv" || true; } | tail -1)"
echo "$compliance"
case "$compliance" in records*) ;; *) echo "WARNING: the data-set runner gave no counts line; this library's engines will have no compliance figure" ;; esac
# V8 judged as every engine is, over UTF-16, its one encoding (tools/benchmarks/v8/dataset.mjs).
compliance_v8="$( { node "$here/tools/benchmarks/v8/dataset.mjs" "$data" || true; } | tail -1)"
echo "$compliance_v8"
case "$compliance_v8" in records*) ;; *) echo "WARNING: V8's data-set run gave no counts line; V8 will have no compliance figure" ;; esac

echo "== the .NET and Corvus engines and V8 (Node $(node --version)), interleaved: $rounds rounds of $seconds s, $passes passes"
( cd "$here/Solutions/Corvus.Regex.Ecma262.Benchmarks" && CORVUS_REGEX_TEXTS="$texts" timed ./bin/Release/net10.0/Corvus.Regex.Ecma262.Benchmarks industry "$workloads" \
    --v8 "$here/tools/benchmarks/v8/industry.mjs" --engines corvus-utf16,corvus-generated-utf16,corvus-generated-utf8,net-interpreter,net-generated,v8 \
    --rounds "$rounds" --seconds "$seconds" --passes "$passes" --spread "$spread" --out "$work/run.json" 2> "$work/run.log" ) || { tail -40 "$work/run.log"; exit 1; }
grep -E '^(SPREAD|COUNT) ' "$work/run.log" || true

echo "== the record"
# Inside the image there is no .git: the build passes the commit (CORVUS_REGEX_COMMIT).
commit="${CORVUS_REGEX_COMMIT:-$(git -C "$here" rev-parse --short HEAD 2>/dev/null || echo unknown)}"
# The data file is written first: whatever fails after it (the table, the summary), the figures still reach /out.
status=0
python3 "$here/tools/benchmarks/industry-results.py" --interleaved "$work/run.json" --commit "$commit" --date "$(date -u +%FT%TZ)" --host ci --out-dir "$work/out" --base-path "${CORVUS_REGEX_BASE_PATH:-}" --run-url "${CORVUS_REGEX_RUN_URL:-}" --compliance "$compliance" --compliance-v8 "$compliance_v8" >/dev/null || status=$?
cp "$work/run.json" "$outdir/industry-run.json"
[ -f "$work/out/industry.json" ] || { echo "the record step wrote no industry.json (exit $status)"; exit 1; }
[ "$status" = 0 ] || echo "WARNING: the record step failed (exit $status) after writing industry.json; the figures are kept"
cp "$work/out/industry.json" "$out"; [ -f "$work/out/industry.md" ] && cp "$work/out/industry.md" "$outdir/industry.md"
echo "wrote $out"
