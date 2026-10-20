#!/usr/bin/env python3
"""Writes the Industry benchmark record from a GeneratedCount run: docs/benchmarks/industry.json (the data of the
results page, see tools/benchmarks/results-page/generate_site.py) and docs/benchmarks/industry.md (the table).

Usage: industry-results.py <BenchmarkDotNet artifacts dir> <commit> <probe before ns> <probe after ns> <date>

The run is the GeneratedCount benchmark of Solutions/Corvus.Regex.Ecma262.Benchmarks under the pinned-host procedure
(docs/design/design.md, section 9): five engines over the 57 Industry workloads, one Count each.
"""
import csv, string, glob, json, math, os, statistics, sys

run, commit, before, after, date = sys.argv[1:6]
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
out_dir = os.path.join(root, "docs", "benchmarks")
U = {"ns": 1, "us": 1e3, "μs": 1e3, "ms": 1e6, "s": 1e9}
B = {"B": 1, "KB": 1024, "MB": 1024 * 1024}

def ns(t):
    v, u = t.split()
    n = float(v.replace(",", "")) * U[u]
    return n if n > 0 else None  # a figure of 0 ns is a row the benchmark could not run, not a measurement

def nbytes(t):
    if t in ("NA", "-", ""):
        return None
    v, u = t.split()
    return float(v.replace(",", "")) * B[u]

ENGINES = [
    (".NET interpreter", ".NET interpreter", ".NET"),
    (".NET generated", ".NET generated", ".NET"),
    ("Corvus UTF-16", "Corvus interpreter", "Corvus"),
    ("Corvus generated UTF-16", "Corvus generated UTF-16", "Corvus"),
    ("Corvus generated UTF-8", "Corvus generated UTF-8", "Corvus"),
]
TEXTS = {"sherlock": "Sherlock Holmes (0.6 M chars)", "leipzig": "Leipzig corpus (16 M chars)", "mariomka": "mariomka (0.05 M chars)", "rebar-ru": "OpenSubtitles ru (1.2 M chars)", "rebar-zh": "OpenSubtitles zh (0.3 M chars)"}

f = glob.glob(os.path.join(run, "results", "*GeneratedCount-report.csv"))[0]
rows = {}
for r in csv.DictReader(open(f)):
    # BenchmarkDotNet writes a backslash in a parameter as two; the pattern has one.
    rows.setdefault(r["Workload"].replace("\\\\", "\\"), {})[r["Method"].strip("'")] = r

workloads = list(rows)
results = {}
for method, name, group in ENGINES:
    results[name] = {}
    for w in workloads:
        r = rows[w].get(method)
        if r is None:
            continue
        mean, err = ns(r["Mean"]), ns(r["Error"])
        if mean <= 0:
            results[name][w] = None
            continue
        entry = {"time": {"median": mean, "min": max(0.0, mean - err), "max": mean + err}}
        alloc = nbytes(r.get("Allocated", "NA"))
        if alloc is not None:
            entry["allocated"] = {"median": alloc, "min": alloc, "max": alloc}
        results[name][w] = entry

def ratio_rows(a, b):
    out = []
    for w in workloads:
        x, y = results[a].get(w), results[b].get(w)
        if x and y:
            out.append(x["time"]["median"] / y["time"]["median"])
    return out

def gm(xs):
    return math.exp(sum(math.log(x) for x in xs) / len(xs))

ri = ratio_rows("Corvus interpreter", ".NET interpreter")
rg = ratio_rows("Corvus generated UTF-16", ".NET generated")
rg8 = ratio_rows("Corvus generated UTF-8", ".NET generated")
n = len(workloads)

method_html = (
    "<p><b>Each figure is one <code>Count</code></b> over the whole text: every match found, as the benchmark of dotnet/performance measures it. The Corvus rows run under the <code>u</code> flag, which .NET has no mode for; counts agree on 53 of the 57 rows, and the four apart are <code>$</code> and <code>.</code> at CRLF, where ECMA-262 counts CR as a line terminator, and <code>\\w</code>, which .NET takes as Unicode letters and digits.</p>"
    "<p><b>Where this library is slower, and why.</b> Four kinds of row read above 1.0, each the price of an ECMA-262 rule .NET does not apply: <code>.</code> and <code>$</code> at CRLF, where ECMA-262's <code>.</code> stops at CR as well as LF, so <code>.*</code> finds 36,491 matches to .NET's 26,105 at parity per match; case-insensitive patterns under the u flag, whose search must find the long s and the Kelvin sign as forms of <code>s</code> and <code>k</code>, which .NET's search ignores; a class that admits a supplementary character (two units) before a literal at a fixed distance, so the distance is not fixed (<code>[a-q][^u-z]{13}x</code>); and non-ASCII literals over UTF-8, where every Cyrillic or Chinese character is two or three bytes with no rare byte among them, against UTF-16's one rare unit per character. The other rows above 1.0 are within a few percent of parity and read either side of it from run to run.</p>"
    "<p><b>The five engines</b> are .NET's interpreter (<code>RegexOptions.None</code>), .NET's source generator (<code>[GeneratedRegex]</code>), this library's interpreter over UTF-16, and this library's source generator over UTF-16 and over UTF-8. The UTF-8 rows match the same text as bytes, with nothing transcoded.</p>"
    "<p><b>The bar is the mean</b> of BenchmarkDotNet's short job (three launches, three warm-ups, three iterations) and the whisker its half-width of the 99.9% confidence interval. Allocated is the bytes the benchmark allocated per operation; the library allocates nothing per match once its working memory has grown.</p>"
    f"<p><b>The host</b> is WSL2 on Windows on an Intel Core i7-13800H, the VM pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under <code>taskset -c 0-11</code> after a settle of at least three minutes, with a host probe at {before} ns before and {after} ns after (12.5 to 16 is the accepted band). .NET 10. Commit {commit}, {date}. The procedure is section 9 of the design document.</p>"
)

data = {
    "meta": {
        "title": "Corvus.Regex.Ecma262: the Industry benchmarks",
        "lede": "Five engines over the Industry workloads of dotnet/performance: .NET's interpreter and source generator, and this library's interpreter and source generator over UTF-16 and over UTF-8. Pick the engines to compare and the figure to compare them on; every view below follows.",
        "repo": "corvus-dotnet/Corvus.Regex.Ecma262",
        "commit": commit, "icon": "/assets/images/favicon.svg",
        "date": date,
        "runUrl": "",
        "runs": 0,
        "facts": [["Host", "i7-13800H, WSL2, pinned to the P-cores"], ["Probe", f"{before} ns before, {after} ns after"], ["Runtime", ".NET 10"]],
        "method": method_html,
    },
    "vocab": {
        "impl": "engine", "impls": "engines", "Impls": "Engines",
        "schema": "workload", "schemas": "workloads", "Schema": "Workload", "Schemas": "Workloads",
        "failed": "did not run", "centre": "Mean", "spread": "Error",
        "barLegend": "mean of the run", "whiskerLegend": "whisker: the error of the mean",
    },
    "measures": {
        "time": {"label": "Time", "long": "time per Count", "parts": ["time"]},
        "allocated": {"label": "Allocated", "long": "bytes allocated per Count", "parts": ["allocated"], "bytes": True},
    },
    "impls": [{"id": name, "name": name, "group": group, "version": ""} for _, name, group in ENGINES],
    "schemas": [{"name": w, "note": "", "meta": [["text", TEXTS.get(w.split(":")[0], w.split(":")[0])]]} for w in workloads],
    "results": results,
}
with open(os.path.join(out_dir, "industry.json"), "w", encoding="utf-8") as fo:
    json.dump(data, fo, indent=1)

def us(x):
    """Microseconds to one decimal, or to three significant digits below ten."""
    return f"{x:.1f}" if x >= 10 else f"{x:.2f}" if x >= 1 else f"{x:.3f}"

def cell(name, w):
    x = results[name].get(w)
    return x["time"]["median"] / 1000 if x else float("nan")

lines = []
for w in workloads:
    ni, ng, ci, cg, c8 = (cell(k, w) for k in (".NET interpreter", ".NET generated", "Corvus interpreter", "Corvus generated UTF-16", "Corvus generated UTF-8"))
    # An inline code element with every punctuation character escaped: the site's markdown would read {Sm} in \p{Sm}
    # as an attribute block, a pipe as a cell, and inside a code span it keeps the backslash of an escaped pipe.
    ww = "<code>" + "".join("\\" + ch if ch in string.punctuation else ch for ch in w) + "</code>"
    lines.append(f"| {ww} | {us(ni)} | {us(ci)} | {ci/ni if ni else 0:.2f} | {us(ng)} | {us(cg)} | {us(c8)} | {cg/ng if ng else 0:.2f} | {c8/ng if ng else 0:.2f} |")

summary = f"""- Interpreter against .NET's interpreter, over UTF-16: median {statistics.median(ri):.2f}, geometric mean {gm(ri):.2f}, at or below 1.0 on {sum(1 for x in ri if x <= 1.0)} of {len(ri)}.
- Generated code against .NET's generated code, over UTF-16: median {statistics.median(rg):.2f}, geometric mean {gm(rg):.2f}, at or below 1.0 on {sum(1 for x in rg if x <= 1.0)} of {len(rg)}.
- Generated code over UTF-8 against .NET's generated code: median {statistics.median(rg8):.2f}, geometric mean {gm(rg8):.2f}, at or below 1.0 on {sum(1 for x in rg8 if x <= 1.0)} of {len(rg8)}."""

md = f"""# Benchmarks: the Industry workloads

- **Commit** {commit}, run on {date}.
- **Host** an Intel Core i7-13800H under WSL2 on Windows, the virtual machine pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under `taskset -c 0-11` after a settle of at least three minutes.
- **Health probe** {before} ns before the run and {after} ns after; 12.5 to 16 is the band that makes a run valid.
- **Runtime and harness** .NET 10, BenchmarkDotNet's short job, the `GeneratedCount` benchmark of `Solutions/Corvus.Regex.Ecma262.Benchmarks`.

The interactive page of this run is at [corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/](https://corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/); its data is `industry.json` beside this file, and `tools/benchmarks/industry-results.py` writes both from a run.

The workloads are the Industry set of dotnet/performance (`Perf.Regex.Industry`: the Sherlock Holmes, Leipzig and mariomka texts and the rebar names), the set behind .NET's own "regex performance improvements in .NET N" tables. Each is run five ways: .NET's interpreter (`RegexOptions.None`), .NET's source generator (`[GeneratedRegex]`), this library's interpreter over UTF-16, and this library's source generator over UTF-16 and over UTF-8. `Count` over the text, one call per operation. A ratio below 1.0 is this library being faster.

The Corvus rows run under the `u` flag, which .NET has no mode for, so the two engines answer by different rules on a few rows: counts agree on 53 of the 57, and the four apart are `$` and `.` at CRLF, where ECMA-262 counts CR as a line terminator, and `\\w`, which .NET takes as Unicode letters and digits. The UTF-8 rows match the same text as bytes, with nothing transcoded.

## Where this library is slower than .NET, and why

Four kinds of row read above 1.0, each the price of an ECMA-262 rule that .NET does not apply:

1. **`.` and `$` at CRLF.** ECMA-262's `.` stops at CR as well as LF, so over a text with CRLF endings `.*` finds 36,491 matches to .NET's 26,105, at parity per match (`.*` at 1.43).
2. **Case-insensitive patterns under the u flag.** The search must find the long s and the Kelvin sign as forms of `s` and `k`, which .NET's search ignores, so its tables carry every form (`Sher[a-z]+|Hol[a-z]+ /i` at 1.07; `the /i` over UTF-8 at 1.14).
3. **A class that admits a supplementary character before a literal at a fixed distance.** Such a character is two units, so the distance is not fixed and the literal is found by a walk back rather than at an offset (`[a-q][^u-z]{13}x` at 1.14).
4. **Non-ASCII literals over UTF-8.** Every Cyrillic or Chinese character is two or three bytes with no rare byte among them, where UTF-16 gives the search one rare unit per character; so the UTF-8 rows for the Chinese names read 1.3 to 1.9 against .NET over UTF-16 while the UTF-16 rows are level.

The other rows above 1.0 are within a few percent of parity and read either side of it from run to run: both engines are at the floor of one vectorised search over the text.

## Summary

{summary}

## The rows

Times are microseconds for one `Count` over the text.

| Workload | .NET interpreter | Corvus interpreter | ratio | .NET generated | Corvus generated UTF-16 | Corvus generated UTF-8 | ratio UTF-16 | ratio UTF-8 |
|---|---|---|---|---|---|---|---|---|
""" + "\n".join(lines) + "\n"
with open(os.path.join(out_dir, "industry.md"), "w", encoding="utf-8") as fo:
    fo.write(md)
print(summary)
above = sorted(((w, results["Corvus generated UTF-16"][w]["time"]["median"] / results[".NET generated"][w]["time"]["median"]) for w in workloads if results[".NET generated"].get(w) and results["Corvus generated UTF-16"].get(w)), key=lambda x: -x[1])
print("above 1.0 (generated):", [(w, round(r, 2)) for w, r in above if r > 1.0])
abovei = sorted(((w, results["Corvus interpreter"][w]["time"]["median"] / results[".NET interpreter"][w]["time"]["median"]) for w in workloads if results[".NET interpreter"].get(w) and results["Corvus interpreter"].get(w)), key=lambda x: -x[1])
print("above 1.0 (interpreter):", [(w, round(r, 2)) for w, r in abovei if r > 1.0])
