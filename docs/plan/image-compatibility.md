# Compatibility of compiled images, and of the library

Status: proposed, 2026-10-31. Agreed in conversation: one revision floor for compiler fixes (feature bits deferred),
the scope is images and the public API, and Corvus.JsonSchema #1046 (V5.8.0) waits for the release that carries it.

## 1. The problem

An image (`Engine/RegexImage.cs`, see `compiled-regex-as-data.md`) records a fingerprint of the engine that wrote it,
a SHA-256 over the sources of `Engine/`, `Syntax/`, `Unicode/` and the settings types. A reader whose fingerprint
differs ignores the image and parses the pattern.

That is safe and much too strict. Any edit to any of those files, a comment included, changes the fingerprint, so every
image written by an earlier release falls back. 1.0.11 changed only the validator, which plays no part in an image, and
every image Corvus.JsonSchema had generated with 1.0.10 stopped being read. A consumer whose generated code was written
with one version and whose application loads a later, entirely compatible one pays the full cost of construction
without knowing it.

Nothing guards compatibility in the other direction either. The opcodes are numbered by their order in the `Op` enum,
so inserting one silently changes the meaning of every image. The library has no API baseline, so a change that breaks
generated code compiled against an earlier version would merge unnoticed.

## 2. The aim

- **A newer library reads every older image that is still correct.** It falls back only when the image uses something
  the library does not know, or when the compiler that wrote it is known to have been wrong, or when the answers'
  inputs (flags, edition, resolved Unicode version) differ.
- **An older library never misreads a newer image.** It falls back.
- **Code generated against one version compiles and runs against every later version of the same major version.**
- **Every rule is enforced by a test or a CI check**, so a change that breaks one cannot merge by accident.

## 3. What an image depends on

The fingerprint is replaced by four things, each versioned on its own.

### 3.1 The layout: a format version and sections

The header keeps the magic number "CRI" and gains a format version that the reader checks against a range,
`MinimumFormat` to `CurrentFormat`, rather than for equality.

The body becomes a sequence of sections. Each section is a tag, a flag that says whether it is required, a length and
its bytes. The program, the capture program, the DFA and the search automata are sections. A reader:

- skips a section it does not know that is marked optional;
- falls back on a section it does not know that is marked required;
- falls back on a required section it expects that is missing.

New data goes in a new section, so most additions need no new format version. A new format version is for a change to
the header or to the framing itself. Dropping a format from the readable range is a breaking change, made only in a
major version.

### 3.2 The meaning of what is stored: frozen once released

Everything an image stores has a meaning that is fixed once a release has written it.

- **Opcodes have explicit numbers** (`Char = 0`, `CharBack = 1`, ...). A number is never reused or reordered. A change
  to what an opcode does is a new opcode. An image that holds an opcode the reader does not know falls back.
- **Every field keeps its meaning.** This covers the derived facts the compiler stores beside the instructions: the
  shape and its set, the quick tables, the window and loop facts, prefixes, literals and required strings, and the
  tables of the DFA and of the search automata. A matcher that comes to need a fact older images lack either derives it
  from the fields they have, or treats its absence as the conservative value. If it can do neither, the fact goes in a
  new required section, and older images fall back.

### 3.3 The compiler: a revision and a floor

The image records `CompilerRevision`, a number the library raises whenever the compiler's output changes for any
pattern, whether the change makes it smaller, faster or right.

The library also has `MinimumTrustedRevision`. It is raised only when a change fixes output that gave wrong answers.
An image from below the floor falls back, because the program it carries can answer wrongly. An image from a revision
that was only slower still reads, because its answers are right.

One floor for every pattern is deliberately coarse. A fix to case-insensitive backreferences invalidates images of
patterns that have none. Compiler fixes that change answers should be rare now that the data set gates every change. If
they are not, a later section can record which constructs a pattern uses, so that a fix invalidates only the images
that use the construct it fixes. The section framing lets that be added without a new format.

### 3.4 The answers' inputs: unchanged

The flags, the edition and the resolved Unicode version stay in the header and must match exactly. When the library's
default Unicode version moves, an image written under `Default` records the version it resolved to, the reader resolves
`Default` to the new one, and the image falls back. That is right, because the answers differ.

The character sets are stored as ranges, so a change to the library's Unicode tables does not affect an image except
through the resolved version.

### 3.5 What the generated code calls

A generated holder calls the library through public members hidden from editors: the constructor that takes an image,
and the members its generated runners use. Code emitted by the source package of one version is compiled against
whatever later version the application references. These members are public API like any other, under the baseline of
§5, and are never removed or changed within a major version.

## 4. Images already published

1.0.10 and 1.0.11 wrote format 1, with a fingerprint. Their contents mean exactly what the first sectioned format's
sections will mean. The release that brings this model:

- reads format 1 when its fingerprint is one of the two that 1.0.10 and 1.0.11 shipped, and treats such an image as
  revision 1;
- writes the sectioned format.

An image built with either published version keeps being read.

## 5. Guards

| Guard | What it checks | What it catches |
|---|---|---|
| **Golden images** | `tests/golden/images-<version>.bin` holds, for every released version, the images that version wrote for a fixed set of patterns. A test reads every one with the current library and compares its answers with those of the pattern parsed, over the data set's texts for those patterns. An image below `MinimumTrustedRevision` must fall back, and every other image must be read. | A newer library that misreads, or no longer reads, an older image. |
| **Output pinned to the revision** | A test writes the golden patterns with the current writer and compares the bytes with the latest golden file. A difference fails unless `CompilerRevision` (or the format) has been raised, and the failure message points to the guidelines and asks whether the change fixes answers. | Output that changes without a revision, and so without anyone deciding whether the floor must rise. |
| **Opcodes pinned** | A test lists every opcode's name and number. | An opcode inserted, renumbered or reused. |
| **Golden generated code** | `tests/golden/generated-<version>/` holds the holders the emitter of each released version wrote for a few patterns. A test compiles them with Roslyn against the current library and runs them. | A change to the members generated code calls that breaks code generated by an earlier release. |
| **Public API baseline** | `Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, and package validation against the last released package (`EnablePackageValidation`, `PackageValidationBaselineVersion`). | Any public member removed or changed, the hidden generated-code members included, and any binary break against the last release. |
| **The golden file for a release exists** | The release workflow refuses a tag whose version has no golden images and no golden generated code. | A release that would leave no record to test later versions against. |
| **Corvus.JsonSchema** | A test there asserts that every image in its checked-in generated code is read by the version of the library it references. | Generated code that ships with images its own library no longer reads. |

The fixed set of patterns is a few thousand records drawn from the data set across its files and its grammars, chosen
once and never changed, so that every golden file is comparable with every other. Their texts come from the data set.

## 6. A fallback is visible

A fallback never changes an answer, only the cost of construction, so it would otherwise go unnoticed. The library
raises an `EventSource` event when it ignores an image, naming why (format, unknown section, unknown opcode, revision,
settings, damage), and keeps a count. A consumer's own tests or telemetry can see that its generated code has stopped
saving the work it was written to save.

## 7. Guidelines

`docs/contributing/compatibility.md` will say, for each kind of change, what to do and which guard enforces it.

| Change | What to do | Enforced by |
|---|---|---|
| A refactoring, a comment, the validator, anything that changes no output | Nothing. | The output-pinned test passes. |
| The compiler's output changes, answers unchanged (smaller, faster) | Raise `CompilerRevision`. Write a new golden file at release. | Output pinned to the revision. |
| The compiler's output changes because it gave wrong answers | Raise `CompilerRevision` and set `MinimumTrustedRevision` to it. Say so in the release notes. | Output pinned, and the golden images test, which then expects older images to fall back. |
| A new kind of instruction | A new opcode with the next number. | Opcodes pinned. |
| A new stored fact | Derive it from older fields, or default it conservatively, or put it in a new section (required only if neither is possible). | Golden images. |
| A change to the header or the framing | A new format version. Keep reading the old ones. | Golden images. |
| Dropping an old format, opcode or public member | Only in a major version. | Package validation, the API baseline, golden images. |
| A new public member | Add it to `PublicAPI.Unshipped.txt`. | The API analyzers. |

## 8. Delivery

1. This note, agreed.
2. One pull request on Corvus.Regex.Ecma262, released as 1.0.12 (a patch, since nothing a consumer sees changes except
   that more images are read):
   - the sectioned format, `CompilerRevision` and `MinimumTrustedRevision` in place of the fingerprint, with
     `EngineFingerprint.targets` removed;
   - format 1 read for the two published fingerprints;
   - explicit opcode numbers, the same as today's ordinals, so no image changes meaning;
   - the guards of §5, with golden files for 1.0.10 and 1.0.11 written from their published packages, and for 1.0.12;
   - the fallback event;
   - `docs/contributing/compatibility.md`, and `design.md` §4 and `compiled-regex-as-data.md` revised in place to the
     new state.
3. Corvus.JsonSchema #1046 takes 1.0.12, regenerates its checked-in code, and gains the test of §5 that its images are
   read. V5.8.0 then ships.

## 9. Open questions

- **The size of the golden files.** A few thousand patterns at a few kilobytes each is several megabytes per release.
  If that grows too large, the golden images can be compressed, or kept for a smaller set of patterns that still covers
  every opcode and section.
- **The other engines.** The ports do not yet write images. When they do, they adopt the same model, each in its own
  repository.
