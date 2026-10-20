---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Patterns and flags"
---
## Patterns and flags

A pattern is written as it would be in JavaScript, and the flags are the letters `RegExp` takes, as a string or as the `RegexFlags` enumeration:

| Flag | `RegexFlags` | Meaning |
|---|---|---|
| `d` | `HasIndices` | Accepted for completeness; indices are always kept. |
| `g` | `Global` | `Replace` replaces every match; without it, the first, as `String.prototype.replace` does. |
| `i` | `IgnoreCase` | Canonicalize of the mode in force: simple case folding with `u` or `v`, `toUpperCase` without. Never the culture. |
| `m` | `Multiline` | `^` and `$` at line terminators. |
| `s` | `DotAll` | The dot matches line terminators. |
| `u` | `Unicode` | The `u` flag grammar; matched by code point. |
| `v` | `UnicodeSets` | Set notation and properties of strings; matched by code point. `u` and `v` together is a syntax error, as in ECMA-262. |
| `y` | `Sticky` | A match only at the start position. |

```csharp
Regex a = new(@"\p{Script=Greek}+", "u");
Regex b = new(@"[[a-z]--[aeiou]]+", RegexFlags.UnicodeSets);
Regex c = new(@"^(?<year>\d{4})-(?<month>\d{2})$", "u");
```

With no flag the pattern is read with the Annex B grammar and matched by UTF-16 code unit, which is what ECMA-262 does with no flag. JSON Schema's reading, the `u` flag first and Annex B when that refuses, is two calls of `Regex.TryCreate`, which costs no exception:

```csharp
if (!Regex.TryCreate(pattern, RegexFlags.Unicode, out Regex? regex, out string? error)
    && !Regex.TryCreate(pattern, RegexFlags.None, out regex, out error))
{
    throw new InvalidOperationException(error);
}
```

A pattern that is not valid throws `RegexParseException`, with the reason and the offset. Every pattern has deterministic limits of size, and `RegexPatternTooLargeException` says when one is passed.
