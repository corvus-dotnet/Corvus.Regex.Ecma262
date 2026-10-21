# Corvus.Regex.Ecma262: design

This document describes the library as it is: what it offers, how the engine is built, why the decisions that
shape it were taken, and how it is tested and measured. The getting-started pages on the
[documentation site](https://corvus-oss.org/Corvus.Regex.Ecma262/) show how to use it; this document is for a
reader who wants to know what is underneath, or who intends to change it.

## 1. What it is

A regular expression library for .NET whose grammar and answers are those of ECMA-262 (ECMAScript 2025), with the
public shape of `System.Text.RegularExpressions`, so that a project moves to it by changing a `using` directive and
its options. It includes a build-time source generator with the shape of `[GeneratedRegex]`. It matches UTF-8
text natively, in addition to UTF-16, and a match allocates nothing once the per-thread working memory has grown.

It is its own library in its own repository, `corvus-dotnet/Corvus.Regex.Ecma262`, and its own NuGet package,
so that it can be used anywhere. Corvus.JsonSchema, whose `pattern` and `patternProperties` keywords are defined
over ECMA-262 expressions, is its first intended consumer; the adoption is a separate decision, taken on the
benchmarks of section 9.

**Why ECMA-262 and not .NET's grammar.** JSON Schema, OpenAPI and many wire formats specify their patterns as
ECMAScript regular expressions. .NET's engine reads a different grammar with different classes (`\d` and `\w`
are Unicode in .NET, ASCII in ECMA-262), a different case-folding rule and a different notion of a line
terminator. A translation layer from one to the other can never be exact, and the shapes it cannot translate
have to be refused or interpreted by hand. An engine that reads the ECMA-262 grammar directly, held to the same
answers as V8 over a large data set (section 8), removes the translation and its exceptions.

## 2. Origins: what is ported and from where

The Corvus JSON Schema ports in Go, Rust, Object Pascal and OCaml each carry an ECMA-262 engine, one design in
four copies, and the C# code generator carries a parser of its own. The library was built from those rather than
from nothing:

| Piece | Source | Notes |
|---|---|---|
| Parser, syntax tree, both grammars (the u flag; Annex B without), group names, modifier groups | `EcmaRegexParser.cs` from Corvus.JsonSchema's code generator | Already C#, already netstandard2.0, already Unicode 17 and ECMAScript 2025. The base of the front end. |
| Unicode property tables, case folding (Canonicalize for both modes), sets | `EcmaRegexUnicode.cs`, `EcmaRegexUnicodeData.cs` (generated) and the Go `ucd` package | The generator script `tools/unicode/gen-ecma-unicode-data.ps1` moved with them. |
| Compiler (tree to flat program), instruction set | The Go and Rust `ecmaregex` compilers | Characters, sets, literals, anchors, splits, captures, lookarounds, counted loops, backreferences in four forms. |
| Backtracking matcher: explicit stack, memo of failed places, counted loops, first sets, time and memory limits | The Go and Rust virtual machines | The Rust form (per-thread state, retained capacity) is the closer model for .NET. |
| ASCII automaton for linear patterns | The Go and Rust automata | 256 states, built on first use. |
| Matching by UTF-16 code unit over UTF-8 text | The Go and Rust `units` cursors | The position between the halves of a four-byte character is the offset of its third byte. |
| Resource limits | The Rust parser's limits and cost model | Pattern nesting, program size, construction cost, match time, backtracking memory. |

Each file records its provenance. The v flag (section 3.2) and everything in sections 4.3, 4.4 and 5 beyond the
instruction interpreter were written for this library and have since been carried back to the ports where they
apply.

## 3. Public surface

### 3.1 Names and namespace

The types are named as .NET names them: `Regex`, `Match`, `Group`, `Capture`, `MatchCollection`,
`GroupCollection`, `CaptureCollection`, `MatchEvaluator`, `ValueMatch`, `GeneratedRegexAttribute`,
`RegexParseException`, `RegexMatchTimeoutException`. The options type is not `RegexOptions`: it is `RegexFlags`,
the flags of ECMA-262 (section 3.2). Moving a project to the library is a change of `using` plus a change of its
options; that break in source compatibility was accepted so that the options say what the engine does rather than
carry .NET options the engine cannot honour.

The namespace is `Corvus.Ecma262.RegularExpressions`, which mirrors `System.Text.RegularExpressions`, and the
package id is `Corvus.Regex.Ecma262`. The namespace cannot contain a segment named `Regex`: C# resolves a simple
name by walking the enclosing namespaces before it reads the `using` directives of the compilation unit, so inside
`namespace Corvus.Text.Json` the name `Regex` would bind to a namespace `Corvus.Regex` and not to the type, and
any consumer in a `Corvus.*` namespace would hit the same.

### 3.2 Flags

The options are ECMA-262's flags and nothing else: a `[Flags] enum RegexFlags` with one member for each flag of
`RegExp`, and a constructor that also takes the flags as the string `new RegExp(pattern, flags)` takes.

| Member | Flag | Meaning |
|---|---|---|
| `HasIndices` | `d` | Accepted for completeness. Indices are always kept, so it changes nothing. |
| `Global` | `g` | `Replace` replaces every match; without it, the first, as `String.prototype.replace` does. |
| `IgnoreCase` | `i` | Canonicalize of the mode in force (simple case folding with `u` or `v`, `toUpperCase` without). Never the culture. |
| `Multiline` | `m` | `^` and `$` at line terminators. |
| `DotAll` | `s` | The dot matches line terminators. |
| `Unicode` | `u` | The u flag grammar; matched by code point. |
| `UnicodeSets` | `v` | Set notation and properties of strings; matched by code point. `u` and `v` together is a syntax error, as in ECMA-262. |
| `Sticky` | `y` | A match only at the start position. |

`RegexFlags.None` is `new RegExp(pattern)` with no flags: the Annex B grammar and matching by UTF-16 code unit,
which is what ECMA-262 does with no flag. The u-first reading JSON Schema uses (the u flag grammar, then Annex B
when that refuses) is the caller's: two calls of the non-throwing `Regex.TryCreate(pattern, flags, out Regex?
regex, out string? error)`, which costs no exception, and `Regex.Flags` says which flags are in force.

Where ECMA-262 keeps `lastIndex` on the `RegExp` object, the library takes the position as an argument, as .NET
does: `Match(input, startAt)` is `exec` with `lastIndex` set to `startAt`, and `Sticky` anchors it there. There is
no mutable state on a `Regex`, so an instance is safe to share between threads, as .NET's is. `Matches`,
`EnumerateMatches` and `Count` are the global search whether or not `Global` is set (ECMA-262's `matchAll` throws
without `g`; a method named `Matches` does not need the flag to say what it does). `Split` ignores `Global`, as
`RegExp.prototype[Symbol.split]` does.

**The v flag.** The grammar of the v flag changes a class and nothing outside it, so it is parser work that lands
on the nodes the engine already has. A class is read as a value of two parts, its code points and its strings
(each of a length other than one, the empty string among them), with a flag for whether its syntax may hold a
string, which ECMA-262's MayContainStrings decides from the syntax and not from what the strings turn out to be.
A string of one character is a character of the class. The union, the intersection (`&&`) and the difference
(`--`) act on both parts, strings compared whole. With the i flag each operand is case folded as it is read, so
that the complement of a class, and of `\P{...}`, `\W`, `\D` and `\S`, is of the folded set (with the u flag the
fold comes after the complement, which is why `\P{Lu}` with `i` matches `A` under `u` and not under `v`); the
sets hold every form of a character where ECMA-262's hold the folded form, and the two agree under union,
intersection, difference and complement because a set here is a union of whole case classes. A class with strings
compiles to a non-capturing group of an alternation: the strings longest first, each a sequence of characters
(each the set of its forms under `i`), then the set; the matcher, the start searches and the generator see
nothing new. The seven properties of strings are a generated file of their own (`EcmaRegexUnicodeStrings.cs`,
from the two emoji sequence files of Unicode 17, by `tools/unicode/gen-ecma-unicode-strings.ps1`), read the first
time a pattern names one.

### 3.3 Errors, and the members whose algorithm is ECMA-262's

A pattern that is not valid in the grammar of its flags throws `RegexParseException` with the ECMA-262 message and
the offset. A pattern over a limit of size throws a distinct `RegexPatternTooLargeException` (an
`ArgumentException`), never a parse error: a caller must be able to tell "not valid" from "too large".

`Regex.Replace` substitutes as ECMA-262's `GetSubstitution` does: `$$`, `$&`, `` $` ``, `$'`, `$n`, `$nn`,
`$<name>`. .NET's `$_`, `$+` and `${name}` are not recognised and are left as they are, as ECMA-262 leaves them.
`Regex.Split` follows `RegExp.prototype[Symbol.split]`: captures are spliced into the result, an empty match at
the start or the end of the text splits nothing, and `count` is ECMA-262's `limit`. `Regex.Escape` escapes the
ECMA-262 syntax characters and `Regex.Unescape` reverses it. Nothing of .NET's that has no ECMA-262 meaning
(right-to-left matching, pattern whitespace, explicit capture, the culture) exists in the surface, so nothing has
to be refused at run time.

### 3.4 Input and positions

Every matching member has an overload for `string`, `ReadOnlySpan<char>` and `ReadOnlySpan<byte>` (UTF-8). A
`Match` from a string input holds the input and gives `Value` as a substring, as .NET does. A `Match` from a span
is not possible in .NET's shape (a class cannot hold a span), so the span overloads are the ones .NET itself gives
spans and a few more, every one of them allocation-free:

- `IsMatch`, `Count`, and `EnumerateMatches` (`ValueMatch` with `Index` and `Length`), as .NET has them.
- `EnumerateMatches(input, Span<ValueCapture> captures)`: after each `MoveNext` the caller's span holds the match
  as element 0 and what each group captured (`Index`, `Length`, `Success`), so a loop over the matches reads the
  groups without a `Match`. `TryMatch` does the same for one match.
- `TryReplace(input, replacement, Span<T> destination, out int written)` and `Replace(input, replacement,
  IBufferWriter<T> destination)`, with the template expanded as `GetSubstitution` expands it, and
  `Replace(input, evaluator, IBufferWriter<T>)` where the evaluator writes its replacement rather than returning
  a string. `Replace(string, string)` runs the same loop over pooled memory, so the result string is its only
  allocation.
- `EnumerateSplits(input[, count])`: the pieces of `Split` as positions, without the captures `Split` puts
  between them (as .NET's `EnumerateSplits` leaves them out).

`Index` and `Length` are in the unit of the input: chars for UTF-16, bytes for UTF-8.

A UTF-8 input that is not valid UTF-8 is read a byte at a time as U+FFFD, as the Go engine reads it. A UTF-16
input can hold a lone surrogate, and it is matched as ECMA-262 matches it (a code unit without `u`, a code point
of its own with `u`). Nothing is transcoded: there is no UTF-16 copy of a UTF-8 text and no UTF-8 copy of a
string.

### 3.5 Limits and timeouts

A `matchTimeout` as in .NET, default `Regex.InfiniteMatchTimeout`, settable per instance. A pattern without a
backreference is matched in linear time by the memo of section 4.2, and takes no timeout: the clock is never read
for it. A pattern with a backreference can take exponential time, so the timeout applies to it, and a match that
passes the timeout throws `RegexMatchTimeoutException`. A match whose backtracking stack reaches its cap (a
million entries) throws `RegexMatchTimeoutException` too, with a message that says memory, because .NET has no
exception for that and a consumer's `catch` must already hold the case. Limits of size on the pattern (nesting
of two hundred, program size, construction cost) are deterministic, never a clock, and throw
`RegexPatternTooLargeException`.

## 4. Engine design

### 4.1 One matcher, two encodings, no copy

The matcher's methods are generic over the unit of the text and an empty struct that reads it:
`Run<TUnit, TEncoding>(ReadOnlySpan<TUnit> input, ...) where TEncoding : struct, ITextEncoding<TUnit>`, with
`Utf8Encoding : ITextEncoding<byte>` and `Utf16Encoding : ITextEncoding<char>`. Each encoding gives `Decode`,
`DecodeBefore`, `UnitAt` and `UnitBefore` (the UTF-16 code unit at a position, for Annex B matching), the line
terminator tests, and the literal and prefix searches. The JIT specialises every generic method for the struct,
so there is no virtual call and no branch on the encoding in the loop. (A `ref struct` cursor that holds the span
and implements the interface would read the same, but ref-struct interfaces need a .NET 9 or later runtime, and
the netstandard2.0 build compiles the same source.) The Go engine's `units` trick, which places the position
between the halves of a supplementary character at its third byte, is the UTF-8 cursor's code-unit mode. The
UTF-16 cursor's code-unit mode is the natural one.

Literal runs are stored in both encodings at compile time (a few bytes each), so a literal instruction is a span
comparison and, for a literal prefix, an anchored search (section 4.3).

### 4.2 Program and working memory

The program is flat arrays (instruction kind, operands, set tables) in a sealed class the `Regex` holds. The
per-thread working memory (backtracking stack, capture slots, loop counters, memo bitset) is a `[ThreadStatic]`
state, grown to what a match needs and retained up to a cap, as the Rust port does. A small match stackallocs its
capture slots. No closures, no boxing, no LINQ, no iterator blocks on the match path.

The memo of failed places is what keeps a pattern without backreferences linear: a (position, construct) pair
that has failed once is never retried. The bitset costs one bit per position per memoised construct, so it is
taken into use only once a match has made enough steps to pay for it (sixty-four steps per position of the
text), and a match that ends before then never clears it. The clock is read every 4,096 steps, so a match that
ends before the first check never reads it.

### 4.3 Finding where to start

Before the matcher runs, the `Regex` applies what it learned at compile time, in this order: an anchored pattern
starts only at 0; a literal every match starts with, or holds at a fixed distance from its start, or holds after
a leading loop, is found by an anchored search; a first set is found by `SearchValues<byte>` /
`SearchValues<char>` (a table on netstandard2.0); a minimum length rules out short rests. This is the part of
.NET's `RegexFindOptimizations` that applies to ECMA-262 patterns, and it is where most of the time of a search on
a long text goes.

**The anchored search** (`Needle<T>`, one class for `byte` and `ushort`). The span search finds a literal by its
first and last units, and in UTF-8 those are often the units every character of a script shares: every Cyrillic
letter starts with D0 or D1, every common CJK character with E4 to E9, so it compares at nearly every character.
The needle instead picks the two or three positions of the literal that text makes rarest, by a table of how
common each unit is (English letter frequencies for ASCII, and what UTF-8 makes of other scripts: lead bytes
common, continuation bytes spread over sixty-four values), compares those positions over a vector of starts, and
compares the literal in full only where every anchor agrees. The third anchor is lazy: it is looked at only in a
vector where the first two agree somewhere, as .NET's own single-string search does. A unit rare enough on its own
(q, z, a digit, punctuation) is searched for alone by the span search for one value, which is the fastest search
there is. Over UTF-16 a literal matched exactly keeps the span search, which is as fast as any for that, except
where both ends of the literal are common letters: there the span search's first-and-last comparison lets too
many starts through, and the runtime's fingerprint search (`SearchValues<string>`) is used instead (`aei` over
the Sherlock Holmes text: 23 us against 28). The loop that compares the anchors is a static method whose
constants are its arguments and which makes no call, so everything stays in registers; the full comparison is
made outside it.

The same search is case-insensitive. Under the i flag the parser reads a letter as the class of its forms, and a
run of such classes (or characters, or any small class of single characters: `[ab][cd]`) at the start of the
pattern is one needle whose positions each allow up to three values; two forms that differ in the bits of a mask
(a letter's cases differ in 0x20, in ASCII, in UTF-16 for Latin, Greek and Cyrillic, and in the last byte of
UTF-8) are one comparison of the unit with those bits set, and ASCII anchors over UTF-16 are packed thirty-two to
a vector. An anchor is taken wherever a mask joins its forms; a position whose forms no mask joins (the s of
`sherlock`, whose third form is the long s) gives way to the rarest position a mask does join. A form whose
encoding is longer than the character's own (the long s and the Kelvin sign over UTF-8, which are two and three
bytes where s and k are one) cannot be at a fixed position: it is looked for apart, and every start from which the
literal could reach it is tried. This is what makes a Cyrillic literal under the i flag run at a third of .NET's
time over UTF-16 rather than fourteen times it, which is where an `OrdinalIgnoreCase` span search had left it.

**Several literals at once** (`MultiNeedle<T>`): an alternation of literals, the pairs of first characters of a
sequence of classes, and the forms a case-insensitive search leaves out. One position that every literal has is
chosen, or two, by a cost model (the comparisons at every vector against the full comparisons at the starts the
units let through); one position is the span search for any of several values, two are a straight-line loop over
up to six distinct units at each, and over UTF-8 two adjacent positions are compared as one sixteen-bit value,
which admits only the literals' own pairs of bytes rather than every pairing of a unit of one position with a
unit of the other.

**Fingerprints** (`Teddy`). Where no position is rare enough for the needle (the cost model puts it at 0.35 or
more: three or more literals, or any case-insensitive alternation, where a letter's two cases are common
together), the literals go into up to eight buckets, and for each of up to three positions two tables of sixteen
entries map a byte's nibbles to the buckets with that byte there in any of its forms. A vector of starts is looked
up by two shuffles per position, and only the starts whose bytes agree with a bucket at every position are
compared. The positions are the rarest the literals share, so that beyond ASCII the bytes that tell the literals
apart are looked at and not a script's lead bytes; as many of the three as pay, each costing its shuffles at every
vector against the starts it turns away. The loop over the vectors with no start is its own method for each number
of positions, with no call in it, so the six tables stay in registers; a start is handled outside it and compared
against a literal's forms by one vector compare per form where every character is one unit in every form. Over
UTF-16 the same search looks at the low byte of each unit, so it has twice the lanes to look at; the runtime's
own string search (`SearchValues<string>`, which is Teddy too) keeps ASCII literals there, with the forms its
comparison does not know (the long s, the Kelvin sign) looked for apart, and the fingerprint search takes the
rest. Measured on the search alone over UTF-8, three names under the i flag went from 0.26 to 0.06 ms, seven from
0.95 to 0.08, four over sixteen million characters from 12.2 to 1.8, and five Russian names from 1.12 to 0.15.
Below the cost of 0.35 the needle's one anchor position wins by half: two or three literals that start with
capitals (`Sher|Hol`, `Sherlock|Street`, `Holmes|Watson`) search at 0.53 to 0.65 of the runtime's time.

**The literal every alternative holds.** A pattern that is an alternation, or starts with one, whose
alternatives each start with a literal or hold one at a fixed or bounded distance from their start
(`([A-Za-z]awyer|[A-Za-z]inn)\s`, `Tom.{10,25}river|river.{10,25}Tom`, `Sher[a-z]+|Hol[a-z]+`, with or without
the i flag) is searched for by all those literals at once; each occurrence opens the window of starts from which
some alternative could reach it, tried in order and never twice, and where every alternative starts with its
literal the matcher runs at the occurrence alone. Without this, such a pattern is tried at every letter.

**What an enumeration carries.** An enumeration holds the thread's match state across its matches and a cursor
(`ScanCursor`): the range of the text it knows to hold no rare line terminator, so that a run of `.` is one
search for LF and CR with the U+2028 and U+2029 scan done once a chunk ahead; and the range known to hold no form
the program's search leaves out, so that the pass for the long s and the Kelvin sign is one over the text rather
than one per match. A match on its own starts with nothing.

**What this cannot do.** A CJK literal over UTF-8 in a text that fits the cache runs at about 1.2 to 1.5 times
.NET over UTF-16: every character is three bytes and no byte of it is rare, where UTF-16 has one rare unit per
character for the span search to find alone. Over a text larger than the cache the smaller encoding wins back.

### 4.4 The ASCII automaton

For a pattern that needs no backtracking, every state set an ASCII text can lead to is worked out once, lazily,
into a table of at most 256 states, and a match is then one table read per unit, for both encodings alike: an
ASCII text is one table read per unit either way, and any other unit hands the text to the matcher from the start.
The automaton takes `\b` and `\B` (not under the i and u flags together, where they fold): a state carries whether
the unit before was a word character and whether it is at the start of the text, and a boundary is passed when
the next unit is known, so a pattern such as `\b\d{1,2}/\d{1,2}/\d{2,4}\b` is decided by the automaton rather
than the backtracker, which halved its time. The automaton is built on the first match, not when the pattern is
compiled, so a program pays nothing for the patterns it never matches, and the steps of building it are bounded
so that a pattern too large for one costs little before that is known. An automaton over full Unicode is not
built; the benchmarks have not asked for it.

## 5. The source generator

A Roslyn incremental generator with the shape of .NET's: `[GeneratedRegex(pattern, options,
matchTimeoutMilliseconds)]` on a `static partial Regex Name();` method or `partial Regex Name { get; }` property,
with the flags as `RegexFlags` or as letters. It ships inside the same package as an analyzer asset, so one
`PackageReference` gives the library and the generator, as the .NET SDK does; a separate `*.SourceGenerator`
package would make the swap two references instead of one. The generator (`Corvus.Regex.Ecma262.SourceGenerator`,
netstandard2.0, Roslyn 4.12) compiles the library's parser and tables from the same source, so a pattern is
checked at build time by the parser that will run it, and a pattern that is not valid is a build error with the
ECMA-262 message and offset, as .NET reports one: CRE0001 is the message and offset, CRE0002 the member's shape,
CRE0003 the flags, CRE0004 a containing type that is not partial, CRE0005 a pattern over a limit. The generator
and the `System.Memory` its host may lack ship in the package at `analyzers/dotnet/cs`. The AOT sample project
publishes with no trimming warning and runs.

**The instance.** The generated member returns one instance held in a nested static class, made by the `Regex`
constructor the first time it is used rather than from baked arrays. A serialised program would freeze the
program's shape into every consumer's build, and what it saves is one construction of a few microseconds per
process; trimming and AOT are safe without it, since the parser and compiler are plain code. What the generated
code adds is the match and the search as code, below.

**The match as code.** The generator emits each program as structured code from its tree (`TreeEmitter`), in the
shape .NET's own generator uses: every construct that can be retried keeps its state in locals and owns a label,
and whatever follows it jumps to the innermost such label when it fails. An alternation keeps the branch it took
and dispatches a retry to that branch's own backtracking; a loop over one character or class keeps the end and
the floor of its run, takes the run whole (by vector where the class allows) and gives back a character at a
time, or by vector to the last character from which what follows could go on, or not at all when what follows
cannot start with a member; an optional group keeps whether it was taken; a capturing group keeps the slots it
replaced and restores them when it is exhausted; a lookahead is a scope whose body jumps to the lookahead's own
failure. Nothing is pushed for these. The memo marks that keep the interpreter linear are kept at the same points
(the compiler records each construct's point): checked where a construct is entered or gives back, set where it
is exhausted. Inside the match, a run of one-unit terms checks the units it needs once, a short run of a set is
walked in code before the vector search is called, and a run followed by one character gives back to the last
such character by one `LastIndexOf`. What the structured code bought was the attempt: an IP address candidate
went from 37 to 21 ns.

A pattern with a loop over a group other than an optional one, or a lookbehind, or a tree of more than 1,500
nodes, is emitted by the instruction emitter instead (`RunEmitter`: a labelled block per instruction, the
backtracking the interpreter shares). A program of more than 2,048 instructions (`\p{RGI_Emoji}` alone is 2,760
alternatives) is too large for straight-line code at all: the generator writes a runner that hands it to the
interpreter. A strings instruction (a trie over the sequences) would be the better shape for both engines, and
is left for a workload that asks for it.

**The search as code.** .NET's generated source emits the search for starts as pattern-specific code over the
span primitives, with the search object in a `static readonly` field whose exact type the JIT then knows, and
checks a match's least length once. The generator does the same (`TreeEmitter.Finder`): an exact alternation of
ASCII literals is one `IndexOfAny` for the characters at its rarest shared position, a check or two of the other
positions, and `StartsWith` of each literal, or the runtime's needle where no position is rare; an alternation in
its forms is the runtime's search, which holds the string search ignoring case where no form is apart and the
packed fingerprint search where one is, and its pass for the forms apart only under the u flag; a fixed literal
is the needle's rarest unit then `StartsWith`, or the string search where the literal's ends are common letters,
or the runtime's needle where neither applies; a leading literal the same; a literal in its forms is the string
search ignoring case where every form is ASCII and the runtime's needle where one is apart (the needle sees the
three forms of s in one pass); a run of ASCII sets then a literal is `IndexOf` of the literal and a walk back over
the sets in code; and an ASCII first set is `IndexOfAny` or a range search. A search that verified the alternative
(or found the literal that is the whole pattern) gives the match's end, and the generated loop returns the match
without running its body.

Every `SearchValues<string>` the generated code holds is called from a small non-inlined method of its own, never
from the generic find directly. Called from the generic method, the JIT gave the runtime's string search nearly
twice its time (`aei` over Sherlock: 57 us from the find, 30 from the method, 26 by the interpreter); moved to a
method of its own, it runs at the interpreter's time.

**The resume.** For a program that tracks no group and whose generated code finds the starts, an enumeration's
later matches go from the enumerator straight to the runner's loop: the state is prepared again only when
something else used it since the last match, else what a match changes is reset; the cursor is loaded and saved
around the call. The general match entry (a prepared state, a copy of the captures, two calls more) had cost ten
to fifteen nanoseconds a match; this is 2.5.

**The guard.** The emitted search and the resume call members the library has only in its `net10.0` build, so
they are emitted under `#if NET10_0_OR_GREATER`; a .NET Framework consumer takes the `netstandard2.0` build of
the library and the plain paths of the generated code, and gives the same answers. The test project and
the data set runner compile generated code with the `net10.0` symbols defined, so the emitted search is what they
exercise.

## 6. Target frameworks

`net10.0` and `netstandard2.0`. .NET 8 leaves support in November 2026 and .NET 9 left it in May 2026, so neither
is a target nor supported; the `netstandard2.0` build is for .NET Framework. netstandard2.0 is needed twice: the generator is a netstandard2.0 Roslyn component that compiles the same parser and compiler source, and
Corvus.JsonSchema targets netstandard2.0 and tests on net481. The SDK is pinned to 10.0 with `rollForward:
latestFeature`.

The `netstandard2.0` build is tested as a .NET Framework consumer sees it: the test projects and the data set
runner target `net481` beside `net10.0`, the compile phase builds that target on Linux from the reference
assemblies, and the Windows legs of the build and of `dataset.yml` run the same tests and the same data over it.
The generated code those tests compile is compiled with the .NET Framework symbols, so the plain paths of the
generated code are asserted too.

**The modern target never pays for netstandard2.0.** Wherever the current runtime has a faster way, the code
takes it under `#if NET` and netstandard2.0 gets a plain fallback, never the other way round: `SearchValues` and
`IndexOfAny` for first sets, the anchored `Vector256` / `Vector128` searches for literals, `Vector128` scans for
ASCII class loops, static abstract interface members where they remove a cost, `Unsafe` reads where bounds checks
show in a profile. The benchmarks of section 9 run on the modern target; the netstandard2.0 build is held to
correctness (the same data set) and not to the same speed.

## 7. Repository layout

```
Corvus.Regex.Ecma262/
  .github/workflows/build.yml          build, test, package, the documentation site (scripted-build-matrix-pipeline)
  .github/workflows/auto_release.yml   release on merge (pr-autoflow)
  .github/workflows/dataset.yml        the full ECMA-262 test data run (section 8), nightly and on demand
  GitVersion.yml, LICENSE (Apache-2.0), README.md, build.ps1, global.json, nuget.config
  docs/design/design.md                this document
  docs/benchmarks/                     what the benchmarks measure and where their figures are (section 9)
  docs/website/                        the documentation site (Vellum), built and deployed by build.yml
  Solutions/
    Corvus.Regex.Ecma262.slnx
    Corvus.Regex.Ecma262/              the library (Syntax/, Unicode/, Engine/, the public surface)
    Corvus.Regex.Ecma262.SourceGenerator/   netstandard2.0, packed into the library package as an analyzer
    Corvus.Regex.Ecma262.Tests/        MSTest on Microsoft.Testing.Platform
    Corvus.Regex.Ecma262.SourceGenerator.Tests/
    Corvus.Regex.Ecma262.Benchmarks/   BenchmarkDotNet (section 9)
    Corvus.Regex.Ecma262.DatasetRunner/  console: the test data run, interpreted and generated, both encodings
    Corvus.Regex.Ecma262.AotSample/    a native AOT publish of a generated pattern
  tools/unicode/                       gen-ecma-unicode-data.ps1 and gen-ecma-unicode-strings.ps1 (from UCD)
  tools/benchmarks/                    industry-results.py (the record of a run) and the results page generator
```

The build is the endjin ZeroFailed pipeline shared with the other Corvus libraries: warning-free on Linux and
Windows, tests on both, a package on every build and a release on merge to `main`. The default branch is `main`,
protected by a ruleset in the same shape as Corvus.JsonSchema's.

## 8. Testing and the ECMA-262 test data

### 8.1 The data set

`corvus-dotnet/ecma262-regex-test-data` holds V8's verdicts (Node 24, V8 13.6) for every pattern of its families
(validity under both grammars) and `RegExp.prototype.test` answers for the texts of each, with the `i`, `m` and
`s` flags where a family exercises them. It is the oracle for every Corvus port, and this library is held to all
of it, over both encodings, interpreted and as generated code:

| File | Patterns | Tests | For |
|---|---|---|---|
| `hand-written.jsonl` | 783 | 194,649 | The grammar by hand |
| `flags.jsonl` | 600 | 163,800 | `i`, `m`, `s` |
| `generated.jsonl` | 60,000 | 182,000 | Validity and matching, generated from the grammar |
| `anchored-lists.jsonl` | 6,000 | 342,200 | Anchored alternations |
| `empty-loops.jsonl` | 6,838 | 341,900 | Loops over empty bodies |
| `line-terminators.jsonl` | 3,000 | 300,000 | `^`, `$`, `.` at every terminator |
| `word-boundaries.jsonl` | 2,000 | 542,000 | `\b`, `\B` |
| `backreferences.jsonl` | 3,871 | 514,843 | Every form of backreference |
| `lookarounds.jsonl` | 4,499 | 590,167 | Lookahead and lookbehind |
| `case-folding.jsonl` | 2,863 | 407,080 | The two case rules |
| `code-units.jsonl` | 3,000 | 296,940 | Matching by code unit without `u` |
| `lone-surrogates.jsonl` | 1,500 | 69,000 | Lone surrogates on UTF-16 input |
| `engine-faults.jsonl` | 3,885 | 377,812 | Families built around faults found in the engines |
| `unicode-sets.jsonl` | 5,520 | 795,150 | The v flag: each pattern with `v`, with `u` and with neither |
| `captures.jsonl` | 5,188 | 207,485 | `exec`, the global search, `y`, `replace`, `replaceAll`, `split` |
| `test262.jsonl` | 4,907 | 547,818 | The patterns and texts of test262's regular expression tests, with `v`, `u` and neither |
| `test262-lone-surrogates.jsonl` | 3,435 | 10,326 | Those of test262 that hold a lone surrogate, on UTF-16 input |

`captures.jsonl` is what the drop-in surface needs beyond "does it match anywhere": the match's `[start, end]` and
each group's, the sequence of `exec` results of a `g` loop with the ECMA-262 `lastIndex` advance, `exec` with `y`
at a set of start positions, the result of `replace` and `replaceAll` for replacement strings exercising every `$`
form, and `split` with and without a limit. Positions are given in UTF-16 code units, as V8 gives them; the
runner derives the UTF-8 byte positions from the text. A test on which V8 departs from ECMA-262 (a match that
starts inside a surrogate pair; with `i` and `v` a string of one character in `\q{...}` not canonicalized; `\p{ASCII}`
folded without the long s and the Kelvin sign) is marked on the record, by rewriting the pattern to the form V8
gets right and asking it again, and the runner skips it.

The runner asks the `v` grammar of every record that has `unicode_sets` answers, and matches a text with a lone
surrogate as UTF-16 alone, whatever the file. In the files from test262 a record or a test may be marked
`unicode-version`: V8 13.6 has the character database of Unicode 16, test262 and this library that of Unicode 17.
Those are not skipped but judged by test262: a marked record in each grammar its `test262_valid` names, by that
verdict, and its tests by their `test262_answer` in that grammar; a marked test in each grammar its `test262_answer`
names, by that answer. Every other marked record or test is skipped. The header of the runner's `Program.cs` states
these rules, and the runners of the other ports follow them.

### 8.2 How the tests read the data

The data set is not vendored. The engine repository's `dataset.yml` checks out the data repository at a pinned commit
and runs the `DatasetRunner` over it on every pull request, on every push to `main`, nightly and on demand, both
encodings, with 0 wrong, 0 refused-valid, 0 accepted-invalid and 0 at a limit as the pass; a second job runs the same
data on Windows under .NET Framework 4.8.1, over the `netstandard2.0` build. The same runner is run locally before a
change is pushed, in Debug and Release and with every pattern as generated code (the generated pass compiles each
pattern's emitted source with Roslyn and takes about six minutes). The current count is 26,913,813 answers right and 0
wrong in each mode.

### 8.3 The other tests

- Unit tests over the matching members, the captures, the span members, the prefilters, the Unicode tables, the
  validity of patterns in each of the three grammars, and the v flag's grammar by name.
- An allocation test that counts the measuring thread only, with the `[MemoryDiagnoser]` benchmarks at 0 B as
  the proof.
- Every scan test's pattern is built as generated code too, by compiling the generator's output with Roslyn with
  the `net10.0` symbols defined, and both engines are asserted.
- Source generator tests on the Roslyn test harness: generated code compiles, equals the interpreter's answers,
  and the diagnostics match .NET's for the same faults.

## 9. Benchmarks

The benchmarks compare this library with `System.Text.RegularExpressions` on the workloads the .NET team itself
measures and publishes, so the numbers can be read beside theirs. One BenchmarkDotNet project, `[MemoryDiagnoser]`
on everything, three sets:

1. **Industry** (`Industry.cs`, `GeneratedIndustry.cs`). The sets of `dotnet/performance`'s `Perf.Regex.Industry`:
   the Sherlock Holmes text and the Rust `sherlock` patterns, the Leipzig corpus searches, the Mariomka set
   (email, URI, IPv4 over its text) and the rebar names in Russian and Chinese. These are the sets behind .NET's
   own "regex performance improvements in .NET N" tables. Each row is run five ways: .NET's interpreter, .NET's
   source generator, this library's interpreter over UTF-16, and this library's source generator over UTF-16 and
   over UTF-8; `Count` over the text, one call per operation. V8 (Node 24's `RegExp`, the oracle of the test
   data) is a sixth engine over the same texts, by its own harness (`tools/benchmarks/v8/industry.mjs`) under
   the same procedure, counting by a `test` loop; as the engine is ported, each port joins the same page.
2. **Common** (`Common.cs`). `Perf.Regex.Common` from the same repository: `IsMatch`, `Matches` and the other
   members on its patterns.
3. **Pathological** (`Pathological.cs`). Nested quantifiers, overlapping alternatives under a repetition,
   backreferences in loops, on texts made for them: the set where .NET reaches its timeout and the memo of
   section 4.2 does not.

Where an industry pattern uses syntax ECMA-262 lacks (an atomic group, a possessive quantifier, `\A` and `\z`, a
`(?i)` prefix), the benchmark holds the ECMA-262 form of the same expression. Every Corvus row runs under the u
flag, which .NET has no mode for: classes decode surrogate pairs and the i flag folds s to the long s and k to
the Kelvin sign. Counts agree with .NET on 53 of the 57 Industry rows; the four apart are `$` and `.` at CRLF,
where ECMA-262 counts CR as a line terminator, and `\w`, which .NET takes as Unicode letters and digits.

Runs follow a pinned-host procedure: the virtual machine pinned to the performance cores, the run under
`taskset`, a health probe before and after whose reading must fall in an accepted band, nothing else on the host.
A pinned run is the procedure for development, read against the run before it; `tools/benchmarks/industry-results.py`
writes a page's data and table from a run's artifacts, and nothing it writes is committed.

The published figures are on one page, [the benchmark page](https://corvus-oss.org/corvus-regex-ecma262-benchmarks/): each night it runs every Corvus ECMA-262 engine's
harness image on one runner, this library's (`.github/workflows/harness-image.yml` publishes it, BenchmarkDotNet's
default job then V8, then the test data for compliance) among them, so that every figure on it was measured on one
machine in one hour, with each day's data kept. This repository publishes no figures of its own.
`docs/benchmarks/industry.md` says what is measured and where the library is slower and why, and repeats no
figure. The benchmarks never share a solution with the tests, because BenchmarkDotNet and worktrees
do not mix.

**Where the comparison is not like for like.** Three Industry rows stay above .NET's generated code however the
search is built, because each is the price of matching by ECMA-262's rules under the u flag. `.*`: ECMA-262's
`.` excludes CR, the text has CRLF endings, so there are 36,491 matches to .NET's 26,105 at parity per match.
`Sher[a-z]+|Hol[a-z]+` under i: the search must find the long s and the Kelvin sign, so it is the packed
fingerprint search with every form in its tables against .NET's string search that ignores them.
`[a-q][^u-z]{13}x`: a supplementary character among the thirteen is two units, so the distance from a start to
the `x` is not fixed and the walk-back search stands in for a fixed-offset one. A few more rows read either side
of 1.0 from run to run with the two engines at parity.

## 10. Differences a .NET user will meet

- A pattern read without `u` and matched by code unit, over UTF-8, is the only place the two cursors differ in
  meaning. The data set's `code-units.jsonl` holds it on both.
- .NET's `Match` is a class graph (`Match`, `GroupCollection`, `Group`, `CaptureCollection`, `Capture`). The
  string overloads allocate it, as .NET's do; that is the drop-in shape and not a target of the zero-allocation
  claim. The span overloads and the enumerators are where nothing is allocated.
- ECMA-262 has no `Captures` history for a group under a quantifier (only the last iteration's value), so
  `Group.Captures` has one entry.
- The grammar is ECMA-262's: no `(?i)` inline option (ECMA-262 has `(?i:...)` groups), no atomic groups, no
  possessive quantifiers, no `\A` and `\z`, no conditionals, no balancing groups. `\d` is `[0-9]`, `\w` the 63
  ASCII word characters, `\s` ECMAScript's white space and line terminators, and `.` excludes LF, CR, U+2028 and
  U+2029.

## 11. History

The library was built in five phases, each tagged `phase-N` in the repository when its gate was met: the matcher
and both cursors against the data set (phase 1); the whole drop-in surface against `captures.jsonl` (phase 2);
the start searches, with the interpreter at or below .NET's interpreter on the Industry set (phase 3); the source
generator, with generated code at or below .NET's generated code on the Industry set apart from the rows of
section 9 (phase 4); and the v flag against `unicode-sets.jsonl` (phase 5). Adoption in Corvus.JsonSchema, where
the ECMA-262 to .NET translator and the shape matchers that keep patterns away from .NET's engine would go, is
the intended next step and a decision of its own.
