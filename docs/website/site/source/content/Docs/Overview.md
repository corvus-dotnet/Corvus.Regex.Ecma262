---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Documentation"
---
The documentation is the library's own working papers, published as they are kept in the repository.

## [Design](/docs/design.html)

What is ported and from where, the public surface and how it follows `System.Text.RegularExpressions`, one matcher over UTF-8 and UTF-16 with no copy, the searches for where a match can start, the source generator's structured code and emitted search, the target frameworks, the test data and the ECMA-262 test families, the benchmark procedure, and the phases with their gates and the dates they were met.

## Benchmarks

The measured runs against `System.Text.RegularExpressions`, interpreted against interpreted and generated against generated, on the Industry workloads .NET itself publishes. Each page records the host, the commit, the probe readings that make the run valid, every row, and the rows above 1.0 with the reason for each.
