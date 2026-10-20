The regular expression engine: `Regex` with the members of `System.Text.RegularExpressions.Regex`, the `Match`, `Group` and `Capture` results, the allocation-free `ValueMatch` and `ValueCapture` with their enumerators over UTF-16 and UTF-8, the `RegexFlags` of ECMA-262, and `GeneratedRegexAttribute` for the source generator.

- [`Regex`](/api/corvus-ecma262-regularexpressions-regex.html) — a compiled pattern, and the static members for one-off use
- [`RegexFlags`](/api/corvus-ecma262-regularexpressions-regexflags.html) — the flags of ECMA-262
- [`GeneratedRegexAttribute`](/api/corvus-ecma262-regularexpressions-generatedregexattribute.html) — the matcher as code at build time
- [`ValueMatch`](/api/corvus-ecma262-regularexpressions-valuematch.html) and [`ValueCapture`](/api/corvus-ecma262-regularexpressions-valuecapture.html) — a match and a group as positions, allocating nothing
