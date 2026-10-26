# Plan: the Julia engine

Step 5 of [an engine of this design for every port](engines-for-every-port.md). A Julia package that gives ECMA-262's
answers, with the start searches that make the other engines fast, held to the same test data and on the same
benchmark page.

## What it starts from

**A port of the Rust crate, not the JSON Schema port's engine.** The plan's Julia row says the engine starts from
`src-jl/.../ecmaregex`. That is not an engine. It translates a pattern into PCRE2's syntax and runs it with the PCRE2
that Julia bundles, and it refuses what it can't translate exactly (some lookbehinds, some backreferences, a pattern
PCRE2 won't hold). It has no flags beyond modifier groups, no UTF-16, no `replace` or `split`, and no search layer, and
a refusal is a wrong answer by the rule every engine is held to. The Rust crate (0.2.x) has all of that, and the Java
and Go engines have shown that porting it is the quicker road. So the Julia engine is a port of the crate, as theirs
were. The JSON Schema port's translator is replaced by the new package in step 7. The plan's Julia row is corrected in
the same change.

## Decisions

These are the choices to agree before any code. Each has a recommendation.

1. **The Julia version.** `julia = "1.10"` in `[compat]`, the long-term support release and the floor of
   Corvus.JsonSchema's Julia port, so that step 7 can take the package without raising it. CI tests on 1.10 and the
   latest release (1.13), on Linux, Windows and macOS.
2. **The encodings, in one matcher.** UTF-8 over `String`, `SubString{String}` and byte vectors (`Vector{UInt8}`,
   `codeunits`), since that's what Julia text is. UTF-16 over `Vector{UInt16}` too, as every other engine has both and
   the benchmark page shows both. Julia specialises a method for each concrete argument type, so the matcher is written
   once over `AbstractVector{T}` with `T` a `UInt8` or a `UInt16` and compiled for each, with no template or expansion
   step (the Java and Go engines need one).
3. **Ill-formed UTF-8 is refused.** A Julia `String` can hold bytes that aren't UTF-8. A `String` or byte vector is
   checked on each call (`isvalid`, which is fast), and a program that matches one text many times wraps it once in a
   checked `Text` and pays once, as the Go engine's `Text` and the Java engine's `Utf8Text` do.
4. **The API is Base's.** `EcmaRegex <: AbstractPattern`, built by `EcmaRegex(pattern, flags)` or the string macro
   `ecma"…"flags` (compiled when the code is, as `r"…"i` is), with Base's verbs: `occursin`, `match`, `eachmatch`,
   `findfirst`, `findnext`, `findall`, `count`, `replace(s, re => repl)` (with every `$` form of `GetSubstitution`) and
   `split`. A match is an `EcmaMatch`, read as a `RegexMatch` is (`m[1]`, `m[:name]`, `m.offset`), with Julia's
   1-based indices. A reusable `Captures` matches with no allocation. Errors are exceptions, as Julia's are: a syntax
   error, a match timeout (the .NET model, as every engine has it), and ill-formed text. The global search moves on as
   `matchAll` does.
5. **The search layer, with no dependency first.** The rarest-byte needle over `findnext` on bytes, which Base runs
   through `memchr`; SWAR over 64-bit words where `memchr` doesn't reach (two to four bytes, ranges, UTF-16 units), as
   the Go engine does; loops simple enough for LLVM to vectorise. The multi-literal search, the alternation windows, the
   case search, and the anchored and reverse automata as the crate has them. The plan's Julia step names SIMD.jl. It
   comes in only where a measured row demands it, so the package starts with no dependency, as the JSON Schema port
   has none.
6. **Generated code later.** Julia can compile a matcher for each pattern into native code at run time, as .NET does
   and the Java engine could with ASM. That is deferred until the rows say the interpreter can't reach the gate, as the Java
   engine's ASM was and the Go engine's `go generate`. The cost to weigh then is the compile time of each new pattern.
7. **Tasks.** A compiled pattern is shared freely between tasks and threads. The working memory of a match (its stack,
   its groups, its memo) is kept per task and reused, so a warm match allocates nothing.
8. **The comparison engines.** `Base.Regex` (PCRE2 with its JIT, "not ECMA-262", counted against V8 like any other
   engine) and V8. No ECMA-262 engine exists in Julia's ecosystem to put beside it.
9. **Releases.** The repository `corvus-regex-ecma262-jl`, the package `CorvusRegexEcma262`, registered in Julia's
   General registry, with the version in `Project.toml`. Automatic releases from the first day, as the JSON Schema port's
   `julia-publish` does them: the version bumped on merge, registered through Registrator, and tagged
   `v<version>` once General merges it. The first registration of a new package waits three days in General's queue,
   so 0.1.0 lands three days after its merge.

## The gate

The same as every engine's:

- the ECMA-262 test data at 0 wrong, in both encodings
- at or below `Base.Regex` on every Industry workload where both answer, and a row above it only where an ECMA-262
  rule forces the work, named with its cost
- V8 on the benchmark page, with every engine's wrong count a failure
- `docs/port-checklist.md` with every item of the crate's search layer done or not applicable, as the Java and Go
  engines have
- no allocation per match once the working memory has grown, with a test that says so (`@allocated`)
- the stack probe: the crate's deep, wide and long patterns, none of which may end the process

## Order

One pull request at a time, as before.

1. The repository, the package, CI on the two Julia versions and three systems, the Unicode tables generated from the
   crate's, and the parser, compiler and matcher. Gate: the test data at 0 wrong in both encodings.
2. The search layer and the automata, the port checklist, the allocation test and the stack probe.
3. The harness, its image, and the night run's `sources.json` and `engines.json`.
4. Release 0.1.0 (registered three days later), and the central page takes it from the next night.

Step 7 (the JSON Schema port takes the package in place of its translator) is its own pull request in
Corvus.JsonSchema, after 0.1.0.
