# Corvus.Regex.Ecma262: design

Status: agreed 2026-10-18 (the owner took every fork; the answers are folded in below). Each numbered section ends
with a **Decision** line.

## 1. What it is

A regular expression library for .NET whose grammar and answers are those of ECMA-262 (ECMAScript 2025), with the
public shape of `System.Text.RegularExpressions`, so that a project moves to it by changing a `using` directive. It
includes a build-time source generator with the shape of `[GeneratedRegex]`. It matches UTF-8 text natively, in
addition to UTF-16, and a match allocates nothing once the per-thread working memory has grown.

It is its own library in its own repository, `corvus-dotnet/Corvus.Regex.Ecma262`, so that it can be used anywhere.
Corvus.JsonSchema is its first intended consumer, but not until the gate in section 2 is met.

**Decision.** Standalone repository and NuGet package. Corvus.JsonSchema is a consumer, not the home.

## 2. The adoption gate

When the library is proved as fast or faster than `System.Text.RegularExpressions` on general regular expression
workloads, Corvus.JsonSchema replaces its ECMA-262 to .NET translator (`EcmaRegexTranslator`, the
`(?#interpreted)` routing, the shape matchers that exist only to keep patterns away from .NET's engine, and issue
#1025) with this library. Until then, nothing in Corvus.JsonSchema changes.

The comparison is against the .NET engine, not against the JSON Schema corpora. "As fast or faster" is measured by
the benchmarks of section 9: on the industry set (the patterns and texts .NET itself publishes numbers for), the
median of the ratio (this library over .NET) is at or below 1.0 for the same engine class (interpreted against
interpreted, generated against generated), and 0 bytes are allocated per match. The pathological set must finish
where .NET times out. At adoption time the JSON Schema corpora are a conformance check (the same answers, nothing
slower for the evaluator as a whole), not the benchmark.

**Decision.** The gate is numeric, per engine class, against the .NET engine on general workloads, run on a pinned
host with the procedure Corvus.JsonSchema already uses for A/B measurement.

## 3. What is ported, and from where

Four Corvus ports already have their own ECMA-262 engine (Go, Rust, Object Pascal, OCaml), one source with four
copies, and the C# translator has a parser of its own. Nothing is rebuilt that one of them has:

| Piece | Source | Notes |
|---|---|---|
| Parser, syntax tree, both grammars (u flag; Annex B without), group names, modifier groups | `src/Corvus.Text.Json.CodeGeneration/EcmaRegexParser.cs` (1,670 lines, C#) | Already C#, already netstandard2.0, already Unicode 17 and ECMAScript 2025. Taken as the base. |
| Unicode property tables, case folding (Canonicalize for both modes), sets | `EcmaRegexUnicode.cs`, `EcmaRegexUnicodeData.cs` (generated) and the Go `ucd` package | The generator script `gen-ecma-unicode-data.ps1` moves with them. |
| Compiler (tree to flat program), instruction set | `src-go/.../ecmaregex/compile.go` and `src-rs/.../ecmaregex/compile.rs` | `opChar`, `opSet`, `opLit`, anchors, `opSplit`, captures, `opLook*`, `opRep`, `opLoop*`, backreferences in four forms. |
| Backtracking matcher: explicit stack, memo of failed places, counted loops with `needs`, `quick` first sets, time and memory limits | `vm.go` / `vm.rs` | The Rust form (per-thread state, retained caps) is the closer model for .NET. |
| ASCII automaton for linear patterns | `dfa.go` / `dfa.rs` | 256 states, built on first use. |
| Matching by UTF-16 code unit over UTF-8 text | `units.go` / `units.rs` | The position between the halves of a four-byte character is the offset of its third byte. |
| Resource limits | `parse.rs` limits, `re2.rs` cost model | Pattern length, nesting, program size, construction cost, match time, backtracking memory. |

Everything is copied with its file-level provenance recorded, then changed where .NET needs it. Both repositories are
Apache-2.0 and the same owner's.

**Decision.** Port, do not rebuild. The C# parser is the base for the front end; the Go/Rust engine for the back end.

## 4. Public surface

### 4.1 Names

The types are named as .NET names them: `Regex`, `Match`, `Group`, `Capture`, `MatchCollection`, `GroupCollection`,
`CaptureCollection`, `MatchEvaluator`, `ValueMatch`, `GeneratedRegexAttribute`, `RegexParseException`,
`RegexMatchTimeoutException`. The options type is not `RegexOptions`: it is `RegexFlags`, the flags of ECMA-262
(section 4.2). Moving a project to the library is a change of `using` plus a change of its options, which the
owner accepted as a break in source compatibility.

The namespace cannot contain a segment named `Regex`. C# resolves a simple name by walking the enclosing namespaces
before it reads the `using` directives of the compilation unit, so inside `namespace Corvus.Text.Json` the name
`Regex` would bind to the namespace `Corvus.Regex` and not to the type, and Corvus.JsonSchema itself could not use
it. Any consumer in a `Corvus.*` namespace would hit the same.

**Decision.** Package id `Corvus.Regex.Ecma262`; namespace `Corvus.Ecma262.RegularExpressions`, which mirrors
`System.Text.RegularExpressions` and has no `Regex` segment.

### 4.2 Flags

The options are ECMA-262's flags and nothing else: a `[Flags] enum RegexFlags` with one member for each flag of
`RegExp`, and a constructor that also takes the flags as the string `new RegExp(pattern, flags)` takes.

| Member | Flag | Meaning |
|---|---|---|
| `HasIndices` | `d` | Accepted for completeness. Indices are always kept, so it changes nothing. |
| `Global` | `g` | `Replace` replaces every match; without it, the first, as `String.prototype.replace` does. |
| `IgnoreCase` | `i` | Canonicalize of the mode in force (simple case folding with `u` or `v`, `toUpperCase` without). Never the culture. |
| `Multiline` | `m` | `^` and `$` at line terminators. |
| `DotAll` | `s` | The dot matches line terminators. |
| `Unicode` | `u` | The u flag grammar; matched by code point. |
| `UnicodeSets` | `v` | Set notation and properties of strings; matched by code point. `u` and `v` together is a syntax error, as in ECMA-262. |
| `Sticky` | `y` | A match only at the start position. |

`RegexFlags.None` is `new RegExp(pattern)` with no flags: the Annex B grammar and matching by UTF-16 code unit,
which is what ECMA-262 does with no flag.

Where ECMA-262 keeps `lastIndex` on the `RegExp` object, the library takes the position as an argument, as .NET
does: `Match(input, startAt)` is `exec` with `lastIndex` set to `startAt`, and `Sticky` anchors it there. There is
no mutable state on a `Regex`, so an instance is safe to share between threads, as .NET's is. `Matches`,
`EnumerateMatches` and `Count` are the global search whether or not `Global` is set (ECMA-262's `matchAll` throws
without `g`; a method named `Matches` does not need the flag to say what it does). `Split` ignores `Global`, as
`RegExp.prototype[Symbol.split]` does.

JSON Schema's reading (u flag first, Annex B when that refuses) is the caller's: two calls of the non-throwing
`Regex.TryCreate(pattern, flags, out Regex? regex, out string? error)`, which costs no exception. `Regex.Flags`
says which flags are in force.

**Decision.** ECMA-262's flags, by name and by string. `None` means no flags. The u-first reading is not a default
of the library.

### 4.3 Errors, and the members whose algorithm is ECMA-262's

A pattern that is not valid in the grammar of its flags throws `RegexParseException` with the ECMA-262 message and
the offset. A pattern over a limit of size throws a distinct `RegexPatternTooLargeException` (an
`ArgumentException`), never a parse error: a caller must be able to tell "not valid" from "too large".

`Regex.Replace` substitutes as ECMA-262's `GetSubstitution` does: `$$`, `$&`, `` $` ``, `$'`, `$n`, `$nn`, `$<name>`.
.NET's `$_`, `$+` and `${name}` are not recognised and are left as they are, as ECMA-262 leaves them. `Regex.Split`
follows `RegExp.prototype[Symbol.split]`: captures are spliced into the result, an empty match at the start or the
end of the text splits nothing, and `count` is ECMA-262's `limit`. `Regex.Escape` escapes the ECMA-262 syntax
characters. `Regex.Unescape` reverses `Escape`. Nothing of .NET's that has no ECMA-262 meaning (right-to-left
matching, pattern whitespace, explicit capture, the culture) exists in the surface, so nothing has to be refused
at run time.

**Decision.** Where .NET and ECMA-262 differ in an algorithm, ECMA-262's is used; what has no ECMA-262 meaning is
absent.

### 4.4 Input and positions

Every matching member has an overload for `string`, `ReadOnlySpan<char>` and `ReadOnlySpan<byte>` (UTF-8). A
`Match` from a string input holds the input and gives `Value` as a substring, as .NET does. A `Match` from a span is
not possible in .NET's shape (a class cannot hold a span), so the span overloads are the ones .NET itself gives spans:
`IsMatch`, `Count`, `EnumerateMatches` (`ValueMatch` with `Index` and `Length`), and a new `EnumerateSplits`. For
captures over a span there is a new `ValueMatchEnumerator` form that reports each group's `(Index, Length)` or
unset, with no allocation. `Index` and `Length` are in the unit of the input: chars for UTF-16, bytes for UTF-8.

A UTF-8 input that is not valid UTF-8 is read a byte at a time as U+FFFD, as the Go engine reads it. A UTF-16 input
can hold a lone surrogate, and it is matched as ECMA-262 matches it (a code unit without `u`, a code point of its
own with `u`). Nothing is transcoded: there is no UTF-16 copy of a UTF-8 text and no UTF-8 copy of a string.

**Decision.** Both encodings are native. No overload transcodes.

### 4.5 Limits

A `matchTimeout` as in .NET, default `Regex.InfiniteMatchTimeout` for the drop-in, settable per instance and by the
`REGEX_DEFAULT_MATCH_TIMEOUT` `AppContext` switch .NET reads, so a consumer's existing configuration applies. A
match that passes it throws `RegexMatchTimeoutException`. A match whose backtracking stack reaches its cap throws
`RegexMatchTimeoutException` too, with a message that says memory, because .NET has no exception for that and a
consumer's `catch` must already hold the case. Limits of size on the pattern (length, nesting, program size,
construction cost) are those of the Go/Rust engine and are deterministic: never a clock.

**Decision.** .NET's timeout shape and configuration; the engine's deterministic size limits.

## 5. Engine design

### 5.1 One matcher, two encodings, no copy

The matcher's methods are generic over the unit of the text and an empty struct that reads it:
`Run<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, ...) where TEncoding : struct, ITextEncoding<TUnit>`, with
`Utf8Encoding : ITextEncoding<byte>` and `Utf16Encoding : ITextEncoding<char>`. Each encoding gives `Decode`,
`DecodeBefore`, `UnitAt` and `UnitBefore` (the UTF-16 code unit at a position, for Annex B matching), the line
terminator tests, the literal and prefix searches. The JIT specialises every generic method for the struct, so
there is no virtual call and no branch on the encoding in the loop. (A `ref struct` cursor that holds the span and
implements the interface would read the same, but ref-struct interfaces need a .NET 9 or later runtime, and the
netstandard2.0 build must compile the same source.) The Go engine's `units` trick, which places the position between the halves of a
supplementary character at its third byte, is the UTF-8 cursor's code-unit mode. The UTF-16 cursor's code-unit mode
is the natural one.

Literal runs are stored in both encodings at compile time (a few bytes each), so `opLit` is a span comparison and,
for a literal prefix, a vectorised `IndexOf`.

### 5.2 Program and working memory

The program is flat arrays (instruction kind, operands, set tables) in a sealed class the `Regex` holds. The
per-thread working memory (backtracking stack, capture slots, loop counters, memo bitset) is a `[ThreadStatic]`
state, grown to what a match needs and retained up to a cap, as the Rust port does. A small match stackallocs its
capture slots. No closures, no boxing, no LINQ, no iterator blocks on the match path. The clock is read every 4,096
steps, as the ports do, so a match that ends before the first check never reads it.

### 5.3 Finding where to start

Before the matcher runs, the `Regex` applies what it learned at compile time, in this order: an anchored pattern
starts only at 0; a required literal prefix is found by `IndexOf`; a first set is found by `SearchValues<byte>` /
`SearchValues<char>` (net8.0 and later; a table on netstandard2.0); a minimum length rules out short rests. This is
the part of .NET's `RegexFindOptimizations` that applies to ECMA-262 patterns, and it is where most of the time of
a search on a long text goes.

### 5.4 Linear patterns

The ASCII automaton of the ports (256 states, lazy) is kept for both cursors: an ASCII text is one table read per
unit either way, and any other unit hands the text to the matcher. A lazy automaton over full Unicode is a later
phase (section 10) if the benchmarks show it is needed.

### 5.5 The source generator

A Roslyn incremental generator with the shape of .NET's: `[GeneratedRegex(pattern, options, matchTimeoutMilliseconds)]`
on a `static partial Regex Name();` method or `partial Regex Name { get; }` property. It ships inside the same
package as an analyzer asset, so one `PackageReference` gives the library and the generator, as the .NET SDK does.

It has two stages, each a gate of its own:

1. **Baked program.** The generator parses and compiles the pattern at build time and emits the program's arrays,
   the prefilter data and the automaton as `static readonly` data, plus the diagnostics (a pattern that is not valid
   is a build error with the ECMA-262 message, as .NET reports one). No parse at run time, trimming and AOT safe.
2. **Straight-line code.** For patterns in the linear class and for the simple backtracking shapes, the generator
   emits C# that matches directly (loops over the span, no instruction dispatch), which is what makes .NET's own
   generator fast. This is the stage the generated-against-generated comparison of section 2 is measured on.

**Decision.** Generator inside the main package. The alternative, a separate `*.SourceGenerator` package as
Corvus.Text.Json has, would make the swap two references instead of one.

## 6. Target frameworks

`net10.0; netstandard2.0`. net8.0 leaves support in November 2026 and net9.0 left it in May 2026, so neither is a
target. netstandard2.0 is needed twice: the generator is a netstandard2.0 Roslyn component that compiles the same
parser and compiler source, and Corvus.Text.Json targets netstandard2.0 and tests on net481. The SDK is pinned to
10.0 with `rollForward: latestFeature`; a net11.0 target is added when that SDK is generally available.

**The modern target never pays for netstandard2.0** (owner, 2026-10-18: "Do not sacrifice .NET 11 performance for
netstandard2.0 compatibility. Consider conditional compilation."). Wherever the current runtime has a faster way,
the code takes it under `#if NET` and netstandard2.0 gets a plain fallback, never the other way round:
`SearchValues` and `IndexOfAny` for first sets, the vectorised span `IndexOf` for prefixes, `Vector128` scans for
ASCII class loops, static abstract interface members and `allows ref struct` where they remove a cost, `Unsafe`
reads where bounds checks show in a profile. The benchmarks of section 9 run on the modern target; the
netstandard2.0 build is held to correctness (the same data set) and not to the same speed.

**Decision.** As above.

## 7. Repository layout

The layout of `corvus-dotnet/Corvus.Globbing`, the closest sibling (a single high-performance library benchmarked
against .NET Regex):

```
Corvus.Regex.Ecma262/
  .github/workflows/build.yml          scripted-build-matrix-pipeline (Endjin.RecommendedPractices.GitHubActions)
  .github/workflows/auto_release.yml   from pr-autoflow
  .github/workflows/dataset.yml        the full ECMA-262 test data run (section 8), nightly and on demand
  GitVersion.yml, LICENSE (Apache-2.0), README.md, build.ps1, global.json
  docs/design/design.md                this document
  docs/benchmarks/                     committed result tables with the host and the date
  Solutions/
    Corvus.Regex.Ecma262.sln
    Corvus.Regex.Ecma262/              the library; the generator's shared source is linked from here
    Corvus.Regex.Ecma262.SourceGenerator/   netstandard2.0, packed into the library package as an analyzer
    Corvus.Regex.Ecma262.Tests/        MSTest, Microsoft.Testing.Platform, as Corvus.JsonSchema
    Corvus.Regex.Ecma262.SourceGenerator.Tests/
    Corvus.Regex.Ecma262.Benchmarks/   BenchmarkDotNet (section 9)
    Corvus.Regex.Ecma262.DatasetRunner/  console: the test data run, V8 differential fuzzer harness
  tools/unicode/gen-ecma-unicode-data.ps1   regenerates the tables from UCD
```

Default branch `main`. The owner adds the repository to endjin-codeops pr-autoflow, which generates GitVersion.yml
and auto_release.yml; until then a hand-written GitVersion.yml in the same shape is used.

**Decision.** The repository `corvus-dotnet/Corvus.Regex.Ecma262` was created on 2026-10-18 (public, Apache-2.0).
pr-autoflow is not a concern of the owner's for now; `GitVersion.yml` is hand-written in the same shape.

## 8. Testing and the ECMA-262 test data

### 8.1 What the data already gives

`corvus-dotnet/ecma262-regex-test-data` holds V8's verdicts for 98,839 patterns (validity under both grammars) and
`RegExp.prototype.test` answers for 4,322,391 texts, with the `i`, `m` and `s` flags in `flags.jsonl` and
`case-folding.jsonl`. That is the gate for `IsMatch` under `None`, `Unicode`, `IgnoreCase`, `Multiline` and
`Singleline`, and for validity, as it is today for every port. The `lone-surrogates.jsonl` family, which no JSON
Schema port is held to, is a gate for this library on UTF-16 input, because ECMA-262 defines those answers.

### 8.2 What the library needs that the data lacks

The drop-in surface answers more than "does it match anywhere". The data set is extended, in its own repository and
by its own generator, with records for:

| Extension | Record | For |
|---|---|---|
| Captures | `exec`: the match's `[start, end]` and each group's `[start, end]` or `null`, under each valid mode | `Match`, `Groups`, `EnumerateMatches`, indices in both units |
| Global search | the sequence of `exec` results of a `g` loop with the ECMA-262 `lastIndex` advance (an empty match advances by one code unit, or one code point with `u`) | `Matches`, `Count`, `EnumerateMatches` |
| Sticky | `exec` with `y` at a set of start positions | `Sticky` |
| Replace | the result of `replace` and `replaceAll` for a small set of replacement strings exercising every `$` form | `Replace` |
| Split | the result of `split` with and without a limit | `Split`, `EnumerateSplits` |
| v flag | a new family of set-notation and string-property patterns: validity and `test` | `UnicodeSets` |
| Named groups | `groups` of the `exec` result, duplicates in separate alternatives included | `Groups[name]` |

The first five are one new file, `captures.jsonl` (each test holds `exec`, `all`, `sticky`, `replace`, `replaceAll`,
`split` and `split3`), and the v flag is a file of its own, so the existing files and their consumers are untouched.
A test on which V8 departs from ECMA-262 (a match that starts inside a surrogate pair) or answers unstably is marked
on the test, as the other files mark records. Positions are given in UTF-16 code units, as V8 gives them; the
runner derives the UTF-8 byte positions from the text. The generator scripts (`generate.js`, `families.js`) grow
the record kinds; `verify.js` checks them; `manifest.json` lists the counts. The size of the data is kept in view:
the capture file samples texts rather than repeating all 4.3 million.

### 8.3 How the tests read the data

The data set is 230 MB and grows. It is not vendored. The data repository gets release tags; the engine repository's
`dataset.yml` job downloads the tagged archive into a cached directory and runs the `DatasetRunner` over it, both
encodings, both engine classes, with 0 wrong as the pass. A checked-in sample (one record in a hundred, fixed seed,
about 2 MB) runs in the ordinary `dotnet test` so that a developer gets a signal in seconds. The sample is
regenerated by a script from the tagged data, never edited by hand.

### 8.4 The other tests

- Unit tests ported from the Go `ecmaregex_test.go`, `size_limit_test.go`, `match_limit_test.go` and `memo_test.go`,
  and from the C# translator's parser tests.
- Allocation tests that count the measuring thread only, with a `[MemoryDiagnoser]` benchmark at 0 B as the proof.
- A V8 differential fuzzer (Node is on the development machine) as an outerloop test; a class it finds goes into the
  data set, never left in scratch.
- Source generator tests on the Roslyn test harness: generated code compiles, equals the interpreter's answers on the
  sample, and diagnostics match .NET's for the same faults.
- A swap test: a small project that compiles against `System.Text.RegularExpressions` and against this library by
  changing one `using`, to hold the drop-in promise.

**Decision.** The data set is the oracle; it is extended in its own repository with new files, and consumed by tag.

## 9. Benchmarks

The benchmarks compare this library with `System.Text.RegularExpressions` on the workloads the .NET team itself
measures and publishes, so the numbers can be read beside theirs. The JSON Schema corpora are not a benchmark set.

One BenchmarkDotNet project, `[MemoryDiagnoser]` on everything, with a job for each engine class on each side:

| Side | Classes |
|---|---|
| .NET | interpreter, `RegexOptions.Compiled`, `RegexOptions.NonBacktracking` where the pattern allows, `[GeneratedRegex]` |
| This library | interpreter, generated (baked program), generated (straight-line code), each over `string` and over UTF-8 |

Pattern sets, each a file in the benchmark project with its provenance:

1. **Industry.** The sets of `dotnet/performance`'s `Perf.Regex.Industry`: the Mariomka set (email, URI, IPv4 over
   its text), regex-redux, the Rust `sherlock` patterns over the Sherlock Holmes text, the Boost documentation set,
   the Leipzig corpus searches, and `slice-slice`. These are the sets behind .NET's own "regex performance
   improvements in .NET N" tables. This is the set the adoption gate of section 2 is measured on.
2. **Common.** `Perf.Regex.Common` and `Perf.Regex.Cache` from the same repository: construction, the `IsMatch`,
   `Match`, `Matches`, `Replace` and `Split` members, `Count`, `EnumerateMatches`, on their patterns.
3. **Pathological.** Nested quantifiers, overlapping alternatives under a repetition, backreferences in loops, on
   texts made for them: the set where .NET reaches its timeout and the engine of the ports does not.
4. **Micro.** One construct at a time (a class loop, a literal search, a lookbehind, a bounded loop, a UTF-8 text
   against a UTF-16 one) to find where a loss comes from.

Where an industry pattern uses syntax ECMA-262 lacks (an atomic group, a possessive quantifier, `\A` and `\z`,
a `(?i)` prefix), the benchmark holds the ECMA-262 form of the same expression and says so beside it; a pattern
with no ECMA-262 form is left out and listed.

Runs follow the pinned-host procedure Corvus.JsonSchema uses (cores pinned, health probe first, interleaved A/B
rounds); result tables are committed under `docs/benchmarks/` with the host, the date and the commit. The benchmarks
never share a solution with the tests, because BenchmarkDotNet and worktrees do not mix.

**Decision.** The .NET engine is the comparison, on .NET's own published workloads, every engine class on both
sides, 0 B as a hard requirement.

## 10. Phases

Each phase is its own run of pull requests in the new repository, each with its gates green, one branch in CI at a
time.

| Phase | Delivers | Gate |
|---|---|---|
| 0 | Repository, build, package skeleton, this document, the Unicode tables and their generator | Builds warning-free on both OSes, packs |
| 1 | Parser, compiler, matcher, both cursors, limits, `IsMatch` only, `None`/`Unicode`/`IgnoreCase`/`Multiline`/`DotAll`/`Sticky` | Existing data set 0 wrong on UTF-8 and UTF-16; lone surrogates right on UTF-16; allocation test 0 B. **Met 2026-10-18**: 15,954,941 right, 0 wrong, 0 refused, 0 at the limit, in 4.4 s; 8 allocation cases at 0 B |
| 2 | The whole drop-in surface of section 4, `Sticky`, `Replace`, `Split`, enumerators, timeout, exceptions | Data set extensions of 8.2 written and green; swap test green. **Met 2026-10-18** apart from the swap test: `captures.jsonl` (exec, global, sticky, replace, split; 5,188 patterns, 207,485 texts) in the data repository at 95a64f0, 3,760,290 answers right, 0 wrong, over UTF-16 and UTF-8 |
| 3 | Prefilters, `SearchValues`, literal search, automaton on UTF-16, benchmarks of section 9, first tables | Interpreter-against-interpreter at or below 1.0 on the industry set; set 3 finishes |
| 4 | Source generator, stage 1 then stage 2, generator tests, diagnostics | Generated-against-generated at or below 1.0 on the industry set; AOT sample publishes |
| 5 | `UnicodeSets` (v flag) | The new family of 8.2 green |
| 6 | Adoption in Corvus.JsonSchema: the translator and shape matchers go, the evaluator and the V5 generator call this library, V4 keeps .NET `Regex` | Corvus.JsonSchema's data set run 0 wrong, the 37 corpora give the same answers and the evaluator is not slower, #1025 closed |

Phase 6 is a separate decision at its time, taken on the tables of phases 3 and 4.

## 11. What is kept in view

- A pattern read without `u` and matched by code unit, over UTF-8, is the only place the two cursors differ in
  meaning. The data set's `code-units.jsonl` holds it on both.
- .NET's `Match` is a class graph (`Match`, `GroupCollection`, `Group`, `CaptureCollection`, `Capture`). The string
  overloads allocate it, as .NET's do; that is the drop-in shape and not a target of the zero-allocation claim. The
  span overloads and the enumerators are where nothing is allocated.
- ECMA-262 has no `Captures` history for a group under a quantifier (only the last iteration's value), so
  `Group.Captures` has one entry. Documented, since it is a difference a user coming from .NET will meet.
