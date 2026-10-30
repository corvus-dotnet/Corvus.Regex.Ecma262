# The compiled regular expression as data

Status: proposed, 2026-10-30. For agreement before any code.

## 1. The problem

Everything a `Regex` holds is a function of five things known when it is written: the pattern, the flags, the
edition, the version of Unicode, and the version of the library. Today the library works all of it out at run time,
for a regular expression made with `new Regex(...)` and for one the source generator wrote alike. The generated
holder calls `new Regex(pattern, flags, ..., new Bounds(), new Captures())`, which parses and compiles the pattern
again, and three more pieces are built on first use:

| Piece | Built | Used by |
|---|---|---|
| The program (instructions, sets, literals, search structures, shape) | in the constructor | everything |
| The DFA over ASCII (`Regex.Automaton`, `Engine/Dfa.cs`) | on the first `IsMatch` from the start | `IsMatch` from the start, for a pattern with no backreference |
| The search automata (`Program.Automata`, `Engine/Adfa.cs`: anchored, forward, skip window, reverse, rest check) | on the first search that wants them | finding a match |
| The capture program (`Regex.CaptureProgram`) | on the first call that reports groups | `Match`, groups, `Replace` with references |

Measured over the 43 patterns the runtime evaluator of Corvus.JsonSchema gives the library in the jsonschema-benchmark
corpora (warm, least of 15, pinned; `corvus-session-tools/regex-build-cost`):

| Step | Total over 43 patterns | Share |
|---|---|---|
| Reading the grammar alone (`IsValidPattern`) | 0.02 ms | |
| The constructor (parse and compile, search structures included) | 0.30 ms | 4% |
| The first `IsMatch`, less a warm one (the DFA) | 6.57 ms | 96% |

A single DFA took from under 1 µs to 787 µs (ui5-manifest's `^([c|C][o|O][v|V][e|E][r|R])|...`). In the
jsonschema-benchmark protocol this is paid in the first pass over the instances: jsconfig's rose from 0.31 to 1.08 ms
and ui5-manifest's from 1.61 to 3.96 ms under native AOT, where no JIT is involved, once the evaluator matched those
patterns with the library.

The generated code does not avoid it. For `IsMatch` from the start, the DFA answers before the generated runner is
asked (`Regex.cs`, `IsMatchUnchecked`), so for a JSON Schema `pattern` over ASCII text the runner is rarely called and
the cost left is the parse and the DFA, both of which the generator had already been in a position to work out.

## 2. The decision this revises

`docs/design/design.md` §4 says: "The generated member returns an instance built by the `Regex` constructor on first
use. A serialised program would freeze the program's shape into every consumer's build to save a few microseconds per
process." The cost is not a few microseconds once the automata are counted, and the coupling it guards against can be
handled (§5). §4 is rewritten in place to describe the new state.

## 3. What changes

1. **A construction option.** `RegexBuildOptions`, a `[Flags]` enum with `None` and `Eager`, on new overloads of the
   constructor and of `TryCreate` (the ECMAScript flags stay letters `new RegExp` takes). With `Eager` the DFA, the
   search automata and the capture program are built in the constructor rather than on first use, so the cost is paid
   where the caller chooses, as with `RegexOptions.Compiled` in .NET. `None` keeps today's behaviour; every existing
   member is unchanged.
2. **The compiled regular expression as data.** The emitter writes the program, the DFA, the search automata and the
   capture program into the generated code as data, and the holder constructs the `Regex` from that data: no parse,
   no compile, nothing built on first use. The pattern is still passed, for `Pattern`, `ToString` and the fallback.
3. **The runtime evaluator of Corvus.JsonSchema** passes `Eager` for every pattern it compiles (a schema is compiled
   once and evaluated many times), and B4's generators emit the data through `CodeGeneration.Sources`.

## 4. What is data and what is built from it

The state divides by whether it can be written down.

**Written as data** (pure values, the same on every machine):
- The instructions (`Inst[]`: op, operands, jump targets, counts, the index of its set), the character sets as their
  ranges (`CharSet.ranges`), the literals, prefixes and required strings in both encodings, the memo points, the
  quick tables, the shape and its set, the counts of groups and loops, the group names, and the flags the compiler
  works out (anchored, units, linear, the window and loop facts in `Program`).
- The DFA: the unit classes, the transition table, the shift, the start state, the accepting and end facts.
- The search automata (`Adfa`): the class tables, the transition tables (`fast`), the end and start facts, the skip
  window and the reverse automaton's tables.
- The capture program, as a second program.

**Built at run time from that data** (objects tied to the machine or the runtime, and cheap):
- `SearchValues<T>` (the sets' and needles' searchers), the needles' and Teddy's tables where they depend on the
  instruction set the processor has (`Needle.packable`, Teddy's packed form), and `MultiNeedle`/`AlternationSearch`
  over their literals. These are made from the literals and ranges the data carries, by the code that makes them
  today. The constructor's whole cost, these included, is the 4% above.

The tree (`EcmaRegexNode`) is not kept for a regular expression made from data; it is needed today only to build the
automata and the capture program, which the data now carries.

## 5. Format and versions

**A versioned binary image in a `ReadOnlySpan<byte>` literal**, read by the library:

```csharp
private static ReadOnlySpan<byte> Compiled => [0x43, 0x52, 0x45, 0x01, /* ... */];

internal static readonly Regex Instance = new(pattern, flags, timeout, edition, unicodeVersion,
    new Bounds(), new Captures(), Compiled);
```

- A `ReadOnlySpan<byte>` literal of bytes is stored in the assembly's data and costs nothing to initialise, under the
  JIT, ReadyToRun and native AOT alike.
- The image starts with a magic number, the format's version, and the facts it was made under: the library version
  that wrote it, the edition, the resolved Unicode version (`RegexUnicodeVersion.Default` resolves per library
  version), and the flags.
- **Mismatch falls back.** If the reading library does not know the format version, or any recorded fact differs from
  what it would resolve now, it ignores the image and parses the pattern as today. A regular expression made either
  way gives the same answers; the image only saves work.
- Why not C# object initialisers: they would bind generated code to the engine's internal types at compile time, so
  a consumer whose generator carries one emitter version (`CodeGeneration.Sources`) and whose application loads a later
  library would fail to build, where the image degrades to today's cost. They would also be large: `Inst` has more than
  twenty members.
- For `[GeneratedRegex]` the generator ships in the library's package, so the image always matches; the version check
  matters for `CodeGeneration.Sources`, whose emitter and the library the application loads can differ.

**The seam.** One new constructor, hidden from editors like the existing generated-code constructors:
`Regex(string pattern, RegexFlags flags, TimeSpan matchTimeout, RegexEdition edition, RegexUnicodeVersion
unicodeVersion, GeneratedRunner? bounds, GeneratedRunner? captures, ReadOnlySpan<byte> compiled)`. It validates the
settings as the others do, then reads the image or falls back.

**Size.** The DFA has at most 255 states and the search automata are bounded likewise; the image is measured per
pattern in the implementation, and a pattern whose image would pass a limit (to be set from the test data's
distribution, for instance its 99.9th percentile) is emitted without one, as today.

**Robustness.** The image is read by code that checks every length and index against the image's bounds and refuses
(falls back) on anything out of range, so a damaged image cannot corrupt the engine's state.

## 6. What is not changed

- Every public member of the library behaves as it does today; `RegexBuildOptions.None` is the default.
- Answers: a regular expression from an image matches exactly as one from the pattern (§7).
- The DFA's construction itself. A single DFA of up to 787 µs suggests the builder could be faster, which would help
  every regular expression made at run time; that is separate work, noted for later.
- The other languages' engines. They take the same step later, each in its own repository, once .NET's is agreed.

## 7. Verification

1. **Round trip.** For every pattern of the ECMA-262 test data, in each grammar, edition and Unicode version: the
   regular expression read from its image equals the one compiled from the pattern, field by field (program, DFA,
   search automata, capture program).
2. **Answers.** The data-set runner gains a third mode, generated with images, over both encodings: the same answers
   as the interpreter, none wrong.
3. **Fallback.** An image with an unknown format version, another library version's resolved Unicode version, or a
   damaged length is ignored, and the regular expression answers as one parsed from the pattern.
4. **Eager.** With `Eager` the first `IsMatch`, search and group report allocate nothing and build nothing.
5. **Allocation.** Constructing from an image allocates only the objects the regular expression keeps.
6. **Performance.** `regex-build-cost` gains a column for construction from an image; the target is that a generated
   regular expression's first match costs what a warm one does. Then the jsonschema-benchmark A/B of Corvus.JsonSchema
   PR B, whose first pass should return to main's.
7. **Native AOT.** The AOT sample is published with an image and its start-up timed.

## 8. Delivery

One pull request on Corvus.Regex.Ecma262, released as 1.0.10 (new members, nothing changed: a patch here). It
carries the option, the image (writer in the emitter, reader in the library), the tests above, `design.md` §4
rewritten, and the generator and setup pages of the documentation updated. Corvus.JsonSchema PR B waits for it.
