# Compatibility

What a change must do so that code built against an earlier release keeps working, and which check enforces each
rule. The reasons are in [the plan](../plan/image-compatibility.md).

Two things reach a consumer from an earlier release and meet a later one:

- **Generated code.** A holder written by the source generator, or by a code generator that takes
  `Corvus.Regex.Ecma262.CodeGeneration.Sources`, is compiled into the consumer's assembly with the version of the
  emitter it was written by. The application may load any later version of the library. The holder calls public
  members hidden from editors, and passes the library a compiled image of its regular expression.
- **Compiled code.** An assembly built against one version runs against a later one.

Within a major version, both must keep working, and an image must keep being read wherever it is still right.

## 1. Images

An image (`Engine/RegexImage.cs`) records four things, and a reader decides from them whether to read it or to compile
the pattern instead. Either way the answers are the same; an image only saves the work of building.

| What | Where | The reader |
|---|---|---|
| The layout | `FormatVersion` in the header, and the body's sections | Reads every layout from `MinimumFormat` to `FormatVersion`. Skips a section it does not know that is optional, ignores the image for one that is required. |
| The instructions | The opcodes of `Op`, by number | Ignores an image with a number it does not know. |
| The compiler | `CompilerRevision` in the header | Ignores an image from below `MinimumTrustedRevision`. |
| The settings | The flags, the edition and the resolved Unicode version | Ignores an image written for others. |

An ignored image raises the `ImageIgnored` event of the `Corvus.Regex.Ecma262` event source, with the reason, and on
.NET counts it in the `images-ignored` counter.

## 2. What a change does

| Change | What to do | What checks it |
|---|---|---|
| A refactoring, a comment, the validator, anything that changes nothing the compiler writes | Nothing. | The golden check passes unchanged. |
| The compiler writes something different for some pattern, with the same answers (smaller, faster) | Raise `RegexImage.CompilerRevision`. | The golden check fails on changed output under an unchanged revision. |
| The compiler writes something different because what it wrote gave wrong answers | Raise `CompilerRevision`, and set `MinimumTrustedRevision` to the new revision. Say so in the release notes: generated code built before it compiles its patterns when first used, until it is generated again. | The golden check, which then expects the older images to be ignored. |
| A new instruction | Add it to `Op` with the next number. Never reuse or renumber one. | `TheInstructionsKeepTheirNumbers`. |
| An instruction that is to do something different | A new instruction. The old number keeps its old meaning. | `TheInstructionsKeepTheirNumbers`, and the golden check. |
| A new fact the matcher stores in a program or an automaton | Derive it from the facts older images hold, or treat its absence as the conservative value. If neither is possible, write it in a new required section, so older images are ignored rather than misread. | The golden check. |
| Something new that a reader may skip | A new optional section, with a tag no section has had. | The golden check. |
| A change to the header or to how sections are framed | A new `FormatVersion`. Keep reading the older layouts. | The golden check. |
| A new public member | Add it to `PublicAPI.Unshipped.txt` (or `PublicAPI/net10.0/` for one only the .NET build has). | The public API analyzers fail the build otherwise. |
| A change to or removal of a public member, the members generated code calls included | Only in a major version. | The public API analyzers, and package validation against the last release. |
| Dropping a layout, an instruction or a section from what is read | Only in a major version. | The golden check. |

## 3. The checks

- **The golden check** (`tools/compatibility/Test-GoldenImages.ps1`, in `dataset.yml`). For each released version in
  `Solutions/Corvus.Regex.Ecma262.GoldenWriter/ReleasedVersions.txt`, the GoldenWriter is built against that version's
  `CodeGeneration.Sources` package and writes, for a fixed sample of the ECMA-262 test data, the image and (for a
  smaller sample) the whole holder that release generates. The data-set runner then reads them with the library as it
  is (`--golden`). It fails if an image or a holder is not read when it should be, or is read when its revision is
  below the trusted one, if a holder does not compile against the library, if any answer differs from V8's, or if an
  image of the library's own compiler revision holds other than what the compiler writes now. To run it:

  ```powershell
  dotnet build Solutions/Corvus.Regex.Ecma262.DatasetRunner -c Release
  ./tools/compatibility/Test-GoldenImages.ps1 -Data ../ecma262-regex-test-data/data
  ```

- **The unit tests of images** (`CompiledImageTests`): the layouts read and refused, layout 1 from the two releases
  that wrote it, the revisions, the sections, the numbers of the instructions, and the event.
- **The public API** (`Microsoft.CodeAnalysis.PublicApiAnalyzers`): every public member is listed in
  `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt`, and the build fails on one that is not, or on one listed that
  is gone.
- **Package validation** (`EnablePackageValidation`, run by `dotnet pack`): the package is compared with
  `PackageValidationBaselineVersion`, the last release, and the pack fails on any change that would break code built
  against it. `CompatibilitySuppressions.xml` holds only the differences between the package's own `net10.0` and
  `netstandard2.0` builds that 1.0.11 already shipped.

## 4. At a release

Once a release is on NuGet, a pull request that follows it:

1. adds the version to `ReleasedVersions.txt`, so every later change is checked against what it writes;
2. sets `PackageValidationBaselineVersion` to it;
3. moves the entries of `PublicAPI.Unshipped.txt` (and of `PublicAPI/net10.0/PublicAPI.Unshipped.txt`) into the
   matching `PublicAPI.Shipped.txt`.
