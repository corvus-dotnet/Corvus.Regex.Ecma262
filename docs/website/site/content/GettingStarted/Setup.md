---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Setup"
---
## Setup

One package carries the library and the source generator:

```bash
dotnet add package Corvus.Regex.Ecma262
```

The library targets `net10.0` and `netstandard2.0`. The vectorised searches and the generated search code need the .NET 10 or later runtime. The `netstandard2.0` build is for .NET Framework, where the same patterns give the same answers by the plain paths. .NET 8 and .NET 9 are not supported.

```csharp
using Corvus.Ecma262.RegularExpressions;
```

### Building ahead of the first match

A `Regex` builds the automata its matches use, and the program that reports groups, when a match first needs them, so it pays nothing for what it never does. A regular expression made once and matched many times, such as a validator's, can build everything in the constructor instead, so that its first match is no slower than the rest:

```csharp
Regex r = new(pattern, RegexFlags.Unicode, Regex.DefaultMatchTimeout, RegexEdition.Current, RegexUnicodeVersion.Default, RegexBuildOptions.Eager);
```

`Regex.TryCreate` takes the same option. A regular expression from the source generator needs neither: everything is built at build time (see [the source generator](#the-source-generator)).

### Start-up under ReadyToRun

An application published with ReadyToRun runs most of the library from code compiled ahead of time, but not the
matchers themselves. They are generic over the text's code unit (`byte` for UTF-8, `char` for UTF-16), and ReadyToRun
compiles no instantiation of them over those types, so the JIT compiles them the first time a pattern is compiled
or matched in the process. That is a one-time cost at start-up, and steady-state speed is unaffected. Native AOT
compiles everything ahead of time and has no such cost.
