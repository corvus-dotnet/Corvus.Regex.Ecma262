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
| C | `corvus-regex-ecma262-rs` (the C ABI crate and its header) | release archives of the shared library and header per platform | the Rust engine | QuickJS `libregexp` (an ECMA-262 engine in C), PCRE2 (not ECMA-262); V8 |
| C++ | `corvus-regex-ecma262-cpp` | a header-only C++20 library over the C ABI (RAII handles, `std::string_view` and `std::u16string_view`, match ranges), with a CMake package, vcpkg and Conan recipes | the Rust engine | `std::regex` (ECMAScript grammar), Boost.Regex in its ECMAScript mode, SRELL (an ECMAScript library), RE2 (not ECMA-262); V8 |
| Ruby | `corvus-regex-ecma262-ruby` | gem `corvus-regex-ecma262`, over the C ABI | the Rust engine | `Regexp` (Onigmo); V8 |
| PHP | `corvus-regex-ecma262-php` | Composer package over FFI to the C ABI | the Rust engine | `preg_*` (PCRE2); V8 |
| Swift | `corvus-regex-ecma262-swift` | Swift package over the C ABI | the Rust engine | `Regex` and `NSRegularExpression`; V8 |
| R | `corvus-regex-ecma262-r` | R package over the C ABI | the Rust engine | base `regexpr` (TRE and PCRE); V8 |
| Fortran | `corvus-regex-ecma262-fortran` | fpm package over the C ABI (`iso_c_binding`) | the Rust engine | none on the platform; V8 |

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

## 3. One benchmark page, and the images that feed it

There is one page of benchmark figures, the central benchmark site at `corvus-oss.org/corvus-regex-ecma262-benchmarks/`
(repository `corvus-regex-ecma262-benchmarks`). An engine repository publishes no page and no results of its own; it
publishes **a harness image**, and the central site runs it.

- **The harness**, `tools/benchmarks/run.sh <workloads.json> <texts dir> <out.json>`, builds the engine and its
  platform's competitors, runs the Industry workloads, runs the ECMA-262 test data for the compliance figures, and
  writes one data file in the results-page shape (the `corvus-benchmark-results-page` skill: `meta`, `impls`,
  `schemas`, `measures`, `results`). `meta` carries the repository, the commit, the run's time and the host as the
  harness recorded them (never typed in), and the engine versions; `impls` are the repository's engines, the
  platform's competitors and V8, each with its `group` (the language) and its compliance (the counts, or
  "not ECMA-262"); `schemas` are the Industry workload ids, the same in every repository (the list comes from one
  place, `workloads.py`); `measures.time` is nanoseconds per `Count`.
- **The image**, `ghcr.io/corvus-dotnet/corvus-regex-ecma262-<lang>`, has the harness as its entry point:
  `docker run --rm -v <workloads.json>:/workloads.json:ro -v <texts>:/texts:ro -v <out>:/out <image>` writes
  `/out/industry.json`. The repository's workflow builds it on every push to main and every release, smoke-runs it
  on two workloads, and publishes `:latest` from main and the version tag from a release.

The central site's nightly job pulls each engine's image at its latest release, runs each in turn on the same
runner (the same CPU and kernel for every engine; the image only carries the engine's own toolchain), and merges the
results (implementations grouped by language, V8 once, every workload matched by id) into one data file and one
page, with each day's merged data kept. Every figure on the page was measured on the same machine in the same hour;
the runner changes from night to night, which the kept daily files show, and V8 in every harness (the same Node)
gives a figure read across two nights a common reference. An engine whose image does not run is shown as "not run
tonight", never filled in from a run elsewhere. An engine has no row until its image is published.

## 4. Order, and the gate at each step

Each step is its own repository, its own pull requests and its own release, in this order, one at a time:

0. **This plan**, the results contract above, and the central benchmark repository building the page from the
   sources that exist (the .NET repository's run, with V8 on it).
1. **Rust.** The highest return: the C ABI crate is the engine under Ruby, PHP, Swift, R and Fortran, and can be
   under Python. The search layer maps onto what Rust already has (`memchr::memmem` is the rarest-byte needle,
   `aho-corasick`'s packed search is the fingerprint search), so this is adoption as much as porting; the
   alternation windows, the case search, the automaton and the memo are carried over. Gate: data set 0 wrong;
   at or below `regex` on every Industry workload where both answer, a row above it only where an ECMA-262 rule
   forces the work, named with its cost; `regress` and V8 on the page; crate and C ABI published.
2. **A standalone library for every language on the Rust crate, before any other engine.** C++, Python, Ruby,
   PHP, Swift, R and Fortran each get their own regex library, in its own repository and on its own package registry
   (section 1), a binding over the Rust C ABI with an API idiomatic to the language. Each repository holds the
   section 2 contents: the data set run through its binding at 0 wrong, the Industry benchmarks against its
   platform's engines and V8, the harness and its image, and its run published to the central site. These are new
   libraries; the Corvus JSON Schema ports are not changed here (that is step 7). No other engine is started until
   Rust is released and every one of these is published.
3. **Java.** Its own engine already; `indexOf` intrinsics for the needle, the Vector API for the fingerprint
   search, generated code through the Java generator we have (under the method-size limit). Same gate.
4. **Go.** The scalar parts of the search layer over `bytes.Index`, which is vectorised in the runtime; assembly
   only where a measured row demands it. Same gate.
5. **Julia.** The search layer with SIMD.jl. Same gate.
6. **Object Pascal and OCaml.** Moved out as they are, held to the data, on the page against their platforms'
   engines; the search layer when a consumer asks.
7. **Phase 6, per library.** When an engine is published: the Corvus library of that language takes the package in
   place of its in-tree engine (or, for .NET, in place of the translator and the shape matchers), with the JSON
   Schema corpora as the conformance check and the evaluator not slower. One pull request per library.

## 5. Decisions taken here, and the ones that are yours

Taken, unless you say otherwise: the repository and package names of section 1; V8 on every benchmark; GraalJS
TRegex and goja as the ECMA-262 competitors on the Java and Go benchmarks, and PCRE2 on the C benchmark; each engine carrying its history out of the JSON Schema
monorepo (a filtered history, as the Rust engine did); the C ABI living in the Rust repository; the central benchmark as its own repository with one job on one runner; the order
of section 4.

Nothing is left open.
