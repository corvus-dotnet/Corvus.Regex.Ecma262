#!/usr/bin/env bash
# The measured benchmark run: every class, .NET's engines beside this library, on the pinned cores.
#
#   tools/benchmarks/measure.sh <output directory> [filter]
#
# Before it runs: the WSL VM is pinned to the P-cores on the host ((Get-Process vmmemWSL).ProcessorAffinity = 0xFFF
# in an elevated PowerShell, again after every wsl --shutdown), the load is below 1.5, and a few minutes have passed
# since the last build. The run is out of process (BenchmarkDotNet's default toolchain), with the default job, under
# taskset on cores 0 to 11. The summary is written by summarize.py from the result files.
set -euo pipefail
here="$(cd "$(dirname "$0")/../.." && pwd)"
out="${1:?output directory}"
filter="${2:-*}"
mkdir -p "$out"
load="$(cut -d' ' -f1 /proc/loadavg)"
if awk "BEGIN { exit !($load >= 1.5) }"; then
  echo "load is $load; wait for it to fall below 1.5" >&2
  exit 1
fi
echo "load $load, cores $(taskset -cp $$ | sed 's/.*: //'), $(date -u +%FT%TZ)" | tee "$out/host.txt"
cd "$here/Solutions/Corvus.Regex.Ecma262.Benchmarks"
MSBUILDDISABLENODEREUSE=1 dotnet build -c Release -m:4 -nr:false >/dev/null
echo "built; settling for 3 minutes" | tee -a "$out/host.txt"
sleep 180
taskset -c 0-11 ./bin/Release/net10.0/Corvus.Regex.Ecma262.Benchmarks --filter "$filter" --artifacts "$out" --memory 2>&1 | tee "$out/run.log" | grep -E '^// \*\*\* |^// Benchmark: ' || true
echo "done $(date -u +%FT%TZ)" | tee -a "$out/host.txt"
