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
- Zero allocation per match once the per-thread working memory has grown.
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
