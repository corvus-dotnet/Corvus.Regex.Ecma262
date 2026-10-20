#!/usr/bin/env python3
"""Writes the Industry benchmark record from a GeneratedCount run: docs/benchmarks/industry.json (the data of the
results page, see tools/benchmarks/results-page/generate_site.py) and docs/benchmarks/industry.md (the table).

Usage: industry-results.py <BenchmarkDotNet artifacts dir> <commit> <probe before ns> <probe after ns> <date>

The run is the GeneratedCount benchmark of Solutions/Corvus.Regex.Ecma262.Benchmarks under the pinned-host procedure
(docs/design/design.md, section 9): five engines over the 57 Industry workloads, one Count each.
"""
import csv, glob, json, math, os, statistics, sys

run, commit, before, after, date = sys.argv[1:6]
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
out_dir = os.path.join(root, "docs", "benchmarks")
U = {"ns": 1, "us": 1e3, "μs": 1e3, "ms": 1e6, "s": 1e9}
B = {"B": 1, "KB": 1024, "MB": 1024 * 1024}

def ns(t):
    v, u = t.split()
    return float(v.replace(",", "")) * U[u]

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
    rows.setdefault(r["Workload"], {})[r["Method"].strip("'")] = r

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
    "<p><b>Each figure is one <code>Count</code></b> over the whole text: every match found, as the benchmark of dotnet/performance measures it. The Corvus rows carry the <code>u</code> flag (the benchmark adds <code>gu</code> to every row), which .NET has no mode for: classes decode surrogate pairs and the <code>i</code> flag folds <code>s</code> to the long s and <code>k</code> to the Kelvin sign. Counts agree on 53 of the 57 rows; the four apart are <code>$</code> and <code>.</code> at CRLF, where ECMA-262 counts CR as a line terminator, and <code>\\w</code>, which .NET takes as Unicode letters and digits.</p>"
    "<p><b>The five engines</b> are .NET's interpreter (<code>RegexOptions.None</code>), .NET's source generator (<code>[GeneratedRegex]</code>), this library's interpreter over UTF-16, and this library's source generator over UTF-16 and over UTF-8. The UTF-8 rows match the same text as bytes, with nothing transcoded.</p>"
    "<p><b>The bar is the mean</b> of BenchmarkDotNet's short job (three launches, three warm-ups, three iterations) and the whisker its half-width of the 99.9% confidence interval. Allocated is the bytes the benchmark allocated per operation; the library allocates nothing per match once its working memory has grown.</p>"
    f"<p><b>The host</b> is WSL2 on Windows on an Intel Core i7-13800H, the VM pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under <code>taskset -c 0-11</code> after a settle of at least three minutes, with a host probe at {before} ns before and {after} ns after (12.5 to 16 is the accepted band). .NET 10. Commit {commit}, {date}. The procedure is section 9 of the design document.</p>"
)

data = {
    "meta": {
        "title": "Corvus.Regex.Ecma262: the Industry benchmarks",
        "lede": "Five engines over the Industry workloads of dotnet/performance: .NET's interpreter and source generator, and this library's interpreter and source generator over UTF-16 and over UTF-8. Pick the engines to compare and the figure to compare them on; every view below follows.",
        "repo": "corvus-dotnet/Corvus.Regex.Ecma262",
        "commit": commit,
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

def cell(name, w):
    x = results[name].get(w)
    return x["time"]["median"] / 1000 if x else float("nan")

lines = []
for w in workloads:
    ni, ng, ci, cg, c8 = (cell(k, w) for k in (".NET interpreter", ".NET generated", "Corvus interpreter", "Corvus generated UTF-16", "Corvus generated UTF-8"))
    ww = w.replace("|", "\\|")
    lines.append(f"| {ww} | {ni:.1f} | {ci:.1f} | {ci/ni if ni else 0:.2f} | {ng:.1f} | {cg:.1f} | {c8:.1f} | {cg/ng if ng else 0:.2f} | {c8/ng if ng else 0:.2f} |")

summary = f"""- Interpreter against .NET's interpreter, over UTF-16: median {statistics.median(ri):.2f}, geometric mean {gm(ri):.2f}, at or below 1.0 on {sum(1 for x in ri if x <= 1.0)} of {len(ri)}.
- Generated code against .NET's generated code, over UTF-16: median {statistics.median(rg):.2f}, geometric mean {gm(rg):.2f}, at or below 1.0 on {sum(1 for x in rg if x <= 1.0)} of {len(rg)}.
- Generated code over UTF-8 against .NET's generated code: median {statistics.median(rg8):.2f}, geometric mean {gm(rg8):.2f}, at or below 1.0 on {sum(1 for x in rg8 if x <= 1.0)} of {len(rg8)}."""

md = f"""# Benchmarks: the Industry workloads

Commit {commit}, {date}. Host: WSL2 on Windows on an Intel Core i7-13800H, the VM pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under `taskset -c 0-11` after a settle of at least three minutes, the host probe at {before} ns before and {after} ns after (12.5 to 16 is the accepted band), .NET 10, BenchmarkDotNet short job, the `GeneratedCount` benchmark of `Solutions/Corvus.Regex.Ecma262.Benchmarks`.

The interactive page of this run is at [corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/](https://corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/); its data is `industry.json` beside this file, and `tools/benchmarks/industry-results.py` writes both from a run.

The workloads are the Industry set of dotnet/performance (`Perf.Regex.Industry`: the Sherlock Holmes, Leipzig and mariomka texts and the rebar names), the set behind .NET's own "regex performance improvements in .NET N" tables. Each is run five ways: .NET's interpreter (`RegexOptions.None`), .NET's source generator (`[GeneratedRegex]`), this library's interpreter over UTF-16, and this library's source generator over UTF-16 and over UTF-8. `Count` over the text, one call per operation. A ratio below 1.0 is this library being faster.

The Corvus rows carry the `u` flag (the benchmark adds `gu` to every row, for the interpreter rows too), which .NET has no mode for: classes decode surrogate pairs and the `i` flag folds `s` to the long s and `k` to the Kelvin sign. Counts agree on 53 of the 57 rows; the four apart are `$` and `.` at CRLF, where ECMA-262 counts CR as a line terminator, and `\\w`, which .NET takes as Unicode letters and digits. The UTF-8 rows match the same text as bytes, with nothing transcoded.

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
