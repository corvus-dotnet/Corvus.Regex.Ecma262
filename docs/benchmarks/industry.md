# Benchmarks: the Industry workloads

The library is measured against `System.Text.RegularExpressions` and against V8 on the Industry workload set of
[dotnet/performance](https://github.com/dotnet/performance) (`Perf.Regex.Industry`): the Sherlock Holmes text and
the Rust `sherlock` patterns, the Leipzig corpus searches, the mariomka email, URI and IPv4 patterns, and the rebar
name searches over Russian and Chinese subtitles. These are the sets behind .NET's own "regex performance
improvements in .NET N" posts, so the figures can be read beside theirs.

## The figures

The figures are published by the daily run, every engine in one job on one GitHub-hosted runner each night, so that
the figures of a run are comparable with each other:

- **[The results page](https://corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/ci/)**: pick the engines and the
  measure, and read the leaderboard, the matrix of every workload, one workload at a time with its spread, or two
  engines head to head. Its header says which commit, which run and which runner.
- **[Every day's data](https://corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/ci/runs/)**: the page's data file for
  each day, named by date and commit, so a change can be read against the days before it.

No figure is repeated here: a table in a document goes stale the night after it is written, and the page does not.

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

The Corvus and V8 rows run under the `u` flag, which .NET has no mode for, so the two read a few rows by different
rules: counts agree with .NET on 53 of the 57 workloads, and the four apart are `$` and `.` at CRLF, where ECMA-262
counts CR as a line terminator, and `\w`, which .NET takes as Unicode letters and digits.

The bar is the mean of BenchmarkDotNet's default job and the whisker one standard deviation of its iterations; a
row's ratio is read against its neighbours' rather than to the last few percent. Allocated is the bytes the
benchmark allocated per operation; the library allocates nothing per match once its working memory has grown.

## Where this library is slower than .NET, and why

Four kinds of row read above 1.0 against .NET's generated code, each the price of an ECMA-262 rule that .NET does
not apply:

1. **`.` and `$` at CRLF.** ECMA-262's `.` stops at CR as well as LF, so over a text with CRLF endings `.*` finds
   36,491 matches to .NET's 26,105, at parity per match.
2. **Case-insensitive patterns under the u flag.** The search must find the long s and the Kelvin sign as forms of
   `s` and `k`, which .NET's search ignores, so its tables carry every form (`Sher[a-z]+|Hol[a-z]+` with `i`).
3. **A class that admits a supplementary character before a literal at a fixed distance.** Such a character is two
   units, so the distance is not fixed and the literal is found by a walk back rather than at an offset
   (`[a-q][^u-z]{13}x`).
4. **Non-ASCII literals over UTF-8.** Every Cyrillic or Chinese character is two or three bytes with no rare byte
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
`tools/benchmarks/results-page/generate_site.py` renders the page; the daily workflow (`.github/workflows/benchmarks.yml`)
is the same two steps on the runner.
