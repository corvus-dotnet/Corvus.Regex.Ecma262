#!/usr/bin/env python3
"""Summarises a benchmark run's result files as the tables of docs/benchmarks.

    tools/benchmarks/summarize.py <artifacts directory> > docs/benchmarks/<date>.md

It reads the *-report.csv files BenchmarkDotNet writes and, for each class, prints one row per workload with the
mean of every engine and the ratio of this library's UTF-16 mean to .NET's interpreter and to .NET compiled, then
the median and geometric mean of those ratios. A ratio below 1 is this library being faster.
"""
import csv
import glob
import os
import statistics
import sys

UNITS = {"ns": 1.0, "us": 1e3, "μs": 1e3, "ms": 1e6, "s": 1e9}

# The parameter columns of the benchmark classes.
PARAMS = ("Workload", "Id", "Compiled")

def nanoseconds(text):
    text = text.strip()
    if not text or text in ("NA", "-", "?"):
        return None
    value, unit = text.split()
    return float(value.replace(",", "")) * UNITS[unit]

def load(directory):
    runs = {}
    for path in glob.glob(os.path.join(directory, "results", "*-report.csv")):
        title = os.path.basename(path).split(".")[-2].replace("-report", "")
        with open(path, encoding="utf-8-sig", newline="") as f:
            for row in csv.DictReader(f):
                params = ", ".join(f"{k}={row[k]}" for k in PARAMS if row.get(k))
                runs.setdefault(title, {}).setdefault(params, {})[row["Method"]] = (nanoseconds(row["Mean"]), row.get("Allocated", ""))
    return runs

def fmt(ns):
    if ns is None:
        return "n/a"
    if ns < 1e3:
        return f"{ns:.1f} ns"
    if ns < 1e6:
        return f"{ns / 1e3:.2f} us"
    if ns < 1e9:
        return f"{ns / 1e6:.3f} ms"
    return f"{ns / 1e9:.3f} s"

def pick(means, *needles):
    for m, (mean, _) in means.items():
        if all(n in m for n in needles):
            return mean
    return None

def main():
    runs = load(sys.argv[1])
    for title, workloads in sorted(runs.items()):
        methods = []
        for w in workloads.values():
            for m in w:
                if m not in methods:
                    methods.append(m)
        print(f"## {title}\n")
        print("| Workload | " + " | ".join(methods) + " | ours/interp | ours/compiled | UTF-8/interp |")
        print("|---|" + "---|" * (len(methods) + 3))
        ratios_i, ratios_c, ratios_8 = [], [], []
        for params, means in workloads.items():
            interp = pick(means, "interpreter") or pick(means, "Net_")
            comp = pick(means, "Compiled")
            ours = pick(means, "UTF-16") or pick(means, "Corvus_")
            ours8 = pick(means, "UTF-8") or pick(means, "Utf8")
            ri = ours / interp if ours and interp else None
            rc = ours / comp if ours and comp else None
            r8 = ours8 / interp if ours8 and interp else None
            for lst, r in ((ratios_i, ri), (ratios_c, rc), (ratios_8, r8)):
                if r:
                    lst.append(r)
            cells = [fmt(means[m][0]) if m in means else "" for m in methods]
            tail = f"{ri:.2f}" if ri else ""
            tail += f" | {rc:.2f}" if rc else " |"
            tail += f" | {r8:.2f}" if r8 else " |"
            escaped = params.replace("|", "\\|")
            print(f"| {escaped} | " + " | ".join(cells) + f" | {tail} |")
        for name, lst in (("UTF-16 over the interpreter", ratios_i), ("UTF-16 over compiled", ratios_c), ("UTF-8 over the interpreter", ratios_8)):
            if lst:
                print(f"\n{name}: median {statistics.median(lst):.2f}, geometric mean {statistics.geometric_mean(lst):.2f}, at or below 1.0 on {sum(1 for r in lst if r <= 1)} of {len(lst)}.")
        print()

if __name__ == "__main__":
    main()
