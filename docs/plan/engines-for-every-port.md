# Plan: an ECMA-262 engine of this design for every Corvus port

The .NET engine in this repository is the reference: the grammar and answers of ECMA-262, held to V8 over the test
data, with the start searches, the automaton and the generated code that put it at or below .NET's own engine. The
Corvus JSON Schema ports in Rust, Go, Java, Julia, Object Pascal and OCaml each carry an ECMA-262 engine of the
older design (the backtracker and the automaton, without the search layer), inside their own source trees; the
Python port translates patterns to Python's engines; TypeScript runs the platform's `RegExp`. This plan takes every
engine out into a library of its own under the Corvus organisation, brings it to this design, holds it to the same
test data and the same benchmarks beside the competing engines of its platform, publishes it, and then has each
Corvus library adopt it in place of its hand-rolled one (the design document's phase 6, widened to every port).

## 1. What is built, per language

| Language | Repository | Package | Starts from | Competing engines on the benchmark |
|---|---|---|---|---|
| .NET | `Corvus.Regex.Ecma262` (this one) | NuGet `Corvus.Regex.Ecma262`, released 1.0.0 | done | `System.Text.RegularExpressions`; V8 |
| Rust | `corvus-regex-ecma262-rs` | crate `corvus-regex-ecma262`; crate `corvus-regex-ecma262-capi` (the C ABI, in the same repository) | the engine in `src-rs/corvus-json-schema` | `regex` (the platform's standard, RE2 semantics), `regress` (an ECMA-262 engine); V8 |
| Java | `corvus-regex-ecma262-java` | Maven `io.github.corvus-dotnet:corvus-regex-ecma262` | the engine in `src-java` | `java.util.regex`; GraalJS TRegex (`org.graalvm.regex`); V8 |
| Go | `corvus-regex-ecma262-go` | module `github.com/corvus-dotnet/corvus-regex-ecma262-go` | `src-go/.../internal/ecmaregex` | `regexp` (RE2); goja's `RegExp`; V8 |
| Julia | `corvus-regex-ecma262-jl` | `CorvusRegexEcma262.jl` | `src-jl/.../ecmaregex` | `Base.Regex` (PCRE2); V8 |
| Python | `corvus-regex-ecma262-py` | wheel `corvus-regex-ecma262`, over the C ABI | the Rust engine | `re`, `regex`; V8 |
| Object Pascal | `corvus-regex-ecma262-pas` | the unit, as the Pascal port ships it | `Corvus.JsonSchema.EcmaRegex.pas` | `TRegExpr` (FPC); V8 |
| OCaml | `corvus-regex-ecma262-ocaml` | opam `corvus-regex-ecma262` | the engine in `ocaml-corvus-json-schema` | `re`, `pcre`; V8 |
| C, Ruby, PHP, Swift, R, Fortran | none of their own | the C ABI crate, consumed as the JSON Schema C library is | the Rust engine | QuickJS `libregexp` (an ECMA-262 engine in C), through the C benchmark |

The names follow the JSON Schema ports (`corvus-json-schema-<lang>`). V8 (Node 24, the oracle of the test data) is on
every benchmark as the common reference; it is the engine the patterns were written for.

## 2. What every engine repository holds

The shape of this repository, cut down to the engine:

- **The engine**, moved out of the port's tree with its history (`git filter-repo` on the path), under the
  language's own layout and package conventions. The run's facts on every results page (host, runtime, job) are
  taken from the harness's own record of the run, never typed in.
- **The design brought over**: the start-search layer (the anchored needle with the rarest positions and the lazy
  third anchor, the multi-literal needle, the fingerprint search, the alternation windows), the case-insensitive
  search with every form, the automaton with word boundaries, the memo that keeps patterns without backreferences
  linear, the allocation discipline, and where the language has a build-time step, the generated match code and
  the emitted search. Sections 4 and 5 of the [design document](../design/design.md) are the specification; the
  per-port checklist of [sweeping optimisations across ports] is the method: every optimisation of the reference
  listed, each marked done, not applicable or to do, nothing skipped silently.
- **Compliance**: the [ECMA-262 test data](https://github.com/corvus-dotnet/ecma262-regex-test-data) run in CI,
  pinned to a commit, both encodings where the engine has both, interpreted and generated where it generates: 0
  wrong, 0 refused-valid, 0 accepted-invalid, 0 at a limit. The same runner is pointed at every competing engine
  on the benchmark that claims ECMA-262 (V8, `regress`, TRegex, goja, QuickJS), and the results page publishes each
  engine's compliance beside its figures: the counts of right, wrong, refused and accepted-invalid for an engine
  that claims the grammar, and "not ECMA-262" for one that does not (`System.Text.RegularExpressions`, `regex`,
  `java.util.regex`, Go's `regexp`, PCRE2, `re`); a reader sees what a fast engine is fast at.
- **Benchmarks**: the Industry workloads (`tools/benchmarks/v8/workloads.py` is the list, from the benchmark
  project's own definitions) run against the platform's standard engine and its ECMA-262 competitors, by a harness
  in the language, writing the results-page data file (the `corvus-benchmark-results-page` skill's shape); the
  page on the repository's site, and the data sent to the central benchmark below.
- **Release**: the port's existing release path (crates.io, Maven Central, Go modules, the Julia registry, PyPI,
  opam), the same CI shape as here, a tag per release.

## 3. The central benchmark

One repository, `corvus-regex-ecma262-benchmarks`, holds the Industry workload definitions and the texts, and one
workflow job on one GitHub-hosted runner builds and runs **every** engine and competitor (each language's toolchain
installed in that job) each night, so that the figures of a run are comparable across languages as well as within
one. It writes one results page (the implementations grouped by language, which the page already does) to
`corvus-oss.org/regex-benchmarks/`, keeping each day's data. Figures across languages on one runner are the only
cross-language figures published; the per-repository pages compare an engine with its own platform's.

Until an engine is published, its row is absent; the page gains a row per release.

## 4. Order, and the gate at each step

Each step is its own repository, its own pull requests and its own release, in this order, one at a time:

0. **This plan**, and the central benchmark repository with the workloads, the texts, the results page, and the two
   engines that exist (.NET and V8) on it.
1. **Rust.** The highest return: the C ABI crate is the engine under Ruby, PHP, Swift, R and Fortran, and can be
   under Python. The search layer maps onto what Rust already has (`memchr::memmem` is the rarest-byte needle,
   `aho-corasick`'s packed search is the fingerprint search), so this is adoption as much as porting; the
   alternation windows, the case search, the automaton and the memo are carried over. Gate: data set 0 wrong;
   the Industry set against `regex`, `regress` and V8 on the page; crate and C ABI published.
2. **Java.** Its own engine already; `indexOf` intrinsics for the needle, the Vector API for the fingerprint
   search, generated code through the Java generator we have (under the method-size limit). Same gate.
3. **Go.** The scalar parts of the search layer over `bytes.Index`, which is vectorised in the runtime; assembly
   only where a measured row demands it. Same gate.
4. **Julia.** The search layer with SIMD.jl. Same gate.
5. **Python.** A wheel over the C ABI; the translator retired. Gate: the data set through the wheel; `re` and
   `regex` on the page.
6. **Object Pascal and OCaml.** Moved out as they are, held to the data, on the page against their platforms'
   engines; the search layer when a consumer asks.
7. **Phase 6, per library.** When an engine is published: the Corvus library of that language takes the package in
   place of its in-tree engine (or, for .NET, in place of the translator and the shape matchers), with the JSON
   Schema corpora as the conformance check and the evaluator not slower. One pull request per library.

## 5. Decisions taken here, and the ones that are yours

Taken, unless you say otherwise: the repository and package names of section 1; V8 on every benchmark; the C ABI
living in the Rust repository; the central benchmark as its own repository with one job on one runner; the order
of section 4.

Yours:

- The central benchmark's home and name (`corvus-regex-ecma262-benchmarks`, published at
  `corvus-oss.org/regex-benchmarks/`), or inside this repository's site instead.
- Whether the engines leave the JSON Schema monorepo's history behind (a clean start, the port's history staying
  where it is) or carry it (a filtered history per engine).
- Whether GraalJS TRegex and goja are worth their weight on the Java and Go benchmarks (each is a large dependency
  for a competitor row), or V8 alone is the ECMA-262 reference there.
