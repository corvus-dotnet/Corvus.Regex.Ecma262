# Corvus.Regex.Ecma262

[![NuGet](https://img.shields.io/nuget/v/Corvus.Regex.Ecma262.svg)](https://www.nuget.org/packages/Corvus.Regex.Ecma262)
[![Build](https://github.com/corvus-dotnet/Corvus.Regex.Ecma262/actions/workflows/build.yml/badge.svg)](https://github.com/corvus-dotnet/Corvus.Regex.Ecma262/actions/workflows/build.yml)
[![ECMA-262 test data](https://github.com/corvus-dotnet/Corvus.Regex.Ecma262/actions/workflows/dataset.yml/badge.svg)](https://github.com/corvus-dotnet/Corvus.Regex.Ecma262/actions/workflows/dataset.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

A drop-in replacement for `System.Text.RegularExpressions` that answers as ECMAScript does, with performance equal
to or better than .NET's on equivalent expressions: the grammar and the answers of ECMA-262, over UTF-8 and over
UTF-16, with no allocation per match and a build-time source generator.

Documentation: [corvus-oss.org/Corvus.Regex.Ecma262](https://corvus-oss.org/Corvus.Regex.Ecma262/)

## Why

`System.Text.RegularExpressions` has its own grammar and its own meaning for `\d`, `\w`, `\s`, `.`, case folding and
Unicode properties. A pattern written for JavaScript, or taken from a JSON Schema (whose `pattern`, `patternProperties`
and `regex` format are defined as ECMA-262 regular expressions), can parse in .NET and match different texts, or
fail to parse at all. This library answers as ECMAScript does, and is tested against V8's answers for every pattern
of the [ECMA-262 regular expression test data](https://github.com/corvus-dotnet/ecma262-regex-test-data): 104,359
patterns and 5.1 million texts, 0 wrong.

- **The answers of ECMAScript 2025**: lookbehind, named groups, modifier groups `(?i:...)`, Unicode property escapes
  on Unicode 17 data, the `u` flag, and the `v` flag with set notation (`[[a-z]--[aeiou]]`, `&&`), strings in classes
  (`\q{...}`) and the properties of strings (`\p{RGI_Emoji}`). The flags are ECMA-262's letters (`d`, `g`, `i`, `m`,
  `s`, `u`, `v`, `y`), not `RegexOptions`.
- **The shape of `System.Text.RegularExpressions`**: `Regex`, `Match`, `Group`, `Capture`, `Matches`, `Replace`,
  `Split` and `EnumerateMatches`, in the namespace `Corvus.Ecma262.RegularExpressions`, so it drops in where the .NET
  class is used.
- **UTF-8 and UTF-16**: one matcher over `ReadOnlySpan<byte>` and over `string` or `ReadOnlySpan<char>`, with no
  transcoding either way and positions in the units of the text.
- **Zero allocation per match** once the per-thread working memory has grown, with allocation-free forms of every
  member: `EnumerateMatches` with the groups into a span the caller gives, `TryMatch`, `TryReplace` into a span,
  `Replace` into an `IBufferWriter` and `EnumerateSplits`, each over both encodings.
- **Bounded**: a backtracking matcher that remembers where a way has failed, so a repetition inside a repetition takes
  time proportional to the pattern times the text; an automaton for the patterns that need no backtracking; a limit of
  time and of memory on every match; deterministic limits of size on every pattern.
- **A source generator** with the shape of `[GeneratedRegex]`, in the same package, which writes the search for a
  start and the match as code at build time and reports a bad pattern as a build error.
- **Fast**: on the Industry workloads .NET itself publishes, generated code runs at a median of 0.86 of .NET's
  generated code over UTF-16 and 0.58 over UTF-8; the interpreter at 0.55 of .NET's interpreter. See
  [docs/benchmarks](docs/benchmarks).

## Install

```bash
dotnet add package Corvus.Regex.Ecma262
```

The package targets `net10.0` and `netstandard2.0` and carries the source generator. The vectorised searches and the
generated search code need the .NET 10 or later runtime. The `netstandard2.0` build is for .NET Framework, where the
same patterns give the same answers by the plain paths. .NET 8 and .NET 9 are not supported.

## Getting started

```csharp
using Corvus.Ecma262.RegularExpressions;

// The flags are ECMA-262's: g for every match, i to ignore case, u to match by code point.
Regex regex = new(@"\b[a-z]+ing\b", "giu");

bool any = regex.IsMatch("Sherlock was walking");
int count = regex.Count("Sherlock was walking and talking");       // 2

Match m = regex.Match("walking and talking");
Console.WriteLine(m.Value);                                          // walking
foreach (Match each in regex.Matches("walking and talking"))
{
    Console.WriteLine($"{each.Index}: {each.Value}");
}

string replaced = regex.Replace("walking and talking", "<$&>");      // <walking> and <talking>
string?[] parts = regex.Split("a walking b talking c");             // ["a ", " b ", " c"]
```

Named groups, lookbehind and the Unicode properties, as in JavaScript:

```csharp
Regex date = new(@"(?<=on )(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})", "u");
Match d = date.Match("on 2026-10-20");
string year = d.Groups["year"].Value;                                // 2026

Regex greek = new(@"\p{Script=Greek}+", "u");
Regex consonants = new(@"[[a-z]--[aeiou]]+", "v");                   // the v flag: set difference
```

### Over UTF-8

Every member that takes `ReadOnlySpan<char>` has a twin that takes UTF-8 bytes. The bytes are matched as they are,
and the positions are byte offsets.

```csharp
Regex words = new(@"\p{L}+", "gu");
ReadOnlySpan<byte> utf8 = "Шерлок Холмс"u8;

int n = words.Count(utf8);                                           // 2
foreach (ValueMatch w in words.EnumerateMatches(utf8))
{
    ReadOnlySpan<byte> word = utf8.Slice(w.Index, w.Length);
}
```

### Without allocating

`Match`, `Matches`, `Replace` with a string and `Split` allocate what they return, as .NET's do. The span members
beside them allocate nothing:

```csharp
Regex email = new(@"(?<user>\w+)@(?<host>\w+)\.\w+", "gu");
string text = "mail sherlock@baker.st or watson@baker.st";

Span<ValueCapture> groups = stackalloc ValueCapture[email.GroupCount + 1];
foreach (ValueMatch match in email.EnumerateMatches(text.AsSpan(), groups))
{
    ReadOnlySpan<char> host = text.AsSpan(groups[2].Index, groups[2].Length);
}

Span<char> buffer = stackalloc char[128];
if (email.TryReplace(text.AsSpan(), "$<user>".AsSpan(), buffer, out int written))
{
    ReadOnlySpan<char> result = buffer.Slice(0, written);
}

foreach (Range piece in email.EnumerateSplits(text.AsSpan())) { }
```

### At build time

`[GeneratedRegex]` on a partial method or property (a partial property needs C# 13) writes the matcher as code. A pattern that is not valid, a flag
that is not one, or `u` with `v`, is a build error with the reason and the position.

```csharp
public static partial class Patterns
{
    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}/\d{2,4}\b", "gu")]
    public static partial Regex Dates();

    [GeneratedRegex(@"(?<word>\w+)", RegexFlags.Global | RegexFlags.Unicode | RegexFlags.IgnoreCase, 100)]
    public static partial Regex Words { get; }
}

int dates = Patterns.Dates().Count("on 11/18/2019 and 1/2/03");    // 2
```

### Validity without an exception

JSON Schema reads a pattern with the `u` flag first and with the Annex B grammar when that refuses. `TryCreate`
costs no exception:

```csharp
if (!Regex.TryCreate(pattern, RegexFlags.Unicode, out Regex? regex, out string? error)
    && !Regex.TryCreate(pattern, RegexFlags.None, out regex, out error))
{
    Console.WriteLine(error);
}
```

## The flags

| Flag | `RegexFlags` | Meaning |
|---|---|---|
| `d` | `HasIndices` | Accepted for completeness; indices are always kept. |
| `g` | `Global` | `Replace` replaces every match; without it, the first, as `String.prototype.replace` does. |
| `i` | `IgnoreCase` | Simple case folding with `u` or `v`, `toUpperCase` without. Never the culture. |
| `m` | `Multiline` | `^` and `$` at line terminators. |
| `s` | `DotAll` | The dot matches line terminators. |
| `u` | `Unicode` | The `u` flag grammar; matched by code point. |
| `v` | `UnicodeSets` | Set notation and properties of strings; matched by code point. `u` and `v` together is a syntax error. |
| `y` | `Sticky` | A match only at the start position. |

With no flag the pattern is read with the Annex B grammar and matched by UTF-16 code unit, as ECMA-262 does.
`Matches`, `EnumerateMatches` and `Count` are the global search whether or not `g` is set; where ECMA-262 keeps
`lastIndex` on the `RegExp`, the library takes the position as an argument. A `Regex` has no mutable state and is
safe to share between threads.

## Differences from .NET's Regex

- **The grammar**: no `(?i)` inline option (ECMA-262 has `(?i:...)` groups), no atomic groups, no possessive
  quantifiers, no `\A` and `\z`, no conditionals, no balancing groups.
- **The classes**: `\d` is `[0-9]`, `\w` the 63 ASCII word characters, `\s` ECMAScript's white space and line
  terminators, and `.` excludes LF, CR, U+2028 and U+2029.
- **Case**: with `i` and `u` simple case folding (`ſ` matches `s`, `K` matches `k`); with `i` and no flag,
  `toUpperCase` of one character.
- **Captures**: ECMA-262 keeps a group's last capture under a quantifier, so `Group.Captures` has one entry.
- **Timeouts**: patterns without backreferences are linear and take no timeout; `MatchTimeout` applies to the others,
  and a match that reaches its limit of time or memory throws `RegexMatchTimeoutException`.

## Documentation

- [The site](https://corvus-oss.org/Corvus.Regex.Ecma262/): getting started, the design and the API reference.
- [docs/design/design.md](docs/design/design.md): what is ported and from where, the public surface, one matcher over
  two encodings, the searches for where a match can start, the source generator, the test data and the benchmarks.
- [docs/contributing/contributing.md](docs/contributing/contributing.md): the tools on each platform, the build and
  the gates step by step, and what a pull request needs.
- [docs/benchmarks/industry.md](docs/benchmarks/industry.md): what the Industry benchmarks measure against
  `System.Text.RegularExpressions` and V8, and where the library is slower and why; the figures are on
  [the benchmark page](https://corvus-oss.org/corvus-regex-ecma262-benchmarks/), beside every Corvus ECMA-262 engine, measured on one runner each night.

## Repository layout

- `Solutions/Corvus.Regex.Ecma262` - the library: the parser (`Syntax`), the Unicode tables (`Unicode`), the compiler,
  the matcher and the searches (`Engine`), and the public surface.
- `Solutions/Corvus.Regex.Ecma262.SourceGenerator` - the source generator, which compiles the engine's sources in.
- `Solutions/Corvus.Regex.Ecma262.Tests`, `Corvus.Regex.Ecma262.SourceGenerator.Tests` - the unit tests, each pattern
  run as interpreted and as generated code.
- `Solutions/Corvus.Regex.Ecma262.DatasetRunner` - the run over the whole ECMA-262 test data, interpreted or generated.
- `Solutions/Corvus.Regex.Ecma262.Benchmarks` - the BenchmarkDotNet comparisons with `System.Text.RegularExpressions`.
- `Solutions/Corvus.Regex.Ecma262.AotSample` - a native AOT sample of the generator.
- `docs/design`, `docs/benchmarks` - the design and the committed benchmark tables.
- `docs/website` - the documentation site.
- `tools/unicode` - the scripts that regenerate the Unicode tables and the properties of strings.

## Building and testing

The build needs the .NET 10 SDK; the repository build script needs PowerShell 7 as well.

```bash
dotnet build Solutions/Corvus.Regex.Ecma262.slnx
dotnet test --solution Solutions/Corvus.Regex.Ecma262.slnx --framework net10.0
pwsh ./build.ps1                       # the CI build: compile, test, analyse, pack
```

The ECMA-262 test data is the gate for every change to the engine. Clone
[ecma262-regex-test-data](https://github.com/corvus-dotnet/ecma262-regex-test-data) beside this repository and run
the `DatasetRunner` over it, interpreted and as generated code; the pass is 0 wrong, 0 refused-valid, 0
accepted-invalid and 0 at the limit. [The contributor guide](docs/contributing/contributing.md) has the tools for
Windows, Linux and macOS and every step: the build, the tests, the data set, the benchmarks and the site.

## Contributing

Pull requests are welcome. [docs/contributing/contributing.md](docs/contributing/contributing.md) says what one
needs: a warning-free build, the unit tests and the data set green (interpreted and generated) with the result
lines in the pull request, the case in the test data first for a change of semantics, a measured run for a change
made for speed, a failing test first for a bug, and the design document kept current.

Releases are tagged from `main` by the release automation when a pull request is merged; a pull request that should
not make a release carries the `no_release` label.

## Project sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Microsoft Gold Partner for Cloud Platform, Data
Platform, Data Analytics, DevOps, and a Power BI Partner.

For more information about our products and services, or for commercial support of this project, please
[contact us](https://endjin.com/contact-us).

Keep up with everything that's going on at endjin via our [blog](https://endjin.com/blog), follow us on
[Twitter](https://twitter.com/endjin), or [LinkedIn](https://www.linkedin.com/company/1671851/). Our other open
source projects can be found at [https://endjin.com/open-source](https://endjin.com/open-source).

## Code of conduct

This project has adopted a code of conduct adapted from the [Contributor Covenant](http://contributor-covenant.org/)
to clarify expected behavior in our community. For more information see the
[Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact
[hello@endjin.com](mailto:hello@endjin.com) with any additional questions or comments.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for details. Copyright (c) Endjin Limited.
