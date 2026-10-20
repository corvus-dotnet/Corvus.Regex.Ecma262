---
ContentType: "application/vnd.endjin.ssg.content+md"
PublicationStatus: Published
Date: 2026-10-20T00:00:00.0+00:00
Title: "Documentation"
---
The documentation describes the library as it is: the design and the reasons for it, the measured run against `System.Text.RegularExpressions`, and how to build and contribute.

## [Design](/docs/design.html)

What the library is and where it came from, the public surface and how it follows `System.Text.RegularExpressions`, one matcher over UTF-8 and UTF-16 with no copy, the searches for where a match can start, the source generator's structured code and emitted search, the target frameworks, the test data and the ECMA-262 test families, the benchmark procedure, and the differences a .NET user will meet.

## [Benchmarks](/docs/industry.html)

The measured run against `System.Text.RegularExpressions` on the Industry workloads .NET itself publishes: five engines (both interpreters, both source generators, and this library's generated code over UTF-8), the host, the commit, the probe readings that make the run valid, and every row. The same run as an [interactive page](/benchmarks/index.html): pick the engines and the figure, and read the leaderboard, the matrix, one workload at a time, or two engines head to head.

## [Contributing](/docs/contributing.html)

The tools on Windows, Linux and macOS, the build and the tests step by step, the test data and the benchmark gates, and what a pull request needs.
