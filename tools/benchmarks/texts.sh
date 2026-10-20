#!/usr/bin/env bash
# Downloads the Industry benchmark texts (the files dotnet/performance's Perf.Regex.Industry uses) into a directory,
# skipping any already there: tools/benchmarks/texts.sh <dir>
set -euo pipefail
dir="${1:?directory}"; mkdir -p "$dir"
base="https://raw.githubusercontent.com/dotnet/performance/main/src/benchmarks/micro/libraries/System.Text.RegularExpressions/TestData"
for f in sherlock.txt.gz 3200.txt.gz mariomka.txt.gz opensubtitles-ru.txt.gz opensubtitles-zh.txt.gz; do
  [ -s "$dir/$f" ] || curl -sSfL -o "$dir/$f" "$base/$f"
done
ls -l "$dir" | tail -n +2 | awk '{print $5, $9}'
