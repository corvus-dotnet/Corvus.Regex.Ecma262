# The record of a run with failures

`run.json` is a run of the interleaved protocol over three Industry workloads, with three kinds of figure the nightly
run can give besides a time:

- the .NET interpreter over `sherlock: zqj` stopped at the 30 s limit (`{"limit": 30}`);
- this library's generated code over UTF-8 on `sherlock: aei` with a round that gave no figure (an error);
- both .NET engines over `sherlock: ^Sherlock Holmes|Sherlock Holmes$ /m` with a count that differs from V8's (a time,
  marked `differs`).

The harness-image workflow writes the record of it with `industry-results.py` and checks that the record keeps each
failure as the data file carries it, so the record step cannot fail on a figure with no time unnoticed. The smoke run
of the image times two workloads whose figures always have one.

The workflow also writes the record of the same run with the .NET generator given no time on any workload, and with a
data-set runner that gave no counts line, and checks that the record keeps the engine and shows this library's engines
with no compliance figure.
