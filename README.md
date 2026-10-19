# Corvus.Regex.Ecma262

[![GitHub license](https://img.shields.io/badge/License-Apache%202-blue.svg)](https://raw.githubusercontent.com/corvus-dotnet/Corvus.Regex.Ecma262/main/LICENSE)

An ECMA-262 regular expression engine for .NET.

- The grammar and the answers of ECMAScript 2025, including lookbehind, named groups, modifier groups, Unicode
  property escapes with Unicode 17 data, and the `u` and `v` flags. The flags are ECMA-262's (`d`, `g`, `i`, `m`,
  `s`, `u`, `v`, `y`), not .NET's `RegexOptions`.
- The public shape of `System.Text.RegularExpressions` (`Regex`, `Match`, `Group`, `Capture`, `Matches`,
  `Replace`, `Split`, `EnumerateMatches`), in the namespace `Corvus.Ecma262.RegularExpressions`.
- Native matching over UTF-8 (`ReadOnlySpan<byte>`) and over UTF-16 (`string`, `ReadOnlySpan<char>`), with no
  transcoding either way.
- Zero allocation per match once the per-thread working memory has grown, and allocation-free forms of the
  members that otherwise allocate: `EnumerateMatches` with the groups into a span the caller gives, `TryReplace`
  into a span, `Replace` into an `IBufferWriter`, and `EnumerateSplits`, each over UTF-8 and UTF-16.
- A backtracking matcher that remembers where a way has failed, so a repetition inside a repetition takes time
  proportional to the pattern times the text; a deterministic automaton for the patterns that need no
  backtracking; a limit of time and of memory on every match; deterministic limits of size on every pattern.
- A build-time source generator with the shape of `[GeneratedRegex]`, shipped in the same package.
- Tested against [V8's answers](https://github.com/corvus-dotnet/ecma262-regex-test-data) for 98,839 patterns and
  4.3 million texts, and benchmarked against `System.Text.RegularExpressions` on the workloads .NET itself publishes.

The engine is a port of the ECMA-262 engine of the [Corvus JSON Schema](https://github.com/corvus-dotnet/Corvus.JsonSchema)
evaluators for Go, Rust, Object Pascal and OCaml, with the C# parser of its .NET translator.

## Status

Under construction. The design is in [docs/design/design.md](docs/design/design.md); the phases and their gates are
in its section 10.

- Phase 1 (the engine and `IsMatch`) passes the whole test data set: 98,777 patterns under each grammar V8 accepts
  them in, 15.95 million answers over UTF-16 and over UTF-8, 0 wrong, 0 refused, 0 matches at their limit, and 0
  bytes allocated per match once warm. The flags `i`, `m`, `s`, `u` and `y` apply; `v` is not yet supported.
- Phase 2 (the surface: `Match`, `Groups`, `Matches`, `Replace`, `Split`, `Escape`, and `EnumerateMatches`, `Count`
  and `TryMatch` over spans) passes the data set's new `captures.jsonl`: V8's `exec`, global, sticky, `replace` and
  `split` answers for 5,188 patterns and 207,485 texts, 3.76 million answers, 0 wrong. The whole set is now
  19.7 million answers at 0 wrong.
- Phase 3 (the searches for where a match can start, and the measured run against `System.Text.RegularExpressions`
  on dotnet/performance's workloads): see [docs/benchmarks](docs/benchmarks) for the table. On the Industry set
  the library is at or below .NET's interpreter on 55 of 57 workloads over UTF-16 (median 0.55) and over UTF-8
  (median 0.49); the exceptions are the two memory-bound literal searches over a 16 MB text, where both engines
  run .NET's span search, and the two CJK searches over UTF-8, where three bytes a character with no rare byte
  cannot be found as fast as one UTF-16 unit.
- Phase 4 (the source generator's structured code and emitted search, in progress): generated code against
  .NET's generated code on the same workloads, median 0.90 over UTF-16 (at or below 1.0 on 46 of 55) and 0.57
  over UTF-8 (48 of 55); see [docs/benchmarks/2026-10-19.md](docs/benchmarks/2026-10-19.md) for the rows above
  1.0 and why.

## Repository structure

- `Solutions` - the library, the source generator, the tests and the benchmarks.
- `docs/design` - the design.
- `docs/benchmarks` - committed benchmark results, with the host, the date and the commit.
- `tools/unicode` - the script that regenerates the Unicode tables.

## Building

```
pwsh ./build.ps1
```

or, with the .NET SDK alone:

```
dotnet build Solutions/Corvus.Regex.Ecma262.slnx
dotnet test --solution Solutions/Corvus.Regex.Ecma262.slnx
```

## Licence

Apache-2.0. Copyright (c) Endjin Limited.
