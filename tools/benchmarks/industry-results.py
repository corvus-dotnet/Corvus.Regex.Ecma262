#!/usr/bin/env python3
"""Writes the Industry benchmark record from a run into a directory: industry.json (the data of the
results page, see tools/benchmarks/results-page/generate_site.py) and industry.md (the table). The daily workflow is
its publisher; nothing it writes is committed.

Usage: industry-results.py (--interleaved <run.json> | --run <BenchmarkDotNet artifacts dir> [--v8 <results json>])
                           --commit <sha> --date <date>
                           [--host pinned|ci] [--probe-before <ns> --probe-after <ns>] [--job short|default]
                           [--out-dir <dir>] [--base-path <prefix>] [--run-url <url>]

The V8 results are those of tools/benchmarks/v8/industry.mjs over the same workloads, run under the same procedure.
The host is "pinned" for the procedure of the design document (the probes are then required) or "ci" for the daily
run on a GitHub-hosted runner (the benchmarks workflow), where nothing is pinned and the figures are those of that
day's runner; the two are kept apart and never read against each other.

The run is the Industry benchmark of Solutions/Corvus.Regex.Ecma262.Benchmarks: five engines over the 57 Industry
workloads, one Count each. With --interleaved it is the run of the interleaved protocol every harness of the central
benchmark page runs (Driver.cs, `Corvus.Regex.Ecma262.Benchmarks industry`), V8 among its engines, whose facts are
the run's own record of itself; with --run, BenchmarkDotNet's GeneratedCount class under the pinned-host procedure
(docs/design/design.md, section 9), V8 from its own harness (--v8).
"""
import csv, string, glob, json, math, os, statistics, sys

import argparse
import re


def failure(r):
    """A figure the harness gave no time for, as the data file carries it: stopped at the harness's limit
    ({"limit": s}), a count of matches that differs from V8's ({"wrongCount": {"counted": n, "v8": m}}), or another
    failure with what the harness said ({"error": "..."})."""
    if "limit" in r:
        return {"limit": r["limit"]}
    m = re.match(r"counted (\d+) where V8 counts (\d+)", str(r.get("error", "")))
    if m:
        return {"wrongCount": {"counted": int(m.group(1)), "v8": int(m.group(2))}}
    return {"error": r["error"]} if "error" in r else None

ap = argparse.ArgumentParser()
ap.add_argument("--run")
ap.add_argument("--interleaved", help="the run.json of the interleaved driver (Driver.cs), in place of --run and --v8")
ap.add_argument("--commit", required=True)
ap.add_argument("--date", required=True)
ap.add_argument("--v8")
ap.add_argument("--host", choices=["pinned", "ci"], default="pinned")
ap.add_argument("--probe-before", default="")
ap.add_argument("--probe-after", default="")
ap.add_argument("--job", choices=["short", "default"], default="short")
ap.add_argument("--out-dir", required=True)
ap.add_argument("--base-path", default="")
ap.add_argument("--run-url", default="")
ap.add_argument("--compliance", default="", help="the data-set runner's last line for the Corvus engines (records ... right N wrong N refused-valid N accepted-invalid N ...)")
ap.add_argument("--compliance-v8", default="", help="tools/benchmarks/v8/dataset.mjs's last line for V8, in the same shape")
args = ap.parse_args()
if not (args.run or args.interleaved):
    raise SystemExit("a run: --interleaved <run.json> or --run <BenchmarkDotNet artifacts>")
run, commit, date, v8_path = args.run, args.commit, args.date, args.v8
before, after = args.probe_before, args.probe_after
if args.host == "pinned" and not (before and after):
    raise SystemExit("a pinned run has its probe readings: --probe-before and --probe-after")
out_dir = args.out_dir
os.makedirs(out_dir, exist_ok=True)
# The host and the runtime as BenchmarkDotNet recorded them in the run's report header, never typed in.
HOST_LINES = []
for g in (glob.glob(os.path.join(run, "results", "*GeneratedCount-report-github.md")) if run else []):
    with open(g, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("```"):
                if HOST_LINES and not line:
                    break
                continue
            HOST_LINES.append(line)
            if len(HOST_LINES) >= 6:
                break
    break
MACHINE = next((l for l in HOST_LINES if "CPU" in l or "cores" in l), "")
OS_LINE = next((l for l in HOST_LINES if l.startswith("BenchmarkDotNet")), "")
RUNTIME = next((l.split(":", 1)[1].strip() for l in HOST_LINES if l.startswith("[Host]")), ".NET 10")
JOB = {"short": "BenchmarkDotNet's short job (one launch, three warm-ups, three iterations)", "default": "BenchmarkDotNet's default job (one launch, warm-ups and iterations as it settles them, fifteen or more)"}[args.job]
# An interleaved run records itself: the host, the runtime and the protocol come from its facts.
INTERLEAVED = json.load(open(args.interleaved, encoding="utf-8")) if args.interleaved else None
if INTERLEAVED:
    f_ = INTERLEAVED["facts"]
    MACHINE = f"{f_.get('cpu', '')}, {f_.get('os', '')}, {f_.get('cores', '')} cores (affinity {f_.get('affinity', '')}), load {f_.get('load', '')} at the start"
    RUNTIME = f_.get("runtime", ".NET 10")
    JOB = (f"the interleaved protocol of every Corvus engine's harness: every workload in turn, each in {f_.get('rounds')} rounds of the "
           f"engines in turn (their order turned each round), each figure a process of its own, warmed for at least 1.5 s (for "
           f".NET's tiered compiler) and timed for {f_.get('seconds')} s in {f_.get('passes')} passes; a workload's figure is the "
           f"median of its rounds, the spread its fastest and slowest round")
if args.host == "pinned":
    HOST_MD = f"""- **Host** an Intel Core i7-13800H under WSL2 on Windows, the virtual machine pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under `taskset -c 0-11` after a settle of at least three minutes.
- **Health probe** {before} ns before the run and {after} ns after; 12.5 to 16 is the band that makes a run valid."""
    HOST_HTML = f"<p><b>The host</b> is WSL2 on Windows on an Intel Core i7-13800H, the VM pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under <code>taskset -c 0-11</code> after a settle of at least three minutes, with a host probe at {before} ns before and {after} ns after (12.5 to 16 is the accepted band). .NET 10. Commit {commit}, {date}. The procedure is section 9 of the design document.</p>"
    FACTS = [["Host", MACHINE or "the pinned host"], ["Pinned", "the performance cores, under taskset, after a settle"], ["Probe", f"{before} ns before, {after} ns after"], ["Runtime", RUNTIME]]
    TITLE_SUFFIX = ""
    LEDE_TAIL = ""
    # Relative to the page (benchmarks/index.html on the site), so no base path is needed inside the data.
    LINKS = [{"label": "About these benchmarks", "url": "../docs/industry.html"}]
else:
    HOST_MD = """- **Host** the runner of the central benchmark site, which runs every engine's harness image in one job on it each night, so the figures of a run are comparable with each other."""
    HOST_HTML = f"<p><b>The host</b> is the runner of the central benchmark site, which runs every engine's harness image in one job on it each night, so the figures of a run are comparable with each other. .NET 10. Commit {commit}, {date}.</p>"
    FACTS = [["Host", MACHINE or "a CI runner"], ["Runner", "one runner for every engine, not pinned"], ["Runtime", RUNTIME]]
    TITLE_SUFFIX = ""
    LEDE_TAIL = ""
    LINKS = [{"label": "About the .NET engine's benchmarks", "url": "https://corvus-oss.org/Corvus.Regex.Ecma262/docs/industry.html"}]
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

# The driver's engines, as the rows of the page name them.
INTERLEAVED_ENGINES = {"net-interpreter": ".NET interpreter", "net-generated": ".NET generated", "corvus-utf16": "Corvus interpreter",
                       "corvus-generated-utf16": "Corvus generated UTF-16", "corvus-generated-utf8": "Corvus generated UTF-8"}
rows = {}
for r in (csv.DictReader(open(glob.glob(os.path.join(run, "results", "*GeneratedCount-report.csv"))[0])) if run else []):
    # BenchmarkDotNet writes a backslash in a parameter as two; the pattern has one.
    rows.setdefault(r["Workload"].replace("\\\\", "\\"), {})[r["Method"].strip("'")] = r

workloads = list(rows) if run else list(next(iter(INTERLEAVED["results"].values())))
results = {}
for method, name, group in (ENGINES if run else []):
    results[name] = {}
    for w in workloads:
        r = rows[w].get(method)
        if r is None:
            continue
        # The spread is one standard deviation of the iterations, not BenchmarkDotNet's Error: that is half of a 99.9%
        # confidence interval, which with the short job's three iterations is some eighteen standard deviations wide.
        mean, err = ns(r["Mean"]), ns(r["StdDev"])
        if mean <= 0:
            results[name][w] = None
            continue
        entry = {"time": {"median": mean, "min": max(0.0, mean - err), "max": mean + err}}
        alloc = nbytes(r.get("Allocated", "NA"))
        if alloc is not None:
            entry["allocated"] = {"median": alloc, "min": alloc, "max": alloc}
        results[name][w] = entry

if INTERLEAVED:
    by_name = {v: k for k, v in INTERLEAVED_ENGINES.items()}
    for _, name, _ in ENGINES:
        figures = INTERLEAVED["results"].get(by_name[name], {})
        # A figure stopped at the limit is {"limit": 30}, which the page shows as giving no result within 30 s.
        results[name] = {w: ({"time": {"median": r["median"], "min": r["min"], "max": r["max"]}} if "median" in r
                             else failure(r))
                         for w, r in figures.items()}

# V8 as an engine beside the .NET ones: its own harness, the same texts and workloads, the same flags as the Corvus rows.
V8 = None
if INTERLEAVED and "v8" in INTERLEAVED["results"]:
    engine = INTERLEAVED["facts"].get("v8", "V8")
    node = engine.split("Node ")[-1].rstrip(")").split(".")[0]
    V8 = ("V8 (Node " + node + ")", engine)
    ENGINES.append((V8[0], V8[0], "V8"))
    results[V8[0]] = {w: ({"time": {"median": r["median"], "min": r["min"], "max": r["max"]}} if "median" in r
                          else failure(r))
                      for w, r in INTERLEAVED["results"]["v8"].items()}
elif v8_path:
    v8 = json.load(open(v8_path, encoding="utf-8"))
    node = v8["engine"].split("Node ")[-1].rstrip(")").split(".")[0]
    V8 = ("V8 (Node " + node + ")", v8["engine"])
    ENGINES.append((V8[0], V8[0], "V8"))
    results[V8[0]] = {}
    for w in workloads:
        r = v8["results"].get(w)
        results[V8[0]][w] = {"time": {"median": r["median"], "min": r["min"], "max": r["max"]}} if r else None

def timed(name, w):
    """An engine's figure for a workload when it has a time; None when it has none, or is a failure (failure())."""
    x = results[name].get(w)
    return x if x and "time" in x else None

def ratio_rows(a, b):
    out = []
    for w in workloads:
        x, y = timed(a, w), timed(b, w)
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
    + "<p><b>Where this library is slower, and why.</b> Four kinds of row read above 1.0, each the price of an ECMA-262 rule .NET does not apply: <code>.</code> and <code>$</code> at CRLF, where ECMA-262's <code>.</code> stops at CR as well as LF, so <code>.*</code> finds 36,491 matches to .NET's 26,105 at parity per match; case-insensitive patterns under the u flag, whose search must find the long s and the Kelvin sign as forms of <code>s</code> and <code>k</code>, which .NET's search ignores; a class that admits a supplementary character (two units) before a literal at a fixed distance, so the distance is not fixed (<code>[a-q][^u-z]{13}x</code>); and non-ASCII literals over UTF-8, where every Cyrillic or Chinese character is two or three bytes with no rare byte among them, against UTF-16's one rare unit per character. The other rows above 1.0 are within a few percent of parity and read either side of it from run to run.</p>"
    + "<p><b>The engines</b> are .NET's interpreter (<code>RegexOptions.None</code>), .NET's source generator (<code>[GeneratedRegex]</code>), this library's interpreter over UTF-16, this library's source generator over UTF-16 and over UTF-8, and V8 (Node 24's <code>RegExp</code>, judged on the test data as every engine is) over the same texts with the same flags as the Corvus rows, its matches counted by a <code>test</code> loop that advances <code>lastIndex</code> as <code>matchAll</code> does. The UTF-8 rows match the same text as bytes, with nothing transcoded.</p>"
    + (f"<p><b>The bar is the median</b> of {JOB}. A row's ratio is read against its neighbours' rather than to the last few percent.</p>"
       if INTERLEAVED else
       f"<p><b>The bar is the mean</b> of {JOB} and the whisker one standard deviation of its iterations; a row's ratio is read against its neighbours' rather than to the last few percent. Allocated is the bytes the benchmark allocated per operation; the library allocates nothing per match once its working memory has grown.</p>")
    + HOST_HTML
)

import re as _re

def counts_of(line, encodings, note=""):
    """The counts of a data-set runner's last line, with the encodings its runner answers in (which the central page's
    figure is a share of: every answer the test data asks in those encodings)."""
    m = _re.search(r"right ([\d,]+)\s+wrong ([\d,]+)\s+refused-valid ([\d,]+)\s+accepted-invalid ([\d,]+)(?:\s+limit ([\d,]+))?", line)
    if not m:
        # A data-set runner that failed gives no counts: the engine is shown with no compliance figure, and its
        # timing is kept.
        print(f"industry-results: no counts in the data-set runner's last line ({line!r}); no compliance figure", file=sys.stderr)
        return None
    n = lambda k: int((m.group(k) or "0").replace(",", ""))
    out = {"right": n(1), "wrong": n(2), "refused": n(3), "acceptedInvalid": n(4), "limited": n(5), "encodings": encodings}
    if note:
        out["note"] = note
    return out


def compliance_of(group):
    """What an engine answers on the ECMA-262 test data: counts for an engine held to it (this library's over both
    encodings; V8's over UTF-16, its one encoding), or not ECMA-262."""
    if group == "V8":
        return counts_of(args.compliance_v8, ["utf16"], "V8 over UTF-16, its one encoding, by tools/benchmarks/v8/dataset.mjs")
    if group == ".NET":
        return "not ECMA-262"
    return counts_of(args.compliance, ["utf16", "utf8"])

data = {
    "meta": {
        "title": "Corvus.Regex.Ecma262: the Industry benchmarks" + TITLE_SUFFIX,
        "lede": "Six engines over the Industry workloads of dotnet/performance: .NET's interpreter and source generator, this library's interpreter and source generator over UTF-16 and over UTF-8, and V8. Pick the engines to compare and the figure to compare them on; every view below follows." + LEDE_TAIL,
        "repo": "corvus-dotnet/Corvus.Regex.Ecma262",
        "commit": commit, "icon": args.base_path + "/assets/images/favicon.svg",
        "date": date,
        "runUrl": args.run_url,
        "links": LINKS,
        "runs": 0,
        "facts": FACTS,
        "method": method_html,
    },
    "vocab": {
        "impl": "engine", "impls": "engines", "Impls": "Engines",
        "schema": "workload", "schemas": "workloads", "Schema": "Workload", "Schemas": "Workloads",
        "failed": "did not run",
        **({"centre": "Median", "spread": "fastest to slowest round", "barLegend": "median of the rounds",
            "whiskerLegend": "whisker: the fastest and the slowest round"} if INTERLEAVED else
           {"centre": "Mean", "spread": "StdDev", "barLegend": "mean of the run", "whiskerLegend": "whisker: one standard deviation"}),
    },
    "measures": {
        "time": {"label": "Time", "long": "time per Count", "parts": ["time"]},
        **({} if INTERLEAVED else {"allocated": {"label": "Allocated", "long": "bytes allocated per Count", "parts": ["allocated"], "bytes": True}}),
    },
    "impls": [{k: v for k, v in {"id": name, "name": name, "group": group, "version": V8[1] if V8 and name == V8[0] else "", "compliance": compliance_of(group)}.items() if v is not None} for _, name, group in ENGINES],
    "schemas": [{"name": w, "note": "", "meta": [["text", TEXTS.get(w.split(":")[0], w.split(":")[0])]]} for w in workloads],
    "results": results,
}
with open(os.path.join(out_dir, "industry.json"), "w", encoding="utf-8") as fo:
    json.dump(data, fo, indent=1)

def us(x):
    """Microseconds to one decimal, or to three significant digits below ten; a dash for a figure with no time."""
    return "-" if x != x else f"{x:.1f}" if x >= 10 else f"{x:.2f}" if x >= 1 else f"{x:.3f}"

def ratio(a, b):
    """A ratio to two places; empty where either figure has no time."""
    return "" if a != a or b != b or not b else f"{a/b:.2f}"

def cell(name, w):
    x = timed(name, w)
    return x["time"]["median"] / 1000 if x else float("nan")

lines = []
for w in workloads:
    ni, ng, ci, cg, c8 = (cell(k, w) for k in (".NET interpreter", ".NET generated", "Corvus interpreter", "Corvus generated UTF-16", "Corvus generated UTF-8"))
    # An inline code element with every punctuation character escaped: the site's markdown would read {Sm} in \p{Sm}
    # as an attribute block, a pipe as a cell, and inside a code span it keeps the backslash of an escaped pipe.
    ww = "<code>" + "".join("\\" + ch if ch in string.punctuation else ch for ch in w) + "</code>"
    v8c = ""
    if V8:
        vv = cell(V8[0], w)
        v8c = f" {us(vv)} | {ratio(cg, vv)} |"
    lines.append(f"| {ww} | {us(ni)} | {us(ci)} | {ratio(ci, ni)} | {us(ng)} | {us(cg)} | {us(c8)} | {ratio(cg, ng)} | {ratio(c8, ng)} |" + v8c)

def ratios(xs):
    """The median and geometric mean of a pair of engines' ratios; none where no workload has a time from both."""
    if not xs:
        return "no workload timed by both"
    return f"median {statistics.median(xs):.2f}, geometric mean {gm(xs):.2f}, at or below 1.0 on {sum(1 for x in xs if x <= 1.0)} of {len(xs)}"

summary = f"""- Interpreter against .NET's interpreter, over UTF-16: {ratios(ri)}.
- Generated code against .NET's generated code, over UTF-16: {ratios(rg)}.
- Generated code over UTF-8 against .NET's generated code: {ratios(rg8)}."""
v8_head = v8_sep = ""
if V8:
    rv = ratio_rows("Corvus generated UTF-16", V8[0])
    summary += f"\n- Generated code over UTF-16 against {V8[0]}: {ratios(rv)}."
    v8_head = f" {V8[0]} | ratio to V8 |"
    v8_sep = "---|---|"

md = f"""# Benchmarks: the Industry workloads

- **Commit** {commit}, run on {date}.
{HOST_MD}
- **Runtime and harness** .NET 10, {JOB}, the `GeneratedCount` benchmark of `Solutions/Corvus.Regex.Ecma262.Benchmarks`.

The data of this run is `industry.json` beside this file, in the results-page shape; the central benchmark page, [corvus-oss.org/corvus-regex-ecma262-benchmarks](https://corvus-oss.org/corvus-regex-ecma262-benchmarks/), shows it beside the engines of every other source.

The workloads are the Industry set of dotnet/performance (`Perf.Regex.Industry`: the Sherlock Holmes, Leipzig and mariomka texts and the rebar names), the set behind .NET's own "regex performance improvements in .NET N" tables. Each is run five ways: .NET's interpreter (`RegexOptions.None`), .NET's source generator (`[GeneratedRegex]`), this library's interpreter over UTF-16, and this library's source generator over UTF-16 and over UTF-8. `Count` over the text, one call per operation. A ratio below 1.0 is this library being faster.

V8 is the sixth engine: Node 24's `RegExp`, whose answers the test data records, over the same texts with the same flags as the Corvus rows (`gu` added), the matches counted by a `test` loop that advances `lastIndex` as `matchAll` does, which is the cheapest count V8 offers; its harness is `tools/benchmarks/v8/industry.mjs`, run under the same procedure. V8 has no vectorised search for a literal, a set or an alternation beyond its first character, so the rows that such a search decides read tens to hundreds of times slower there; the rows it wins are short fixed literals over its one-byte strings.

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

| Workload | .NET interpreter | Corvus interpreter | ratio | .NET generated | Corvus generated UTF-16 | Corvus generated UTF-8 | ratio UTF-16 | ratio UTF-8 |{v8_head}
|---|---|---|---|---|---|---|---|---|{v8_sep}
""" + "\n".join(lines) + "\n"
with open(os.path.join(out_dir, "industry.md"), "w", encoding="utf-8") as fo:
    fo.write(md)
print(summary)
above = sorted(((w, timed("Corvus generated UTF-16", w)["time"]["median"] / timed(".NET generated", w)["time"]["median"]) for w in workloads if timed(".NET generated", w) and timed("Corvus generated UTF-16", w)), key=lambda x: -x[1])
print("above 1.0 (generated):", [(w, round(r, 2)) for w, r in above if r > 1.0])
abovei = sorted(((w, timed("Corvus interpreter", w)["time"]["median"] / timed(".NET interpreter", w)["time"]["median"]) for w in workloads if timed(".NET interpreter", w) and timed("Corvus interpreter", w)), key=lambda x: -x[1])
print("above 1.0 (interpreter):", [(w, round(r, 2)) for w, r in abovei if r > 1.0])
