# Automatic releases for every package

Every merged pull request releases what it changed, with the next patch number, unless it is labelled `no_release`.
This is what the .NET packages already do through `auto_release.yml`, taken to the packages whose registries read a
version from a manifest: the regex engines' repositories and the ports in Corvus.JsonSchema.

## Decisions

- **The tag is the version.** No manifest in a repository names a released version. The release build writes the
  tag's version into the manifest (`pom.xml`, `Cargo.toml`, the gemspec, `DESCRIPTION`, the C header's
  `CRE_VERSION_*` and so on) and never commits it. The repository holds the ecosystem's form of "not released"
  (`0.0.0-SNAPSHOT` in Maven, which Maven Central refuses; `0.0.0` in Cargo, where a version string and the C
  library's numeric macros must agree). README install snippets name the
  registry's latest version (a badge or a link) instead of a number.
- **Only what changed is released.** Each package has a tag prefix and a list of paths. A package is released when
  its paths changed since its own last tag. In a repository with one package and a harness image, the library's paths
  give a `v` release (library and image) and the harness's alone give a `harness-` release (image only).
- **The next number** is the package's last tag with the patch raised by one. A pull request that needs a minor or
  major version edits the package's `next-version` in `.github/release.json` (as GitVersion's `next-version`); it
  takes effect once, at the first release at or above it. As elsewhere in corvus-dotnet, a minor version is a
  breaking change and a feature is a patch.
- **`no_release`** on a merged pull request releases nothing; its changes ship with the next release. A merge of a
  pull request without it releases every package whose paths changed since that package's last tag, so the changes of
  earlier `no_release` merges are included.

## How a release happens

1. `auto-release.yml` runs when a pull request is merged. Without `no_release`, it works out, for each package, whether
   its paths changed since its last tag and, if so, its next version (`tools/release/next-version.ps1`, the same
   script in every repository).
2. With the endjin bot's token (`ENDJIN_BOT_APP_ID`, `ENDJIN_BOT_PRIVATE_KEY`, granted to every repository), it creates
   each package's tag on the merge commit and a published GitHub release for it. The notes are the titles and links of
   the pull requests merged since the package's last tag that touched its paths. A tag or release made with the
   workflow's own token would start no other workflow, so the bot's token is what starts the publishing.
3. Each registry's publish workflow runs on the release (or on its tag pattern), takes the version from the tag,
   writes it into the manifest, runs the tests (and the test data where the repository has it), and publishes. It still
   asks the registry first, so running it again publishes nothing twice.

The script is copied into each repository rather than shared through a central action, as the engines' repositories
are standalone. Its copies are kept identical.

## What a registry cannot do alone

- **opam**: publishing is a pull request to opam-repository, reviewed by people. The workflow opens it.
- **Julia's General registry**: registration goes through Registrator; the workflow asks it, and TagBot is not used
  because the tag comes first.
- Others (Maven Central, crates.io, PyPI, RubyGems, Packagist, CPAN, the Go proxy, npm) publish from the workflow.

## Order

One pull request per repository, one in CI at a time.

1. **Java** (`corvus-regex-ecma262-java`): one package and a harness image. The pilot.
2. **Rust** (`corvus-regex-ecma262-rs`): the crate and the C library, two packages in one repository, which proves the
   per-package paths before the monorepo.
3. **The bindings over the crate**: Python, Ruby, PHP, R.
4. **Corvus.JsonSchema**: the ports' publish workflows move from "a new version in the manifest" to the tag, under
   their existing prefixes (`rs-v`, `java-v`, `py-v`, `rb-v`, `capi-v`, `php-v`, `r-v`, `ml-v`, `pas-v`, `perl-v`,
   `fortran-v`, `src-go/corvus-json-schema/v`...). The .NET packages keep GitVersion and `auto_release.yml`.

Corvus.Regex.Ecma262 (.NET) already releases this way and does not change.
