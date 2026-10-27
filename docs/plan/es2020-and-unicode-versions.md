# Plan: the ECMAScript 2020 grammar and a choice of Unicode version, in every engine

A sweep through every engine of [an engine of this design for every port](engines-for-every-port.md), before the ARM
sweep and before phase 6. It gives each engine two settings, chosen independently of each other: the edition of the
grammar (today's, or ECMAScript 2020's) and the version of Unicode its character database holds (13, 17, 18, or the
latest the engine has). The same sweep gives every engine a create that takes a match timeout and returns an error in
place of throwing, and moves the reference Node to its security release.

## Why

Phase 6 replaces the regular expressions of Corvus.JsonSchema and its ports with these engines. JSON Schema 2020-12
says a pattern SHOULD be valid in the dialect of "ECMA-262, 11th edition" (ECMAScript 2020), section 21.2.1, and
SHOULD be read with the u flag. So a validator needs the 2020 grammar, which refuses what later editions added.

ECMAScript 2020 takes "the latest version of the Unicode Standard" for what characters are (its section 2), and JSON
Schema names no Unicode version. But its section 21.2.2.8.4 closes the property names: an implementation "must not
support" names other than those of its Tables 55 to 58, which list Unicode 12's scripts. So in the 2020 grammar the
names are 2020's whatever the character database, and the membership of a character follows the database. That is
why the two settings are independent: a validator that wants ECMAScript 2020 exactly as its edition was published
takes the 2020 grammar with Unicode 13 (the version of the year), and one that wants today's characters takes the
2020 grammar with 17 or 18.

## Decisions

Each has a recommendation.

1. **Two settings, each with a default that changes nothing.**
   - The edition: `Current` (the default: what the engine accepts today) or `Es2020`.
   - The Unicode version: `V13`, `V17`, `V18` or `Latest`. The default is the Unicode version of the V8 in the
     reference Node, 17 today. It moves only when the reference Node moves, so the default answers stay V8's. `Latest`
     resolves to the newest version the engine carries (18 today), and moves when a later version is added.
   - Neither is a flag letter: the flags are ECMA-262's. Each engine takes them in its own idiom beside the flags and
     the match timeout (an options value, named arguments, an overload). An engine reports the version it resolved,
     as each reports `UnicodeVersion` today.
2. **What the 2020 grammar refuses.** Everything ECMAScript added to regular expressions after its 11th edition, and
   nothing else, as a syntax error at compile time:
   - the `d` flag (match indices, ECMAScript 2022) and the `v` flag (set notation and properties of strings, 2024);
   - modifier groups, `(?i:...)` and `(?-i:...)` (2025);
   - a group name used by two groups, even in separate alternatives (2025);
   - a property name or value that is not in ECMAScript 2020's Tables 55 to 58 (a Unicode 13 or later script, such as
     `Script=Yezidi`, or a binary property added since).

   Every answer to a pattern the 2020 grammar accepts is the same as the current edition's under the same Unicode
   version. Annex B (a pattern without `u`) is the same in both editions.
3. **The tables of each version, from the Unicode Character Database.** Today every engine's tables come, at one or two
   removes, from the regress crate's tables of Unicode 17.0.0, through the .NET scripts (`tools/unicode`) and the Go
   module's. The 18.0 overlay of the test data was made by running regress's `gen-unicode` on UCD 18.0.0 and the same
   scripts, which give the shipped tables byte for byte when run on 17.0.0. The sweep makes that chain the one source:
   - run it on UCD 13.0.0, 17.0.0 and 18.0.0 (with each version's `emoji-sequences.txt` and
     `emoji-zwj-sequences.txt`);
   - check each version's output against that version's UCD files, as the comparison described in
     `gen-ecma-unicode-data.ps1` was made for 17.0.0;
   - feed every port's generator from it, each with a `-Check` mode in CI.

   Each engine carries all three versions. **Recommended: whole tables per version,** read lazily as they are now. The
   cost is about three times today's table data in each library (about 100 KB of source in .NET, 400 KB in Rust). The
   alternative is 17's tables plus the ranges each other version changes. That is smaller, but it is a second shape of
   table in every port, and a delta is harder to check against the UCD than a whole table.
4. **The test data gains the 2020 grammar and Unicode 13.**
   - A Unicode 13 overlay, `data/unicode/13.0.jsonl`, made as the 18.0 one was: the .NET data-set runner built with
     13.0 tables and `--answers`. It is checked as 18.0's was, against SRELL or another engine with that database
     where one exists.
   - An edition overlay, `data/edition/es2020.jsonl`: the validity of every record under the 2020 grammar. V8 has no
     2020 mode, so the overlay is made by a script from the rules of decision 2 (the flags, modifier groups, duplicate
     names, and the property names of the 2020 tables). It is checked by the .NET runner in its 2020 mode, which must
     agree with it on every record. No answer to an accepted pattern changes, so the overlay holds validities only.
   - `manifest.json` names both. A runner applies them with `unicode.js apply` (and an `edition` counterpart), and every
     engine's CI runs the data under 13, 17 (the data as it is) and 18, and under the 2020 grammar.
5. **The try-create with a timeout.** Every engine gets a create that takes the match timeout and returns an error in
   place of throwing, in its own idiom, so a validator never needs the global default:
   - .NET: `Regex.TryCreate(pattern, flags, matchTimeout, out regex, out error)`;
   - Java: a compile that returns a result type;
   - Julia: a try form that returns `nothing` with the error;
   - Pascal: `TryCompileEcmaRegex` with an error out.

   Rust, Go, OCaml and the C library have it already. Each binding over the C library exposes it in its own idiom. The
   new settings of decision 1 go through the same calls.
6. **The reference Node moves to 24.22.1.** It is the security release of the same line, with the same V8 (13.6.233.17),
   so no answer changes:
   - first, the data is verified again with 24.22.1 (`verify.js`, `unicode.js check`, `spec-answers.js check`,
     `verify-test262-exec.js`);
   - then every pin moves: the data's manifest and workflow, every engine's and binding's harness `Dockerfile`, the
     .NET `Dockerfile`'s version and SHA-256, the central site's workflow and docs.

   Only LTS lines are taken, as agreed.
7. **The order, one engine at a time, each its own pull request and release:**
   1. The test data: Node 24.22.1, the 13.0 overlay and the edition overlay.
   2. The .NET reference engine, which makes the overlays and checks them.
   3. The Rust crate, the source of the other ports' tables, and its C library.
   4. The bindings over the C library: C, C++, Python, Ruby, PHP, Swift, R and Fortran.
   5. Go, Java, Julia, Pascal and OCaml.
   6. The central site's harnesses are unchanged: they time the default, which is today's answers.
8. **Versions.** The new settings and calls add to each API and change no default answer, so each engine's release is
   the next patch, as features are numbered here. An engine whose API must change shape to take the settings (none is
   expected) would be a minor release.

## No cost to a warm match

Agreed with all the decisions above, with one constraint: these changes must not slow a warm match. So:
- both settings are resolved when a pattern is compiled. The edition only changes what the parser accepts, and the
  Unicode version only changes which tables the compiler reads its classes and case folding from;
- nothing the matcher or the searches do for each character changes, and a compiled pattern holds no reference to the
  settings;
- each engine's pull request carries an A/B of the Industry workloads, with the default settings, against its last
  release, showing no row moved beyond the run's noise. A pattern compiled under another Unicode version may compile
  more slowly the first time its tables are read, which is not a warm cost.

## What it does not change

The defaults: an engine built from this sweep, given no new setting, accepts the same patterns and gives the same
answers as before, and the benchmark page measures the same thing. The ARM sweep and the R `sherlock: .* /s` row come
after, in that order.
