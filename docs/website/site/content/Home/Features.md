---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Features"
---
## The answers of ECMAScript

The grammar and the semantics of ECMAScript 2025: lookbehind, named groups, modifier groups, Unicode property escapes on Unicode 17 data, and the `u` and `v` flags with set notation and strings in classes. Tested against V8's answers for 117,889 patterns and 5.9 million texts, test262's among them, at 0 wrong.

## Two encodings, one matcher

Matching over UTF-8 (`ReadOnlySpan<byte>`) and over UTF-16 (`string`, `ReadOnlySpan<char>`) with no transcoding either way, from the same engine.

## Zero allocation per match

Nothing is allocated once the per-thread working memory has grown. `EnumerateMatches`, `TryMatch`, `TryReplace`, `Replace` into an `IBufferWriter` and `EnumerateSplits` give the allocation-free forms of every member.

## The shape of System.Text.RegularExpressions

`Regex`, `Match`, `Group`, `Capture`, `Matches`, `Replace`, `Split` and `EnumerateMatches`, in the namespace `Corvus.Ecma262.RegularExpressions`, so the library drops in where the .NET class is used.

## A source generator

`[GeneratedRegex]` on a partial method or property writes the matcher as code at build time, with the pattern checked and reported as a build error.

## Bounded in time and memory

A backtracking matcher that remembers where a way has failed, a deterministic automaton for the patterns that need no backtracking, a limit of time and of memory on every match, and deterministic limits of size on every pattern.
