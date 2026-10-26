# Plan: the Object Pascal engine

Step 6 of [an engine of this design for every port](engines-for-every-port.md). A Free Pascal library that gives
ECMA-262's answers, with the start searches that make the other engines fast, held to the same test data and on the
same benchmark page.

## What it starts from

**A port of the Rust crate, not the JSON Schema port's engine.** The plan's Pascal row says the engine is moved out of
`Corvus.JsonSchema.EcmaRegex.pas`. That engine is a port of the old Go `ecmaregex` package, the one the Go engine
didn't start from. It reads ill-formed UTF-8 as U+FFFD, which every UTF-8 interface must now refuse. It has no step
budget, so a pattern with nested quantifiers can run for ever. It has no flags beyond modifier groups, no UTF-16, no
`replace` or `split`, and no search layer. The Rust crate (0.2.x) has all of that, and the Go and Julia engines have
shown that porting it is the quicker road. So the Pascal engine is a port of the crate, as theirs were, with the Go and
Julia files as the nearest template. The JSON Schema port's engine is replaced by the new library in step 7. The plan's
Pascal row is corrected in the same change.

## Decisions

These are the choices to agree before any code. Each has a recommendation.

1. **The compiler.** Free Pascal 3.2.2 or later, the floor of the JSON Schema port, so step 7 can take the library
   without raising it. The source is in the Delphi dialect and written to be Delphi-compatible, but not compiled with
   Delphi, as the port says of itself. CI builds and tests on Linux, macOS and Windows (x86_64), with the port's
   install steps.
2. **The encodings, in one matcher.** UTF-8 over `RawByteString`/`UTF8String` and over a pointer and a length. UTF-16
   over `UnicodeString`, which is Pascal's own UTF-16 string, and over a pointer and a length. The matcher is written
   once in include files and compiled twice, once with a byte unit and once with a word unit, the way the Go engine's
   template is expanded. Free Pascal's generics would do it too, but 3.2.2 inlines poorly through generic routines,
   and the matcher's inner loop needs the inlining.
3. **Ill-formed UTF-8 is refused.** A `RawByteString` can hold bytes that aren't UTF-8. Text is checked on each call,
   and a program that matches one text many times wraps it once in a checked `TUtf8Text` record and pays once, as the
   Go engine's `Text` and the Java engine's `Utf8Text` do. UTF-16 with lone surrogates is matched as ECMA-262 says, as
   every engine does.
4. **The API is Pascal's.** An interface, `IEcmaRegex`, reference counted as the port's `IJsonSchemaValidator` is,
   built by `CompileEcmaRegex(Pattern, Flags)`. Its methods follow `TRegEx` in Delphi's `System.RegularExpressions`,
   which is the shape Pascal programmers know: `IsMatch`, `Match` (a `TEcmaMatch` record with `Index`, `Length`,
   `Value`, `Groups[i]` and `Groups['name']`, 1-based as Pascal strings are), `Matches` (a `for … in` enumerator),
   `Replace` (with every `$` form of `GetSubstitution`), `Split` and `Count`. A reusable `TEcmaCaptures` matches with no
   allocation. Errors are exceptions: `EEcmaRegexSyntax`, `EEcmaRegexFlags`, `EEcmaRegexTooLarge`,
   `EEcmaRegexEncoding`, `EEcmaRegexTimeout` and `EEcmaRegexMemory`, all under `EEcmaRegex`. The match timeout follows
   the .NET model, as every engine has it.
5. **Every array read is checked.** Range checking is on in every unit, as in the port. Where Free Pascal's own check
   costs a call on a hot path, the check is written as one inline comparison in a small accessor, and a test calls
   each accessor out of range. This is the port's rule, carried over.
6. **The search layer, with no assembler first.** The rarest-byte needle over `IndexByte` and `IndexWord`, which the
   RTL already runs in assembler on x86_64 (to be measured in step 2). SWAR over 64-bit words where those don't reach (two to four bytes, ranges,
   UTF-16 pairs), as the Go engine has it. The multi-literal search, the alternation windows, the case search, and the
   anchored and reverse automata as the crate has them. Free Pascal has no vector intrinsics. Inline SSE2 assembler
   comes in only where a measured row demands it, as Julia's `llvmcall` did, and only on x86_64 with the SWAR path kept
   for every other target.
7. **No generated code.** Free Pascal can't compile at run time, and a build-time generator for each pattern would ask
   every user to run a tool. The interpreter is the engine, as Go's is.
8. **Threads.** A compiled pattern is shared freely between threads. The working memory of a match (its stack, its
   groups, its memo) is kept per thread in a `threadvar` and reused, so a warm match allocates nothing. The backtracker
   keeps its choices on that heap stack, so no text or pattern can overflow the thread's stack. The stack probe holds
   that.
9. **The comparison engines.** `TRegExpr` (the `RegExpr` unit that ships with Free Pascal, "not ECMA-262", counted
   against V8 like any other engine) and V8. No ECMA-262 engine exists for Pascal to put beside it.
10. **Releases.** The repository `corvus-regex-ecma262-pas`, the public unit `Corvus.Ecma262.RegularExpressions` (the
    .NET namespace) with the rest under it, and the version as a constant in that unit. Pascal has no registry this
    could publish to, so a release is the tag `v<version>` and its source archives on GitHub's releases page, made
    automatically on merge as the port's `pascal-publish` makes them, and the harness image beside it. A Lazarus
    package (`.lpk`) for the Online Package Manager can follow if anyone asks.

## The gate

The same as every engine's:

- the ECMA-262 test data at 0 wrong, in both encodings
- at or below `TRegExpr` on every Industry workload where both answer, and a row above it only where an ECMA-262 rule
  forces the work, named with its cost
- V8 on the benchmark page, with every engine's wrong count a failure
- `docs/port-checklist.md` with every item of the crate's search layer done or not applicable
- no allocation per match once the working memory has grown, with a test that says so (the heap manager's counters)
- the stack probe: the crate's deep, wide and long patterns, none of which may end the process

## Order

One pull request at a time, as before.

1. The repository, CI on the three systems, the Unicode tables generated from the crate's, and the parser, compiler
   and matcher. Gate: the test data at 0 wrong in both encodings.
2. The search layer and the automata, the port checklist, the allocation test and the stack probe.
3. The harness, its image, and the night run's `sources.json` and `engines.json`.
4. Release 0.1.0, and the central page takes it from the next night.
