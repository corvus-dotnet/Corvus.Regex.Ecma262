# Plan: the Go engine

Step 4 of [an engine of this design for every port](engines-for-every-port.md). A Go library that gives ECMA-262's
answers, with the start searches that make the other engines fast, held to the same test data and on the same
benchmark page.

## What it starts from

**A port of the Rust crate, not the JSON Schema port's engine.** The plan's Go row says the engine starts from
`src-go/.../internal/ecmaregex`. That engine is real, but it was built for JSON Schema and nothing else. It matches
UTF-8 only, takes no flags beyond modifier groups, translates what it can to the standard library's `regexp` (RE2) and
backtracks over the rest, and reads malformed UTF-8 as U+FFFD, which we now refuse everywhere. It has no UTF-16, no
flags, no `replace` or `split`, and no search layer. The Rust crate (0.2.x) has all of that, and the Java engine has
already shown that porting it is the quicker road. So the Go engine is a port of the crate, as the Java one was. The
crate came from that Go engine in the first place (its files still follow the Go names), but it has had far more work
since, and the port carries all of it. The JSON Schema port's engine is replaced by the new module in step 7. The plan's
Go row is corrected in the same change.

## Decisions

These are the choices to agree before any code. Each has a recommendation.

1. **The Go version.** `go 1.25` in `go.mod`, the same as Corvus.JsonSchema's Go port, so that step 7 can take the
   module without raising that port's floor. CI builds and tests on 1.25, 1.26 and 1.27.
2. **The encodings.** UTF-8 first, over both `string` and `[]byte`, since that's what Go text is. UTF-16 over
   `[]uint16` too, as every other engine has both and the benchmark page shows both. One matcher template, expanded per
   encoding by `go generate`, as the Java engine expands its template per encoding.
3. **Ill-formed UTF-8 is refused.** A `string` or a `[]byte` is checked on each call. A program that matches one text
   many times makes a `Text` once (`NewText`, which checks it) and pays once, as the Java engine's `Utf8Text` does.
4. **The API.** The method set of the standard library's `regexp` (`MatchString`, `FindStringIndex`,
   `FindAllStringIndex`, `FindStringSubmatchIndex`, `ReplaceAllString`, `Split` and their `[]byte` forms), so it reads
   as Go reads, with `Compile(pattern, flags)` for ECMA-262's flags, and a reusable `Captures` value for matching with
   no allocation. A match timeout as the other engines have it (the .NET model). Since `regexp`'s methods return no
   error, a pattern with a timeout uses `Try` forms that return `(result, error)`.
5. **The search layer, scalar first.** The rarest-byte needle over `bytes.IndexByte` and `bytes.Index`, which the Go
   runtime already vectorises in assembly. The multi-literal search, the alternation windows, the case search, and the
   anchored and reverse automata as the crate has them. SWAR over 8-byte words where a runtime search doesn't reach.
   Assembly only where a measured row demands it, as the plan says.
6. **Generated code later.** Go has no runtime code generation, but `go generate` could emit a matcher per pattern.
   That comes only if the rows say the interpreter can't reach the gate, as with the Java engine.
7. **The comparison engines.** Go's `regexp` (RE2, "not ECMA-262"), goja's `RegExp` (an ECMA-262 engine, which sits on
   RE2 and on dlclark's `regexp2`, a port of .NET's engine), and V8. goja's compliance is judged on the test data like
   any ECMA-262 engine's.
8. **Releases.** The module is `github.com/corvus-dotnet/corvus-regex-ecma262-go`, released as the Go module proxy reads
   it, by a `v<version>` tag. Automatic releases from the first day (`tools/release`), with the version stamped into a
   `Version` constant, since `go.mod` has no version of its own.

## The gate

The same as every engine's:

- the ECMA-262 test data at 0 wrong, in both encodings
- at or below `regexp` on every Industry workload where both answer, and a row above it only where an ECMA-262 rule
  forces the work, named with its cost
- goja and V8 on the benchmark page, with every engine's wrong count a failure
- `docs/port-checklist.md` with every item of the crate's search layer done or not applicable, as the Java engine has
- no allocation per match once the working memory has grown, with a test that says so

## Order

One pull request at a time, as before.

1. The repository, the module, CI on the three Go versions, the Unicode tables generated from the crate's, and the
   parser, compiler and matcher. Gate: the test data at 0 wrong in both encodings.
2. The search layer and the automata, the port checklist, the allocation test and the stack probe.
3. The harness, its image, and the night run's `sources.json` and `engines.json`.
4. Release 0.1.0, and the central page takes it from the next night.

Step 7 (the JSON Schema port takes the module in place of its engine) is its own pull request in Corvus.JsonSchema,
after 0.1.0.
