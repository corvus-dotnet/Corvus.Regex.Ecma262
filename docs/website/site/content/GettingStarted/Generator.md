---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "The source generator"
---
## The source generator

`[GeneratedRegex]` on a partial method or a partial property writes the matcher as code at build time, in the shape of .NET's own attribute (a partial property needs C# 13, which `net10.0` has by default; on `netstandard2.0` set `LangVersion` to 13 or use a method). The pattern and the flags are checked by the generator: a pattern that is not valid, a flag that is not one, or `u` with `v`, is a build error with the reason and the position.

```csharp
public static partial class Patterns
{
    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}/\d{2,4}\b", "gu")]
    public static partial Regex Dates();

    [GeneratedRegex(@"(?<word>\w+)", RegexFlags.Global | RegexFlags.Unicode | RegexFlags.IgnoreCase, 100)]
    public static partial Regex Words { get; }
}

int n = Patterns.Dates().Count("on 11/18/2019 and 1/2/03");       // 2
```

The member returns one `Regex` each time, built on the generated code. The generated code is the search for where a match can start and the match itself as structured code, over both encodings. A pattern too large for straight-line code (a class of thousands of strings, such as `\p{RGI_Emoji}`) is handed to the interpreter by its generated runner, so every pattern compiles.

The generator also writes the compiled regular expression into the generated code as data: the programs, the automaton that answers `IsMatch` over ASCII text, and the automata that speed a search, all worked out at build time. The `Regex` is made from that data with nothing parsed, compiled or built at run time, so its first match costs what a later one does, under the JIT, ReadyToRun and native AOT alike. The data records the version of the engine that wrote it; a library that is not that version ignores it and compiles the pattern, with the same answers.

The generator ships in the same package and runs on the `netstandard2.0` build of the library, as a source generator must.

### In generated code

A source generator does not run on another generator's output, so a `[GeneratedRegex]` member written by a code generator is never given its body. A code generator writes the body itself with the `Corvus.Regex.Ecma262.CodeGeneration.Sources` package: `RegexEmitter.TryEmit` returns the same code the source generator writes, as a class to nest in the generated type, or the reason the pattern does not compile. The package is C# compiled into the code generator, with no assembly, so a source generator can take it.

```xml
<PackageReference Include="Corvus.Regex.Ecma262.CodeGeneration.Sources" PrivateAssets="all" />
```
