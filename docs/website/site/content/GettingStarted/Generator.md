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

The generator ships in the same package and runs on the `netstandard2.0` build of the library, as a source generator must.
