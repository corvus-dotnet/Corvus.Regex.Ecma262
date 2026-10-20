---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Quick Start"
---
```csharp
using Corvus.Ecma262.RegularExpressions;

// The flags are ECMA-262's: here g (every match), i (ignore case) and u (by code point).
Regex regex = new(@"\b[a-z]+ing\b", "giu");

int count = regex.Count("Sherlock was walking and talking");   // 2

// Over UTF-8, with no transcoding and no allocation.
ReadOnlySpan<byte> utf8 = "walking and talking"u8;
foreach (ValueMatch m in regex.EnumerateMatches(utf8))
{
    Console.WriteLine(utf8.Slice(m.Index, m.Length).Length);  // 7, 7
}
```

```csharp
// At build time: the matcher as code, the pattern checked by the compiler.
public static partial class Patterns
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", "u")]
    public static partial Regex IsoDate();
}
```
