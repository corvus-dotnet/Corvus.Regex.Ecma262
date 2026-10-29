# The getting-started outline

Every language's getting-started page has the same standard topics, with the same headings, in the same order. Each
topic covers the same ground in that language's terms, using its own capabilities (a .NET span, a Rust iterator, a
Ruby block). After the standard topics come the language's extra topics, which no other language needs.

The .NET page is in this repository (`docs/website/site/content/GettingStarted/`); every other language's is
`docs/getting-started.md` in its own repository, fetched by the site from its main branch, with every example run by
that repository's tests. The pages on main describe the latest release, so a change to them alone is merged with
`no_release` and published without a release of the library.

## The page

The title is `Getting started with <language>`. Code is the language's own: idiomatic names and the usual way of
showing a result (an assertion, a `# =>` comment), as long as the tests check it.

## The standard topics

### 1. Introduction

- What the library is, in that language: a package, an extension, a header, a binding over the crate or a port.
- That it answers as ECMAScript does where the language's own regex library does not, so a pattern from JavaScript or
  a JSON Schema matches the same texts.
- "This guide covers:" then one bullet for each topic of the page, standard and extra, in order.

### 2. Setup

- The install command, and what it installs (one package, the native builds there are).
- The versions of the language, compiler or runtime it needs, and the platforms.
- The import, `use`, `#include` or `require` a program writes.

### 3. Patterns and flags

- The flags table: `d`, `g`, `i`, `m`, `s`, `u`, `v`, `y`, with a column for the language's own names when it has an
  enumeration of them, and the meaning of each, as the .NET table gives it.
- Three example patterns: a property escape with `u`, a set operation with `v`, named groups.
- No flag means the Annex B grammar, matched by UTF-16 code unit.
- JSON Schema's reading, the `u` flag first and Annex B when that refuses, with the language's form of compiling that
  does not throw (or its catch when it has none), each compile given a match timeout.
- Errors: what an invalid pattern raises (the reason and, where given, the offset), invalid flags, and the limits of
  size and what passing one raises.

### 4. The edition and the version of Unicode

- First, the defaults, by the language's names: today's grammar (ECMAScript 2025) and V8's version of Unicode (17
  today), and that either is overridden when the pattern is compiled, independently of the other.
- `Es2020`, chosen in place of the default, and what it refuses: the `d` and `v` flags, modifier groups, a group name
  used twice, property names outside the 2020 tables.
- The versions of Unicode (13, 17, 18, latest, and the default that is V8's), what they decide, and how to read back
  the version a pattern was compiled with.
- That both are read when the pattern is compiled, so a match costs the same.
- One example compiling with both, and one showing a property that the older version does not have.

### 5. Matching

- The members, with the language's names: test, the first match, its groups by number and by name, the global search,
  the count, replace (with the `$` forms and, where the language has one, a callback) and split.
- The start position, where ECMA-262 keeps `lastIndex`, and the `y` flag.
- What allocates and what does not, where the language gives a choice.
- That a compiled pattern holds no mutable state, and is shared between threads.

### 6. Timeouts

- A match has no limit by default.
- The match timeout of a pattern, and what reaching it raises; that it bounds the backtracking memory too.
- The default timeout for every pattern made after it is set, and the per-call limit where there is one.
- That a pattern without a backreference is matched in time linear in the text, and no pattern can overflow the stack.
- One example of a timeout reached.

### 7. UTF-8 and UTF-16

- The text types of each encoding, and that nothing is transcoded.
- Positions: the units of each encoding, and any other unit the language gives (Ruby's characters).
- That UTF-8 is checked, and text that is not UTF-8 is refused, never read as U+FFFD; what refusing raises.
- Lone surrogates in UTF-16.
- Without `u` or `v`, a match by UTF-16 code unit, and what that means over UTF-8.
- One example giving the same match over both encodings.

## The extra topics

After the standard topics, in this order:

- The language's own capabilities that the standard topics do not cover: .NET's source generator, C's ownership of
  what the library returns.
- `Differences from <the language's regex library>`: what a user coming from it will meet, in grammar, character
  classes, case, replacement, split, positions and limits.
