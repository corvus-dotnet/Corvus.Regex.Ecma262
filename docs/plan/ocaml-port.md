# Plan: the OCaml engine

Step 6 of [an engine of this design for every port](engines-for-every-port.md), after the Object Pascal engine. An
OCaml library that gives ECMA-262's answers, with the start searches that make the other engines fast, held to the
same test data and on the same benchmark page.

## What it starts from

**A port of the Rust crate, not the JSON Schema port's engine.** The plan's OCaml row says the engine is moved out of
`ocaml-corvus-json-schema`. That engine (`src-ml/corvus-json-schema/ecmaregex`) says of itself that it is "a port of
the ecmaregex package of the Go module", the package the Go engine didn't start from. It reads malformed UTF-8 as
U+FFFD, which every UTF-8 interface must now refuse. It has no UTF-16, no flags beyond modifier groups, no `replace` or
`split`, and no search layer. The Rust crate (0.2.x) has all of that, and the Go, Julia and Object Pascal engines have
shown that porting it is the quicker road. So the OCaml engine is a port of the crate, as theirs were, with the Go,
Julia and Pascal files as the nearest templates. The JSON Schema port's engine is replaced by the new library in
step 7. The plan's OCaml row is corrected in the same change.

## Decisions

These are the choices to agree before any code. Each has a recommendation; the fourth is the one with a real
trade-off.

1. **The compiler.** OCaml 4.14 or later with dune 3, the floor of the JSON Schema port, so step 7 can take the library
   without raising it. A 64-bit OCaml only (the matcher packs into the 63-bit `int`), as the port's opam file says.
   CI builds and tests on Linux, macOS and Windows with OCaml 4.14 and 5.x, and with the flambda compilers that
   opam-repository's CI tests every package on (the port learned that the hard way in 0.1.1).
2. **The encodings, in one matcher.** UTF-8 over a `string` or `bytes` with an offset and a length, which is how OCaml
   holds text. UTF-16 over a `string` or `bytes` of UTF-16LE code units, the convention of the standard library's own
   `String.get_utf_16le_uchar` (4.14). Positions are 0-based, in the units of the text (bytes or code units), as
   `String.sub` takes them. The matcher is written once and generated twice at build time by a dune rule, once with a
   byte unit and once with a 16-bit unit, the way the Go template and the Pascal include files are expanded. A functor
   would be the OCaml way to say it, but without flambda `ocamlopt` calls through a functor's argument indirectly, and
   the matcher's inner loop needs its unit reads inlined on every compiler opam-repository tests.
3. **Ill-formed UTF-8 is refused.** A `string` can hold bytes that aren't UTF-8. Text is checked on each call
   (`String.is_valid_utf_8`, 4.14), and a program that matches one text many times wraps it once in a checked
   `Utf8_text.t` and pays once, as the Go engine's `Text`, the Java engine's `Utf8Text` and the Pascal engine's
   `TUtf8Text` do. UTF-16 with lone surrogates is matched as ECMA-262 says, as every engine does.
4. **The searches: OCaml with C kernels (recommended), or OCaml alone.** OCaml has no vector intrinsics, and its
   `String.index_from` is an OCaml loop, not `memchr`. Pascal reached V8 only once its kernels were vector code (SSE2,
   SSSE3 Teddy, AVX2). The recommendation is the same split here: the engine in OCaml, and the few search kernels
   (one to four bytes, ranges, the pair and two-byte searches, Teddy, the skip window's run, the UTF-16 unit) in C,
   called as `[@@noalloc]` externals with untagged arguments, so a call costs a C call and nothing else. The C has a
   portable scalar path for every kernel and the vector paths behind compile-time and run-time CPU checks, as the
   Pascal `Swar` unit has them. The cost: the package needs a C compiler (which `ocamlopt` needs anyway), and it
   cannot run under js_of_ocaml or wasm_of_ocaml without JavaScript versions of the kernels. The alternative is OCaml
   alone (SWAR over `String.get_int64_le`), which keeps those targets but leaves the literal searches several times
   slower than V8. **Yours to choose.**
5. **The API is OCaml's.** A module `Corvus_regex_ecma262` with an abstract `t`. `compile ?flags ?match_timeout pattern`
   returns `(t, error) result`, so a pattern from a schema is tried without an exception and with its own timeout from
   the start (the try-create with a timeout every engine is to have); `compile_exn` raises. Then `is_match`, `exec`
   (a match with its groups, by number and by name), `matches` (a `Seq.t`), `count`, `replace` (with every `$` form of
   `GetSubstitution`), `replace_all` and `split`, each over UTF-8 and over UTF-16. A reusable `captures` value matches
   with no allocation. A match that reaches its limit raises `Match_timeout` or `Match_memory`; text that is not UTF-8
   raises `Invalid_argument`. The match timeout follows the .NET model, with `set_default_match_timeout`.
6. **Threads and domains.** A compiled pattern is shared freely between threads and domains. The working memory of a
   match (its stack, its groups, its memo) is held as the port holds it: in the pattern, taken by an `Atomic` compare
   and set (sound on 4.14 with system threads and on 5.x with domains), with a fresh one for a match that finds it
   taken, and a `scratch` the caller owns for a matcher with no contention and no allocation. The backtracker keeps
   its choices on that heap stack, so no text or pattern can overflow the stack; the stack probe holds that on 4.14
   and on 5.x, whose stacks differ.
7. **No generated code.** OCaml can't compile at run time, and a ppx that compiled patterns at build time would be a
   project of its own. The interpreter is the engine, as Go's and Pascal's are.
8. **The comparison engines.** `re` (ocaml-re, the common pure-OCaml engine, through its Perl syntax) and `pcre2`
   (the binding to PCRE2), both "not ECMA-262" and counted against V8 like any other engine; and V8. The plan named
   `pcre`, the binding to PCRE1, which is end of life; `pcre2` replaces it. No ECMA-262 engine exists for OCaml to put
   beside it.
9. **Releases.** The repository `corvus-regex-ecma262-ocaml`, the opam package `corvus-regex-ecma262` (library
   `corvus_regex_ecma262`), and the version in `dune-project`. A release is the tag `v<version>`, made automatically on
   merge by the shared auto-release, and a pull request to opam-repository, as the JSON Schema port's releases go
   there; and the harness image beside it.
10. **Unicode tables** generated from the Rust crate's (`tables_gen.rs` and `strings_gen.rs`) by a script with a
    `-Check` mode that CI runs, as the Pascal engine does, so the two can't drift.

## The gate

The same as every engine's:

- the ECMA-262 test data at 0 wrong, in both encodings, on OCaml 4.14 and 5.x
- at or below `re` and `pcre2` on every Industry workload where they answer, and a row above them only where an
  ECMA-262 rule forces the work, named with its cost; and V8 compared row by row, as the Pascal engine was
- V8 on the benchmark page, with every engine's wrong count a failure
- `docs/port-checklist.md` with every item of the crate's search layer done or not applicable
- no allocation per match once the working memory has grown, with a test that says so (`Gc.minor_words` around warm
  matches)
- the stack probe: the crate's deep, wide and long patterns, none of which may end the process

## Order

One pull request at a time, as before.

1. The repository, CI, the Unicode tables, the parser, the compiler and the matcher over both encodings, with the
   test data at 0 wrong.
2. The search layer and the automata, the checklist, the allocation test and the stack probe.
3. The harness, its image and the central site's sources entry.
4. The release, 0.1.0, to opam-repository.

Then the R `sherlock: .* /s` row, then the sweep for the ECMAScript 2020 mode and the choice of Unicode version.
