# The numbers of the interleaved protocol of every Corvus ECMA-262 engine's harness (corvus-regex-ecma262-rs,
# tools/benchmarks/protocol.sh), which tools/benchmarks/run.sh sources: ROUNDS rounds, each figure
# SECONDS_EACH seconds of PASSES passes in a process of its own, and SPREAD, the spread over which a figure is listed in
# the run's facts. JOB=short is a quick look (3 rounds of 0.1 s, one pass).
if [ "${JOB:-}" = short ]; then d_rounds=3 d_seconds=0.1 d_passes=1; else d_rounds=3 d_seconds=0.7 d_passes=7; fi
rounds="${ROUNDS:-$d_rounds}"
seconds="${SECONDS_EACH:-$d_seconds}"
passes="${PASSES:-$d_passes}"
spread="${SPREAD:-0.05}"
