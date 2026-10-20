---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Introduction"
---
## Introduction

Corvus.Regex.Ecma262 is a regular expression engine that answers as ECMAScript does. Where `System.Text.RegularExpressions` has its own grammar and its own meaning for `\d`, `\w`, `\s`, `.`, case folding and Unicode properties, this engine has ECMA-262's, so a pattern written for JavaScript, or taken from a JSON Schema, matches the same texts here as it does there.

This guide covers:

- **Setup** — the one package, and what it brings.
- **Patterns and flags** — the flags are ECMA-262's letters, not `RegexOptions`, and what each one does.
- **Matching** — the members you know from `System.Text.RegularExpressions`, and the allocation-free forms beside them.
- **UTF-8** — the same patterns over `ReadOnlySpan<byte>` with no transcoding.
- **The source generator** — `[GeneratedRegex]` and what it generates.
- **Differences from .NET's Regex** — what to expect when the two meet.
