---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "UTF-8"
---
## UTF-8

Every member that takes a `ReadOnlySpan<char>` has a twin that takes UTF-8 bytes, and the engine matches the bytes as they are. Nothing is transcoded, and the positions it gives are byte offsets.

```csharp
Regex regex = new(@"\p{L}+", "gu");
ReadOnlySpan<byte> utf8 = "Шерлок Холмс"u8;

int words = regex.Count(utf8);                                     // 2
foreach (ValueMatch m in regex.EnumerateMatches(utf8))
{
    ReadOnlySpan<byte> word = utf8.Slice(m.Index, m.Length);       // the bytes of one word
}

ArrayBufferWriter<byte> output = new();
regex.Replace(utf8, "_"u8, output);                                // "_ _"
```

Under the `u` and `v` flags a character is a code point in both encodings, so a class, a dot or a quantifier takes a four-byte sequence as one character. With no flag ECMA-262 matches by UTF-16 code unit; over UTF-8 the engine keeps that meaning, so a pattern with no flag gives the same answers over the bytes of a text as over its chars, except where the text holds a lone surrogate, which UTF-8 cannot hold.
