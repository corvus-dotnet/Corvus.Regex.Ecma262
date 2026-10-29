# Corvus.Regex.Ecma262: design

An outline of the library as it is: what it offers, how the engine is built and why, and how it is tested and
measured. The [getting-started pages](https://corvus-oss.org/Corvus.Regex.Ecma262/) show how to use it.

## 1. What it is

A .NET regular expression library with ECMA-262's grammar and answers (ECMAScript 2025) and the public shape of
`System.Text.RegularExpressions`, so a project moves to it by changing a `using` directive and its options. It has a
build-time source generator in the shape of `[GeneratedRegex]`, matches UTF-8 natively as well as UTF-16, and a warm
match allocates nothing.

**Why ECMA-262.** JSON Schema, OpenAPI and many wire formats specify their patterns as ECMAScript regular
expressions. .NET's grammar differs in its classes (`\d` and `\w` are Unicode in .NET, ASCII in ECMA-262), its case
folding and its line terminators, so a translation to it can never be exact. An engine that reads ECMA-262 directly,
held to V8's answers over a large data set (section 6), needs no translation.

## 2. Public surface

**Names.** The types are .NET's names (`Regex`, `Match`, `Group`, `Capture`, the collections, `MatchEvaluator`,
`ValueMatch`, `GeneratedRegexAttribute`, `RegexParseException`, `RegexMatchTimeoutException`) in the namespace
`Corvus.Ecma262.RegularExpressions`. The namespace has no `Regex` segment: C# binds a simple name to an enclosing
namespace before a `using`, so inside any `Corvus.*` namespace `Regex` would bind to `Corvus.Regex`.

**Flags.** The options are ECMA-262's flags and nothing else: `[Flags] enum RegexFlags` (`HasIndices` d, `Global` g,
`IgnoreCase` i, `Multiline` m, `DotAll` s, `Unicode` u, `UnicodeSets` v, `Sticky` y), or the flags as a string as
`new RegExp` takes them. No .NET option the engine cannot honour exists, so none has to be refused at run time.
`RegexFlags.None` is `new RegExp(pattern)`: the Annex B grammar, matched by UTF-16 code unit.

- The position is an argument, as in .NET (`Match(input, startAt)` is `exec` with `lastIndex` set), so a `Regex` has
  no mutable state and is safe to share between threads.
- `Matches`, `EnumerateMatches` and `Count` are the global search with or without `g`; `Split` ignores `g`, as
  `RegExp.prototype[Symbol.split]` does.
- The v flag is parser work: a class is a set of code points and a set of strings, and a class with strings compiles
  to an alternation of the strings, longest first, then the set. The matcher and the searches see nothing new.

**Edition and Unicode version.** Two settings beside the flags, independent of each other:

- `RegexEdition`: `Current` (ECMAScript 2025, the default) or `Es2020`, the grammar JSON Schema 2020-12 names. The
  2020 grammar refuses the `d` and `v` flags, modifier groups, a group name used twice, and property names outside
  its tables.
- `RegexUnicodeVersion`: `V13`, `V17`, `V18`, `Latest` (18) or `Default` (V8's version, 17).
- Both are resolved when the pattern is compiled, so a warm match costs the same whatever they are. The constructor,
  `TryCreate`, `IsValid` and `[GeneratedRegex]` take them; `Regex.Edition` and `Regex.UnicodeVersion` report them.

**Errors.** A pattern that is not valid throws `RegexParseException` with ECMA-262's message and the offset. A
pattern over a size limit throws `RegexPatternTooLargeException`, so a caller can tell "not valid" from "too large".
`TryCreate(pattern, flags, matchTimeout, out regex, out error)` reports either without an exception and needs no
global timeout.

**Validity without allocation.** `IsValidPattern(pattern, flags, [edition, unicodeVersion])` takes a pattern as UTF-16
or UTF-8 and says whether it is valid in the grammar, with nothing allocated: a reader of its own follows the parser's
decisions with no tree and no sets, keeps groups and nested classes on stacks rather than recursing, and needs no
string. It asks the grammar only, so a pattern too large to compile is valid here; `IsValid` compiles. The data-set
runner holds it to V8's verdict on every pattern.

**ECMA-262 algorithms.** `Replace` substitutes as `GetSubstitution` does (`$$`, `$&`, `` $` ``, `$'`, `$n`, `$nn`,
`$<name>`). `Split` follows `RegExp.prototype[Symbol.split]` (captures spliced in, `count` as `limit`). `Escape` and
`Unescape` use ECMA-262's syntax characters.

**Input.** Every matching member takes `string`, `ReadOnlySpan<char>` or `ReadOnlySpan<byte>` (UTF-8); positions are
in the input's unit. Nothing is transcoded.

- The span members allocate nothing: `IsMatch`, `Count`, `EnumerateMatches` (optionally filling a caller's
  `Span<ValueCapture>` with each group), `TryMatch`, `TryReplace` into a span, `Replace` into an `IBufferWriter<T>`,
  and `EnumerateSplits`.
- UTF-8 input is checked before it is read, and bytes that are not UTF-8 throw `ArgumentException`: they are never
  matched or read as U+FFFD. An `Unchecked` twin of each member skips the check for text already known to be UTF-8.
- A lone surrogate in UTF-16 input is matched as ECMA-262 matches it.

**Limits.** No pattern and no text can hang a program or overflow the stack.

- A `matchTimeout` per instance, with .NET's `REGEX_DEFAULT_MATCH_TIMEOUT` default, applies to every pattern; a
  match past it, or whose backtracking stack reaches its cap, throws `RegexMatchTimeoutException`.
- Size limits on the pattern (nesting, program size, construction cost) are deterministic, never a clock.
- A deeply nested pattern is read and compiled on a thread with a 64 MB stack of its own; where no thread can be
  started, the reader checks the stack as it goes and refuses what will not fit. Buffers whose size the pattern
  decides go on the stack only up to 256 elements and are rented beyond.

## 3. Engine

**One matcher, two encodings.** The matcher is generic over the text's unit and an empty struct that reads it
(`ITextEncoding<TUnit>`, with `Utf8Encoding` and `Utf16Encoding`). The JIT specialises each method for the struct,
so the loop has no virtual call and no branch on the encoding. Matching by UTF-16 code unit over UTF-8 places the
position between the halves of a four-byte character at its third byte.

**Program and working memory.** A compiled pattern is flat arrays the `Regex` holds. Per-thread working memory
(backtracking stack, capture slots, loop counters, memo) is grown to need and kept. The memo of failed (position,
construct) pairs keeps a pattern without backreferences linear; it is taken into use only once a match has made
enough steps to pay for it, and the clock is read every 4,096 steps.

**Finding where to start.** Most of a search's time is finding starts, so the compiler works out what each pattern
allows and the search uses the best that applies:

- *Anchored patterns* start only at 0.
- *A literal* (at the start, at a fixed distance, or after leading loops) is found by `Needle<T>`: it compares the
  two or three positions of the literal that text makes rarest, chosen from a table of unit frequencies, so a script
  whose characters share lead bytes does not stop it at every character. Under `i`, forms that differ in masked bits
  are one comparison; forms of another length (the long s, the Kelvin sign over UTF-8) are looked for apart.
- *Several literals* use `MultiNeedle<T>` (one or two shared positions, chosen by a cost model) or, where no position
  is rare, `Teddy` fingerprints (nibble tables looked up by vector shuffles). Over UTF-16 Teddy narrows the units to
  bytes (`Teddy<T>.PackedSupported`: AVX2, AdvSimd or SSE2).
- *An alternation whose alternatives each hold a literal* is searched by all the literals at once, each occurrence
  opening the window of starts that could reach it.
- *Terms before a literal* within 62 characters are read backwards from each occurrence by a reverse automaton,
  which gives the starts that reach it.
- *Runs of loops then a literal* are found by the literal and walked back to the earliest start.
- *A pattern with no literal* reads its forward automaton from a start to find where the first match ends before
  trying starts, after a Boyer-Moore window rules out the positions no match can end within.
- Otherwise *a first set* is found by `SearchValues`, and a minimum length rules out short rests.
- An enumeration carries what it has learned about the text (no line terminator here, no long s there) from one match
  to the next.

**Automata.** For a pattern that needs no backtracking, a lazy DFA over ASCII (at most 256 states, built on first use
and bounded in its construction) answers whether a text matches, `\b` included; a non-ASCII unit hands the text to
the matcher. Where a match is found, an anchored automaton (`Adfa`, up to 2,048 states) finds where it ends, with the
backtracking matcher's answer, and accelerates states that loop over large sets by a vector search. All automata are
built on first use, from trees less than 64 levels deep.

## 4. Source generator

A Roslyn incremental generator in the shape of .NET's: `[GeneratedRegex(pattern, flags, matchTimeoutMilliseconds)]`
on a partial method or property. It ships in the same package as an analyzer, so one `PackageReference` gives both.
It compiles the library's own parser, so a pattern is checked at build time by the parser that will run it;
diagnostics CRE0001 to CRE0005 cover the pattern, the member's shape, the flags, a non-partial type and size limits.

- The generated member returns an instance built by the `Regex` constructor on first use. A serialised program would
  freeze the program's shape into every consumer's build to save a few microseconds per process.
- *The match as code* (`TreeEmitter`): structured code from the tree, in the shape .NET's generator uses. Each
  construct that can be retried keeps its state in locals and owns a label, so nothing is pushed for it; the memo
  marks are kept at the interpreter's points. Patterns outside that shape (a loop over a group, a lookbehind, a very
  large tree) use the instruction emitter (`RunEmitter`), and a program too large for straight-line code hands the
  match to the interpreter.
- *The search as code* (`TreeEmitter.Finder`): pattern-specific code over the span primitives, with each search
  object in a `static readonly` field whose type the JIT knows. Every `SearchValues<string>` call sits in a
  non-inlined method of its own, since called from the generic find the JIT gives it twice its time.
- An enumeration's later matches go from the enumerator straight to the generated loop where the program tracks no
  group.
- The emitted search uses members the library has only on `net10.0`, under `#if NET10_0_OR_GREATER`; a .NET
  Framework consumer gets the plain paths and the same answers.
- *The emitter as source.* A source generator does not run on another's output, so a `[GeneratedRegex]` written
  into generated code is never given its body. `RegexEmitter.TryEmit` writes the same code for a pattern
  (`TryValidate` only checks it, as `Regex.TryCreate` would), and the
  `Corvus.Regex.Ecma262.CodeGeneration.Sources` package carries it, with the parser and compiler it needs, as C#
  compiled into the code generator that takes it. Its types are internal and in a namespace of their own
  (`Corvus.Ecma262.RegularExpressions.CodeGeneration`), so it loads no assembly beside a source generator and
  never clashes with the library. One list of files (`EmitterSources.props`) feeds the generator and the package.

## 5. Targets and layout

`net10.0` and `netstandard2.0`. The `netstandard2.0` build serves .NET Framework and the generator, which is a
netstandard2.0 Roslyn component compiling the same source. The modern target never pays for it: a faster runtime
path (`SearchValues`, `Vector128`/`Vector256`, AdvSimd) is taken under `#if NET` with a plain fallback for
netstandard2.0. The `netstandard2.0` build is held to the same answers, not the same speed: tests and the data run
target `net481` too.

```
Solutions/
  Corvus.Regex.Ecma262/                     the library (Syntax/, Unicode/, Engine/, the public surface)
  Corvus.Regex.Ecma262.SourceGenerator/     packed into the library package as an analyzer
  Corvus.Regex.Ecma262.CodeGeneration.Sources/  the emitter as a source package, compiled here as it ships
  Corvus.Regex.Ecma262.Tests/, .SourceGenerator.Tests/, .CodeGeneration.Sources.Tests/
  Corvus.Regex.Ecma262.Benchmarks/          BenchmarkDotNet
  Corvus.Regex.Ecma262.DatasetRunner/       the ECMA-262 test data, interpreted and generated, both encodings
  Corvus.Regex.Ecma262.StackProbe/          patterns built to overflow the stack
  Corvus.Regex.Ecma262.AotSample/           a native AOT publish of a generated pattern
tools/unicode/                              the generated Unicode tables (13, 17, 18) and their check
tools/benchmarks/                           the harness image's scripts
docs/                                       this document, the benchmark notes, the documentation site
```

The build is endjin's ZeroFailed pipeline: warning-free on Linux and Windows, a package on every build and a release
on merge to `main`.

## 6. Testing

**The ECMA-262 test data.** [`ecma262-regex-test-data`](https://github.com/corvus-dotnet/ecma262-regex-test-data)
holds V8's answers (Node 24.22.1, Unicode 17): validity under each grammar, `test` answers, and for the drop-in
surface the match and group positions of `exec`, the global search, `y`, `replace`, `replaceAll` and `split`, plus
every `exec` call of test262. Its `manifest.json` lists the files and counts. The library answers all of it right,
over both encodings, interpreted and as generated code, with none wrong, refused or at a limit. `dataset.yml` runs it
on every pull request and nightly, on Linux, on arm64, on x64 without AVX2, and on .NET Framework 4.8.1; it also runs
the data as the data's own scripts write it for Unicode 13 and 18 and for ECMAScript 2020.

**Other tests.** Unit tests over the members, captures, span members, searches, Unicode tables and each grammar;
allocation tests counting the measuring thread only (the least of three windows once warm); every search test's
pattern also compiled as generated code; generator tests on Roslyn's harness; and the stack probe (964 patterns,
each in its own process, on threads of 8 MB, 1 MB and 256 KB, none overflowing).

## 7. Benchmarks

The BenchmarkDotNet project measures the workloads .NET's own team publishes: the Industry sets of
`dotnet/performance` (Sherlock Holmes, Leipzig, Mariomka, rebar Russian and Chinese), `Perf.Regex.Common` and a
pathological set. Industry rows run five ways (.NET's interpreter and generator, this library's interpreter over
UTF-16, and its generated code over UTF-16 and UTF-8), every Corvus row under the u flag.

The published figures are on [the benchmark page](https://corvus-oss.org/corvus-regex-ecma262-benchmarks/), which
runs every engine's harness image each night on one x86-64 runner and one arm64 runner; this repository publishes
none of its own. `docs/benchmarks/industry.md` says what is measured and where the library is slower and why.

Three Industry rows are not like for like with .NET, as the price of ECMA-262's rules under u: `.*` (ECMA-262's `.`
excludes CR, so the CRLF text has more matches), `Sher[a-z]+|Hol[a-z]+` under i (the search must also find the long
s and the Kelvin sign), and `[a-q][^u-z]{13}x` (a supplementary character is two units, so the distance to the `x` is
not fixed).

## 8. Differences a .NET user will meet

- The grammar is ECMA-262's: `(?i:...)` groups but no `(?i)` prefix, no atomic groups, possessive quantifiers, `\A`,
  `\z`, conditionals or balancing groups. `\d` is `[0-9]`, `\w` the 63 ASCII word characters, and `.` excludes LF,
  CR, U+2028 and U+2029.
- `Group.Captures` has one entry: ECMA-262 keeps only a quantified group's last iteration.
- The string overloads allocate the `Match` graph, as .NET's do; the span members and enumerators allocate nothing.
- Without `u` or `v`, a pattern over UTF-8 is matched by UTF-16 code unit, as ECMA-262 defines it.
