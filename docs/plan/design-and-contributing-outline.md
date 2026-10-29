# The design and contributing outlines

Every language has a design document and a contributing guide beside its getting-started page, each with the same
sections, in the same order, as .NET's (`docs/design/design.md` and `docs/contributing/contributing.md` here). Each
section covers the same ground in that language's terms. A language whose engine is the Rust crate (Python, Ruby and
PHP bind the crate itself; C is the crate behind a C ABI; C++, Swift, Fortran and R bind the C library) says so where
the engine is described, and points to the Rust crate's design for it, rather than describing it again.

Every other language's documents are `docs/design.md` and `docs/contributing.md` in its own repository, fetched by
the site at its latest release, as `docs/getting-started.md` is. Both describe the library as it is, never its
history. The repository's working documents (port checklists, design notes) are not published.

## The design document

The title is `<library>: design`, and the opening sentence says it is an outline of the library as it is, with a link
to the getting-started page.

1. **What it is.** The library in that language (a port of the Rust crate, or a binding to the crate or to the C
   library), what it offers, and why ECMA-262.
2. **Public surface.** The names and shapes, and why (the language's idioms, the native library a user knows); the
   flags; the edition and the version of Unicode; errors; the ECMA-262 algorithms (`replace`, `split`); the input
   types and their positions; what allocates and what does not; the limits (timeout, sizes, stack).
3. **Engine.** For a port: the matcher, the searches for where a match can start, the automata, any vector code, and
   how one matcher serves both encodings. For a binding: what the binding does between the language and the engine
   (handles, how text is passed without a copy, how errors cross, threads), with the engine left to the Rust crate's
   design.
4. **The language's own parts**, when there are any (as .NET has its source generator): a build-time feature, a
   native extension's packaging.
5. **Targets and layout.** The versions and platforms, the packages built, and the repository's layout.
6. **Testing.** The ECMA-262 test data (what is run, where, over which encodings and settings), and the other tests.
7. **Benchmarks.** The harness image, the central benchmark page, and the rows that are not like for like, with why.
8. **Differences a user will meet.** The few that matter most, short, with the getting-started page's section as the
   full list.

## The contributing guide

The title is `Contributing`, and the opening paragraph says who it is for and what it covers, with a link to the
design document.

1. **Tools.** A table of each tool, what it is needed for and its version, then how to install them on Windows, Linux
   and macOS (or the platforms the language supports).
2. **Getting the sources.** The repository, and the test data beside it.
3. **Building and testing, step by step.** Each command, from a clean clone: build, the unit tests, the
   getting-started page's examples, the API reference's check, the test data, and any formatting or lint gate.
4. **The language's own parts**, when there are any (as .NET has the Unicode tables): generated tables, the C
   library a binding is built against, a native toolchain.
5. **Benchmarks.** How to run the harness locally, and that the published figures are the central page's.
6. **What a pull request needs.** The gates, the case in the test data first for a change to the engine's semantics,
   a failing test first for a bug, the design document kept current, the API documentation; then the conventions of
   the code.
7. **Releases.** What a merge releases, `no_release`, how a minor or major version is asked for, and the ruleset on
   `main`.
