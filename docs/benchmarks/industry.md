# Benchmarks: the Industry workloads

**Corvus.Regex.Ecma262 is faster than `System.Text.RegularExpressions` almost everywhere, and it gives V8's answers
where .NET doesn't.** On the Industry workloads that .NET itself publishes figures for, a typical night looks like
this.

- The interpreter is at or below .NET's interpreter on all but three of the workloads they both answer, and takes
  about a third of the time at the median.
- The generated code is at or below .NET's generated code on about nine workloads in ten, over UTF-16 and over UTF-8
  alike. It takes about three quarters of the time at the median over UTF-16, and about three fifths over UTF-8.
- Both are far ahead of V8, which takes about seven times as long at the median.

The exact figures are on [the benchmark page](https://corvus-oss.org/corvus-regex-ecma262-benchmarks/), where every
engine is measured on one runner each night.

The library is measured against `System.Text.RegularExpressions` and against V8 on the Industry workload set of
[dotnet/performance](https://github.com/dotnet/performance) (`Perf.Regex.Industry`): the Sherlock Holmes text and
the Rust `sherlock` patterns, the Leipzig corpus searches, the mariomka email, URI and IPv4 patterns, and the rebar
name searches over Russian and Chinese subtitles. These are the sets behind .NET's own "regex performance
improvements in .NET N" posts, so the figures can be read beside theirs.

## The figures

The figures are on one page, [corvus-oss.org/corvus-regex-ecma262-benchmarks](https://corvus-oss.org/corvus-regex-ecma262-benchmarks/). Each night it runs every Corvus
ECMA-262 engine's published harness image, this library's among them, one after another on one runner, so every
figure on it was measured on the same machine in the same night. Pick the engines and the measure, and read the
leaderboard, the matrix of every workload, one workload at a time with its spread, or two engines head to head.
Each engine's ECMA-262 compliance is beside its name, its header says which release of each engine ran, and every
day's data is kept.

No table of figures is repeated here. A table in a document goes stale the night after it's written, and the page doesn't.

## What is measured

Six engines, each counting the matches of every workload over its whole text (`Count`, one call per operation):

| Engine | What it is |
|---|---|
| .NET interpreter | `System.Text.RegularExpressions` with `RegexOptions.None` |
| .NET generated | `System.Text.RegularExpressions` through `[GeneratedRegex]` |
| Corvus interpreter | this library's interpreter over UTF-16 |
| Corvus generated UTF-16 | this library's source generator over UTF-16 |
| Corvus generated UTF-8 | this library's source generator over the same text as UTF-8 bytes, nothing transcoded |
| V8 (Node 24) | Node's `RegExp`, the oracle of the test data, over the same texts, counted by a `test` loop that advances `lastIndex` as `matchAll` does (`tools/benchmarks/v8/`) |

The Corvus and V8 rows run under the `u` flag, which .NET has no mode for, so the two read a few patterns by
different rules. On three workloads .NET counts something other than V8 does: `.*` and the `$` of
`^Sherlock Holmes|Sherlock Holmes$` with `m`, where ECMA-262 counts CR as a line terminator and .NET doesn't, and
`\w+`, where .NET's `\w` takes letters and digits of every script. A count other than V8's is a wrong answer, so the
benchmark page shows .NET's figures on those three as failures (✕), with both counts in the tooltip. This library
counts as V8 does on every workload.

The figures on the benchmark page are measured under the interleaved protocol of every Corvus engine's harness
(`Corvus.Regex.Ecma262.Benchmarks industry`, `tools/benchmarks/run.sh`): every workload in turn, each in three rounds
of the engines in turn, their order turned each round, each figure a process of its own, warmed for at least 1.5 s
(.NET compiles a method again as it is used, and needs that long to reach its final code) and timed for 0.7 s in seven
passes. The bar is the median of the rounds and the whisker the fastest and the slowest round; a row's ratio is read
against its neighbours' rather than to the last few percent. The BenchmarkDotNet classes of the same project
(`Industry.cs`, `GeneratedIndustry.cs`) measure the same counts under the pinned-host procedure, with what each
allocates; the library allocates nothing per match once its working memory has grown.

## Where this library is slower than .NET, and why

Three kinds of row read above 1.0 against .NET's generated code. Each is the price of an ECMA-262 rule that .NET
doesn't apply.

1. **Case-insensitive patterns under the u flag.** The search must find the long s and the Kelvin sign as forms of
   `s` and `k`, which .NET's search ignores, so its tables carry every form (`Sher[a-z]+|Hol[a-z]+` with `i`).
2. **A class that admits a supplementary character before a literal at a fixed distance.** Such a character is two
   units, so the distance is not fixed and the literal is found by a walk back rather than at an offset
   (`[a-q][^u-z]{13}x`).
3. **Non-ASCII literals over UTF-8.** Every Cyrillic or Chinese character is two or three bytes with no rare byte
   among them, where UTF-16 gives the search one rare unit per character; so the UTF-8 rows for the Chinese names
   read above .NET over UTF-16 while the UTF-16 rows are level.

The other rows above 1.0 are within a few percent of parity and read either side of it from run to run: both engines
are at the floor of one vectorised search over the text.

V8 has no vectorised search for a literal, a set or an alternation beyond its first character, so the rows that such
a search decides read tens to hundreds of times slower there; the rows it wins are short fixed literals over its
one-byte strings.

## Running them yourself

A quick look at one row, in a few minutes:

```bash
cd Solutions/Corvus.Regex.Ecma262.Benchmarks
CORVUS_REGEX_WORKLOADS=aei dotnet run -c Release -- --filter '*GeneratedCount*' --job short
```

A run whose figures are to be compared with the page follows the pinned-host procedure of the
[design document](../design/design.md) (section 9): the machine pinned to its performance cores, a settle after the
last build, nothing else running, the run under `taskset`. `tools/benchmarks/industry-results.py` then writes a
page's data and table from the run's artifacts (`--host pinned`, with the probe readings), and
`tools/benchmarks/results-page/generate_site.py` renders the page. `tools/benchmarks/run.sh` is the same steps as
one script, and the harness image (`Dockerfile`, published by `.github/workflows/harness-image.yml`) runs it; that
image is what the benchmark page runs each night.
