---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Differences from .NET's Regex"
---
## Differences from .NET's Regex

The surface follows `System.Text.RegularExpressions`; the meaning follows ECMA-262. The differences a user coming from .NET will meet:

- **The grammar.** No `(?i)` inline option (ECMA-262 has `(?i:...)` groups), no atomic groups, no possessive quantifiers, no `\A` and `\z`, no conditionals, no balancing groups. `\z` is the letter `z` under Annex B and an error under `u`.
- **The character classes.** `\d` is `[0-9]`, `\w` is the 63 ASCII word characters, `\s` is ECMAScript's white space and line terminators, and `.` excludes the four line terminators (LF, CR, U+2028, U+2029).
- **Case.** With `i` and `u`, simple case folding (so `ſ` matches `s` and `K` matches `k`); with `i` and no flag, `toUpperCase` of one character. Never the current culture.
- **Captures.** ECMA-262 keeps only a group's last capture under a quantifier, so `Group.Captures` has one entry.
- **Positions.** Indices are in the units of the text matched: UTF-16 code units over strings, bytes over UTF-8.
- **Timeouts.** Patterns without backreferences are linear and take no timeout; a timeout applies to the others, and the memory a match may use is bounded as well.
