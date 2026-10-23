# Contributing

This guide is for someone who wants to build the library, run its tests and gates, and open a pull request. It
covers the tools on Windows, Linux and macOS, the build step by step, the gates every change must pass, and how a
pull request is reviewed and released. The [design document](../design/design.md) describes what is underneath.

## 1. Tools

| Tool | Needed for | Version |
|---|---|---|
| .NET SDK | Everything | 10.0, as `global.json` pins it (`rollForward: latestFeature`, so any 10.0.x feature band serves) |
| PowerShell 7 | The repository build script, the Unicode table generators, the site build | 7.4 or later |
| Git | The repository and the test data | Any recent version |
| Node.js | The test data generator (in the data repository) and the documentation site's search index | 24 for the data generator (the V8 it carries is the oracle); 20 or later for the site |
| Python 3 | Writing the record of a benchmark run | 3.10 or later |
| GitHub CLI | The documentation site only (it fetches the site generator) | Any recent version |

A contributor who only builds the library and runs its tests needs the .NET SDK and Git. PowerShell is needed for
the repository build script and the tools; the site and the benchmark tools need the rest.

### Windows

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.PowerShell
winget install Git.Git
winget install OpenJS.NodeJS          # the site; use the 24 release for the data generator
winget install Python.Python.3.13     # benchmark records
winget install GitHub.cli             # the site
```

Open a new terminal after the installs so that `PATH` is current. The repository builds in Windows Terminal,
PowerShell 7 or Developer PowerShell; Visual Studio 2022 17.14 or later and Visual Studio Code with the C# Dev Kit
both open `Solutions/Corvus.Regex.Ecma262.slnx`.

### Linux

The .NET SDK is best installed with Microsoft's install script, which puts it under your home directory and never
conflicts with a distribution package:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"      # put both lines in your shell profile
```

On Ubuntu and Debian the SDK is also in Microsoft's package feed (`packages.microsoft.com`) as `dotnet-sdk-10.0`;
on Fedora, `dnf install dotnet-sdk-10.0`. Whichever route you take, `dotnet --list-sdks` must show a 10.0 SDK.

PowerShell 7 comes as a package (`powershell` from Microsoft's feed on Ubuntu and Debian, `dnf install powershell`
on Fedora, `snap install powershell --classic` elsewhere) or as a .NET tool: `dotnet tool install --global
PowerShell`. Node.js 24 is in the NodeSource feed or as a binary tarball from nodejs.org; Python 3, Git and the
GitHub CLI (`gh`) are in every distribution's repositories.

The benchmark procedure pins the run to a set of cores with `taskset`, which is part of `util-linux` and already
installed.

### macOS

```bash
brew install --cask dotnet-sdk        # or the 10.0 installer from https://dot.net
brew install --cask powershell
brew install git node@24 python gh
```

Add `$(brew --prefix node@24)/bin` to `PATH` if Homebrew does not link the versioned Node. On Apple silicon the
SDK is native arm64; the vectorised paths run on NEON through `Vector128`, and the searches that have an AVX2 form
(the packed needle and the fingerprint search, section 4.3 of the design) take their `Vector128` form, so every
test passes, but benchmark numbers are not comparable with the published run, which is on x64.

## 2. Getting the sources

```bash
git clone https://github.com/corvus-dotnet/Corvus.Regex.Ecma262.git
git clone https://github.com/corvus-dotnet/ecma262-regex-test-data.git   # beside it: the test data, 230 MB
cd Corvus.Regex.Ecma262
```

The test data repository is the oracle for the engine (design, section 8). It is not vendored, so clone it beside
the library; the commands below assume `../ecma262-regex-test-data`.

## 3. Building and testing, step by step

Every command runs from the repository root. The solution file is `Solutions/Corvus.Regex.Ecma262.slnx`.

1. **Restore and build.** Every project treats warnings as errors, and the .NET analyzers run on the source
   generator's output too, so a clean build is the first gate.

   ```bash
   dotnet build Solutions/Corvus.Regex.Ecma262.slnx
   ```

   The build must end with `0 Warning(s)` and `0 Error(s)`.

2. **Run the unit tests.** The test projects are MSTest on Microsoft.Testing.Platform, so `dotnet test` takes
   `--solution` and does not accept `--nologo` or `-v q`. The matching tests build every pattern as generated code
   as well as interpreted, by compiling the generator's output with Roslyn, and assert both engines.

   ```bash
   dotnet test --solution Solutions/Corvus.Regex.Ecma262.slnx --framework net10.0
   ```

   The test projects target `net481` as well as `net10.0`, so that the `netstandard2.0` build of the library is
   tested as a .NET Framework consumer sees it. The `net481` target builds on every platform (from the reference
   assemblies) but runs only on Windows; on Linux and macOS pass `--framework net10.0` to `dotnet test`, which is
   what the Linux leg of CI does, and leave the `net481` run to the Windows leg.

   To run one test or one class, filter by its fully qualified name:

   ```bash
   dotnet test --solution Solutions/Corvus.Regex.Ecma262.slnx --filter "FullyQualifiedName~CaptureTests"
   ```

3. **Run the test data, interpreted.** The runner takes the data directory and a path for the wrong answers,
   then an optional file filter (`*.jsonl`), the encodings (`both`, `utf16` or `utf8`) and `--generated`.

   ```bash
   dotnet build Solutions/Corvus.Regex.Ecma262.DatasetRunner -c Release
   dotnet Solutions/Corvus.Regex.Ecma262.DatasetRunner/bin/Release/net10.0/Corvus.Regex.Ecma262.DatasetRunner.dll ../ecma262-regex-test-data/data wrong.tsv
   ```

   The last line of the output is the result. The pass is `wrong 0`, `refused-valid 0`, `accepted-invalid 0`
   and `limit 0`; the run takes about fifteen seconds in Release. Each failure is a line of `wrong.tsv`: its kind
   (`WRONG`, `REFUSED`, `ACCEPTED-INVALID` or `LIMIT`), the file, the pattern with its flags, the text, the
   encoding and the expected answer.

4. **Run the test data as generated code.** The same run with every pattern emitted by the source generator,
   compiled with Roslyn and matched by the generated code. It takes about six minutes and some memory.

   ```bash
   dotnet Solutions/Corvus.Regex.Ecma262.DatasetRunner/bin/Release/net10.0/Corvus.Regex.Ecma262.DatasetRunner.dll ../ecma262-regex-test-data/data wrong-generated.tsv '*.jsonl' both --generated
   ```

5. **The repository build script.** The ZeroFailed build the CI runs: it compiles, tests, analyses and packs, and
   with `-Website` builds the documentation site too. It needs PowerShell 7 and fetches its build modules on
   first use.

   ```bash
   pwsh ./build.ps1
   pwsh ./build.ps1 -Website -BasePathPrefix /Corvus.Regex.Ecma262
   ```

   The package lands in `_packages/`. On Linux and macOS give the test task the framework to run, since the
   `net481` tests run only on Windows: `BUILDVAR_TargetFrameworkMoniker=net10.0 pwsh ./build.ps1`. The site build needs the GitHub CLI signed in to an account that can read
   the Vellum releases (`endjin/Endjin.StaticSiteGen`, which is not public); without that access the site step is
   skipped with a warning and the rest of the build is unaffected. See
   [docs/website/DEVELOPMENT.md](https://github.com/corvus-dotnet/Corvus.Regex.Ecma262/blob/main/docs/website/DEVELOPMENT.md) for the site.

6. **The AOT sample.** A native publish of a generated pattern, to show the generator leaves nothing for the
   trimmer to warn about:

   ```bash
   dotnet publish Solutions/Corvus.Regex.Ecma262.AotSample -c Release
   ```

   A native AOT publish needs the platform's C++ toolchain: the Visual Studio C++ workload on Windows, `clang`
   and `zlib1g-dev` on Linux, the Xcode command line tools on macOS.

### On a shared machine

MSBuild starts one node per core and keeps them alive between builds. On a machine that runs other work, cap the
nodes and turn node reuse off: `dotnet build -m:4 -nr:false`, and `MSBUILDDISABLENODEREUSE=1` in the environment
for commands that take no switches, such as `dotnet test`.

## 4. The Unicode tables

The property tables and case folding (`Solutions/Corvus.Regex.Ecma262/Unicode/EcmaRegexUnicodeData.cs`) and the
properties of strings (`EcmaRegexUnicodeStrings.cs`) are generated from the Unicode Character Database by the
scripts in `tools/unicode/`. They are regenerated only for a new Unicode version:

```bash
pwsh tools/unicode/gen-ecma-unicode-data.ps1
pwsh tools/unicode/gen-ecma-unicode-strings.ps1
```

Both files are listed in the library's and the generator's project files by name (the projects do not glob), so a
new generated file is added to both.

## 5. Benchmarks

The benchmarks compare the library with `System.Text.RegularExpressions` on the workloads of `dotnet/performance`
(design, section 9). A quick look at one row runs in a few minutes:

```bash
cd Solutions/Corvus.Regex.Ecma262.Benchmarks
dotnet run -c Release -- --filter '*GeneratedCount*' --job short
```

A run whose numbers are to be compared with the published record follows the pinned-host procedure, on Linux:
the machine pinned to its performance cores, a three-minute settle after the last build, nothing else running,
and the run under `taskset`. `tools/benchmarks/measure.sh <output directory> [filter]` does that, refuses to start
when the load is above 1.5, and keeps the result files; `tools/benchmarks/industry-results.py --out-dir <dir>` then
writes a page's data and table from the `GeneratedCount` CSV, for your own reading (nothing it writes is committed);
`tools/benchmarks/v8` runs the same workloads under Node 24 for the V8 column. The ratios of a run on a different
host are comparable with themselves, never with another host's.

The published figures are on [the benchmark page](https://corvus-oss.org/corvus-regex-ecma262-benchmarks/), which runs this repository's harness image each night
beside the engines of every other source. `harness-image.yml` builds that image on every push to main and every
release, smoke-runs it on two workloads, and publishes it; to try the image yourself, see the `Dockerfile`.

A benchmark run must never share the machine with a build: the SDK's build servers and the JIT's tiering of the
benchmark compete for the same cores and the numbers are worthless.

## 6. What a pull request needs

- **A warning-free build** and the unit tests passing, on the change as pushed.
- **The data set at 0 wrong**, interpreted and generated, for any change to the parser, the compiler, the matcher,
  the searches or the generator, and the result lines in the pull request. `dataset.yml` runs the interpreted
  pass in CI, on every push and pull request and nightly, on Linux over the `net10.0` build and on Windows over
  the `netstandard2.0` build under .NET Framework 4.8.1; the generated pass is yours to run.
- **The case in the data first** for a change to the engine's semantics. The data repository's families are built
  around the faults found, with V8 as the oracle: add the pattern to the family it belongs to (or a new family),
  regenerate with the data repository's `generate.js` and check it with `verify.js`, and pin the new data commit
  in `dataset.yml`. A fault V8 itself has is marked in the data by rewriting the pattern to the form V8 gets
  right and asking it again, never copied into the engine.
- **A measured run** for a change made for speed, following section 5, with the rows it moved in the pull
  request. The structured code of the generator and the interpreter share the engine, so an optimisation of one
  is considered for the other, and a change to the generator is run through the generated data-set pass.
- **A failing test first** for a bug: the test that reproduces it, then the fix, so the test is seen to fail and
  then pass.
- **The design document kept current.** A decision taken while working is written into
  [docs/design/design.md](../design/design.md), which describes the library as it is, with the reason for each
  decision beside it.
- **The XML documentation** on every public member, since the API reference on the site is generated from it and
  `GenerateDocumentationFile` is on.

### Conventions

- The code is C# with nullable reference types on, the .NET analyzers at their warning level treated as errors,
  file-scoped namespaces and `this.` on instance members. A new file follows the style of the files around it.
- Nothing on the match path allocates: no closures, no boxing, no LINQ, no iterator blocks. The allocation test
  and the `[MemoryDiagnoser]` benchmarks hold that line.
- Wherever the current runtime has a faster way, the code takes it under `#if NET` and the `netstandard2.0`
  build gets a plain fallback, never the other way round (design, section 6).
- Anything the source generator emits that calls a `net10.0`-only member is emitted under
  `#if NET10_0_OR_GREATER`, and the test project and the data set runner define those symbols when they compile
  generated code, so the emitted path is what the gates exercise.

## 7. Releases

Merging a pull request to `main` makes a release: the release automation tags the merge with the version
GitVersion computes from `GitVersion.yml`, and the build publishes the package to NuGet. A pull request that
should not make a release (documentation, CI, tooling) carries the `no_release` label. The version is bumped by
editing `next-version` in `GitVersion.yml`: a breaking change to the public surface is a major bump, a feature a
minor one, and a fix a patch.

`main` is protected by a ruleset: changes arrive by pull request with one approving review, the `.NET build
gate` and `ECMA-262 test data` checks must be green on the branch as it will merge, and the branch is never
force-pushed or deleted.
