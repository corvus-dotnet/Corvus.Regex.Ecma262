# Benchmarks: the Industry workloads

- **Commit** 535de98, run on 2026-10-20.
- **Host** an Intel Core i7-13800H under WSL2 on Windows, the virtual machine pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under `taskset -c 0-11` after a settle of at least three minutes.
- **Health probe** 13.6 to 14.2 ns before the run and 13.2 to 14.2 ns after; 12.5 to 16 is the band that makes a run valid.
- **Runtime and harness** .NET 10, BenchmarkDotNet's short job (one launch, three warm-ups, three iterations), the `GeneratedCount` benchmark of `Solutions/Corvus.Regex.Ecma262.Benchmarks`.

The interactive page of this run is at [corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/](https://corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/); its data is `industry.json` beside this file, and `tools/benchmarks/industry-results.py` writes both from a run.

The workloads are the Industry set of dotnet/performance (`Perf.Regex.Industry`: the Sherlock Holmes, Leipzig and mariomka texts and the rebar names), the set behind .NET's own "regex performance improvements in .NET N" tables. Each is run five ways: .NET's interpreter (`RegexOptions.None`), .NET's source generator (`[GeneratedRegex]`), this library's interpreter over UTF-16, and this library's source generator over UTF-16 and over UTF-8. `Count` over the text, one call per operation. A ratio below 1.0 is this library being faster.

V8 is the sixth engine: Node 24's `RegExp`, the oracle of the test data, over the same texts with the same flags as the Corvus rows (`gu` added), the matches counted by a `test` loop that advances `lastIndex` as `matchAll` does, which is the cheapest count V8 offers; its harness is `tools/benchmarks/v8/industry.mjs`, run under the same procedure. V8 has no vectorised search for a literal, a set or an alternation beyond its first character, so the rows that such a search decides read tens to hundreds of times slower there; the rows it wins are short fixed literals over its one-byte strings.

The Corvus rows run under the `u` flag, which .NET has no mode for, so the two engines answer by different rules on a few rows: counts agree on 53 of the 57, and the four apart are `$` and `.` at CRLF, where ECMA-262 counts CR as a line terminator, and `\w`, which .NET takes as Unicode letters and digits. The UTF-8 rows match the same text as bytes, with nothing transcoded.

## Where this library is slower than .NET, and why

Four kinds of row read above 1.0, each the price of an ECMA-262 rule that .NET does not apply:

1. **`.` and `$` at CRLF.** ECMA-262's `.` stops at CR as well as LF, so over a text with CRLF endings `.*` finds 36,491 matches to .NET's 26,105, at parity per match (`.*` at 1.43).
2. **Case-insensitive patterns under the u flag.** The search must find the long s and the Kelvin sign as forms of `s` and `k`, which .NET's search ignores, so its tables carry every form (`Sher[a-z]+|Hol[a-z]+ /i` at 1.07; `the /i` over UTF-8 at 1.14).
3. **A class that admits a supplementary character before a literal at a fixed distance.** Such a character is two units, so the distance is not fixed and the literal is found by a walk back rather than at an offset (`[a-q][^u-z]13x` at 1.14).
4. **Non-ASCII literals over UTF-8.** Every Cyrillic or Chinese character is two or three bytes with no rare byte among them, where UTF-16 gives the search one rare unit per character; so the UTF-8 rows for the Chinese names read 1.3 to 1.9 against .NET over UTF-16 while the UTF-16 rows are level.

The other rows above 1.0 are within a few percent of parity and read either side of it from run to run: both engines are at the floor of one vectorised search over the text.

## Summary

- Interpreter against .NET's interpreter, over UTF-16: median 0.46, geometric mean 0.17, at or below 1.0 on 55 of 57.
- Generated code against .NET's generated code, over UTF-16: median 0.85, geometric mean 0.53, at or below 1.0 on 45 of 57.
- Generated code over UTF-8 against .NET's generated code: median 0.53, geometric mean 0.40, at or below 1.0 on 51 of 57.
- Generated code over UTF-16 against V8 (Node 24): median 0.16, geometric mean 0.14, at or below 1.0 on 52 of 57.

## The rows

Times are microseconds for one `Count` over the text.

| Workload | .NET interpreter | Corvus interpreter | ratio | .NET generated | Corvus generated UTF-16 | Corvus generated UTF-8 | ratio UTF-16 | ratio UTF-8 | V8 (Node 24) | ratio to V8 |
|---|---|---|---|---|---|---|---|---|---|---|
| <code>leipzig\: \(\[A\-Za\-z\]awyer\|\[A\-Za\-z\]inn\)\\s</code> | 422717.8 | 2902.3 | 0.01 | 10412.4 | 2972.2 | 1575.0 | 0.29 | 0.15 | 12656.6 | 0.23 |
| <code>leipzig\: \.\{0\,2\}\(Tom\|Sawyer\|Huckleberry\|Finn\)</code> | 1959717.0 | 2828.8 | 0.00 | 116695.2 | 2887.1 | 1175.9 | 0.02 | 0.01 | 54052.4 | 0.05 |
| <code>leipzig\: \.\{2\,4\}\(Tom\|Sawyer\|Huckleberry\|Finn\)</code> | 1940677.9 | 2898.4 | 0.00 | 134095.3 | 3169.7 | 1234.2 | 0.02 | 0.01 | 205262.6 | 0.02 |
| <code>leipzig\: Huck\[a\-zA\-Z\]\+\|Saw\[a\-zA\-Z\]\+</code> | 3966.9 | 2690.1 | 0.68 | 2755.1 | 2485.2 | 1132.9 | 0.90 | 0.41 | 9234.8 | 0.27 |
| <code>leipzig\: Tom\.\{10\,25\}river\|river\.\{10\,25\}Tom</code> | 25324.9 | 3283.6 | 0.13 | 7661.5 | 3021.1 | 1358.4 | 0.39 | 0.18 | 11795.0 | 0.26 |
| <code>leipzig\: Tom\|Sawyer\|Huckleberry\|Finn</code> | 7578.3 | 2635.4 | 0.35 | 3147.7 | 2608.5 | 967.8 | 0.83 | 0.31 | 18327.5 | 0.14 |
| <code>leipzig\: Tom\|Sawyer\|Huckleberry\|Finn \/i</code> | 193990.2 | 2846.8 | 0.01 | 2756.1 | 2885.5 | 2059.9 | 1.05 | 0.75 | 38283.8 | 0.08 |
| <code>leipzig\: Twain</code> | 2347.4 | 2493.9 | 1.06 | 2218.2 | 2262.6 | 640.6 | 1.02 | 0.29 | 2436.4 | 0.93 |
| <code>leipzig\: Twain \/i</code> | 2842.6 | 2263.5 | 0.80 | 2293.8 | 2326.2 | 896.5 | 1.01 | 0.39 | 10404.4 | 0.22 |
| <code>leipzig\: \[a\-z\]shing</code> | 2463.9 | 2125.7 | 0.86 | 2079.9 | 2100.2 | 658.3 | 1.01 | 0.32 | 9271.2 | 0.23 |
| <code>leipzig\: \\p\{Sm\}</code> | 12842.3 | 1974.3 | 0.15 | 2028.2 | 2164.2 | 641.2 | 1.07 | 0.32 | 115577.9 | 0.02 |
| <code>mariomka\: \(\?\:\(\?\:25\[0\-5\]\|2\[0\-4\]\[0\-9\]\|\[01\]\?\[0\-9\]\[\.\.\.</code> | 5974.2 | 4886.4 | 0.82 | 2647.5 | 2372.3 | 1891.5 | 0.90 | 0.71 | 774.5 | 3.06 |
| <code>mariomka\: \[\\w\\\.\+\-\]\+\@\[\\w\\\.\-\]\+\\\.\[\\w\\\.\-\]\+</code> | 352.6 | 330.2 | 0.94 | 303.4 | 327.0 | 153.1 | 1.08 | 0.50 | 58004.0 | 0.01 |
| <code>mariomka\: \[\\w\]\+\:\/\/\[\^\/\\s\?\#\]\+\[\^\\s\?\#\]\+\(\?\:\\\?\[\^\\s\#\]\*\.\.\.</code> | 1402.9 | 1106.7 | 0.79 | 830.4 | 665.5 | 372.7 | 0.80 | 0.45 | 44431.5 | 0.01 |
| <code>rebar\-ru\: Шерлок Холмс</code> | 67.2 | 51.5 | 0.77 | 57.5 | 51.9 | 50.1 | 0.90 | 0.87 | 39.6 | 1.31 |
| <code>rebar\-ru\: Шерлок Холмс \/i</code> | 246.5 | 64.0 | 0.26 | 114.7 | 67.9 | 114.9 | 0.59 | 1.00 | 437.0 | 0.16 |
| <code>rebar\-ru\: Шерлок Холмс\|Джон Уотсон\|Ирен Адлер\|и\.\.\.</code> | 3562.6 | 126.2 | 0.04 | 1358.2 | 127.6 | 125.1 | 0.09 | 0.09 | 1595.9 | 0.08 |
| <code>rebar\-ru\: Шерлок Холмс\|Джон Уотсон\|Ирен Адлер\|и\.\.\. \/i</code> | 16380.6 | 189.7 | 0.01 | 1216.5 | 165.6 | 304.3 | 0.14 | 0.25 | 2553.7 | 0.06 |
| <code>rebar\-zh\: 夏洛克·福尔摩斯</code> | 17.4 | 16.7 | 0.96 | 17.1 | 17.3 | 22.2 | 1.01 | 1.29 | 70.6 | 0.25 |
| <code>rebar\-zh\: 夏洛克·福尔摩斯\|约翰华生\|阿德勒\|雷斯垂德\|莫里亚蒂教授</code> | 50.6 | 25.4 | 0.50 | 24.6 | 24.4 | 46.9 | 0.99 | 1.91 | 115.9 | 0.21 |
| <code>sherlock\: \.\*</code> | 641.4 | 426.7 | 0.67 | 311.2 | 445.0 | 404.1 | 1.43 | 1.30 | 777.4 | 0.57 |
| <code>sherlock\: \.\* \/s</code> | 584.4 | 0.020 | 0.00 | 0.025 | 0.037 | 0.024 | 1.50 | 1.00 | 77.7 | 0.00 |
| <code>sherlock\: Holmes</code> | 46.6 | 27.4 | 0.59 | 36.4 | 25.1 | 18.6 | 0.69 | 0.51 | 18.6 | 1.35 |
| <code>sherlock\: Holmes \/i</code> | 77.9 | 33.5 | 0.43 | 39.5 | 32.8 | 32.0 | 0.83 | 0.81 | 285.1 | 0.12 |
| <code>sherlock\: Holmes\.\{0\,25\}Watson\|Watson\.\{0\,25\}Holmes</code> | 176.1 | 53.7 | 0.30 | 38.8 | 36.5 | 25.4 | 0.94 | 0.65 | 443.6 | 0.08 |
| <code>sherlock\: Sher\[a\-z\]\+\|Hol\[a\-z\]\+</code> | 112.6 | 56.4 | 0.50 | 45.9 | 33.0 | 24.3 | 0.72 | 0.53 | 416.1 | 0.08 |
| <code>sherlock\: Sher\[a\-z\]\+\|Hol\[a\-z\]\+ \/i</code> | 2633.8 | 91.6 | 0.03 | 65.4 | 70.0 | 62.9 | 1.07 | 0.96 | 507.7 | 0.14 |
| <code>sherlock\: Sherlock</code> | 35.4 | 21.3 | 0.60 | 30.7 | 21.6 | 15.4 | 0.71 | 0.50 | 52.2 | 0.41 |
| <code>sherlock\: Sherlock \/i</code> | 57.3 | 26.2 | 0.46 | 31.9 | 27.5 | 28.5 | 0.86 | 0.89 | 258.5 | 0.11 |
| <code>sherlock\: Sherlock Holmes</code> | 31.4 | 21.5 | 0.69 | 28.2 | 20.8 | 16.8 | 0.74 | 0.60 | 23.2 | 0.90 |
| <code>sherlock\: Sherlock Holmes \/i</code> | 59.7 | 27.1 | 0.45 | 29.9 | 27.2 | 27.5 | 0.91 | 0.92 | 258.5 | 0.11 |
| <code>sherlock\: Sherlock\\s\+Holmes</code> | 33.4 | 24.9 | 0.75 | 29.4 | 21.3 | 13.5 | 0.72 | 0.46 | 213.2 | 0.10 |
| <code>sherlock\: Sherlock\|Holmes</code> | 104.4 | 48.5 | 0.46 | 35.3 | 35.2 | 22.2 | 1.00 | 0.63 | 348.3 | 0.10 |
| <code>sherlock\: Sherlock\|Holmes\|Watson</code> | 157.5 | 48.0 | 0.30 | 46.1 | 44.3 | 38.3 | 0.96 | 0.83 | 205.9 | 0.22 |
| <code>sherlock\: Sherlock\|Holmes\|Watson \/i</code> | 4331.8 | 65.2 | 0.02 | 74.3 | 64.0 | 58.5 | 0.86 | 0.79 | 464.6 | 0.14 |
| <code>sherlock\: Sherlock\|Holmes\|Watson\|Irene\|Adler\|Jo\.\.\.</code> | 1244.0 | 51.0 | 0.04 | 119.0 | 62.3 | 49.9 | 0.52 | 0.42 | 380.4 | 0.16 |
| <code>sherlock\: Sherlock\|Holmes\|Watson\|Irene\|Adler\|Jo\.\.\. \/i</code> | 14226.0 | 83.7 | 0.01 | 216.0 | 80.4 | 70.1 | 0.37 | 0.32 | 1115.2 | 0.07 |
| <code>sherlock\: Sherlock\|Street</code> | 44.7 | 24.1 | 0.54 | 22.7 | 22.5 | 14.1 | 0.99 | 0.62 | 333.8 | 0.07 |
| <code>sherlock\: The</code> | 52.2 | 41.1 | 0.79 | 40.2 | 36.9 | 20.6 | 0.92 | 0.51 | 22.9 | 1.61 |
| <code>sherlock\: \[\^\\n\]\*</code> | 626.5 | 294.7 | 0.47 | 349.2 | 297.5 | 298.6 | 0.85 | 0.86 | 600.9 | 0.50 |
| <code>sherlock\: \[a\-q\]\[\^u\-z\]\{13\}x</code> | 31.3 | 32.0 | 1.02 | 21.4 | 24.4 | 19.2 | 1.14 | 0.90 | 9571.9 | 0.00 |
| <code>sherlock\: \[a\-zA\-Z\]\+ing</code> | 7086.1 | 228.2 | 0.03 | 2788.6 | 103.7 | 102.7 | 0.04 | 0.04 | 3242.3 | 0.03 |
| <code>sherlock\: \\b\\w\+n\\b</code> | 11854.6 | 1806.8 | 0.15 | 5313.6 | 983.1 | 996.5 | 0.19 | 0.19 | 2269.2 | 0.43 |
| <code>sherlock\: \\p\{Ll\}</code> | 11359.1 | 4103.8 | 0.36 | 4192.7 | 4110.9 | 4210.8 | 0.98 | 1.00 | 7351.0 | 0.56 |
| <code>sherlock\: \\p\{Lu\}</code> | 880.9 | 279.1 | 0.32 | 300.4 | 281.9 | 257.9 | 0.94 | 0.86 | 445.8 | 0.63 |
| <code>sherlock\: \\p\{L\}</code> | 12358.8 | 4347.4 | 0.35 | 4495.2 | 4467.1 | 4468.4 | 0.99 | 0.99 | 7565.6 | 0.59 |
| <code>sherlock\: \\s\[a\-zA\-Z\]\{0\,12\}ing\\s</code> | 8036.8 | 680.5 | 0.08 | 2901.5 | 455.8 | 455.6 | 0.16 | 0.16 | 2331.8 | 0.20 |
| <code>sherlock\: \\w\+</code> | 4925.5 | 1791.6 | 0.36 | 2842.2 | 1781.5 | 2073.6 | 0.63 | 0.73 | 2807.4 | 0.63 |
| <code>sherlock\: \\w\+\\s\+Holmes</code> | 5082.7 | 41.2 | 0.01 | 2275.4 | 34.9 | 26.3 | 0.02 | 0.01 | 4240.4 | 0.01 |
| <code>sherlock\: \\w\+\\s\+Holmes\\s\+\\w\+</code> | 5189.6 | 39.9 | 0.01 | 2394.0 | 33.6 | 24.0 | 0.01 | 0.01 | 4191.0 | 0.01 |
| <code>sherlock\: \^Sherlock Holmes\|Sherlock Holmes\$ \/m</code> | 33.0 | 22.8 | 0.69 | 28.4 | 20.2 | 13.1 | 0.71 | 0.46 | 209.4 | 0.10 |
| <code>sherlock\: aei</code> | 30.5 | 25.7 | 0.84 | 26.9 | 25.8 | 15.5 | 0.96 | 0.58 | 195.7 | 0.13 |
| <code>sherlock\: aqj</code> | 30.6 | 17.8 | 0.58 | 26.1 | 18.7 | 11.7 | 0.72 | 0.45 | 195.7 | 0.10 |
| <code>sherlock\: the</code> | 309.8 | 157.1 | 0.51 | 182.1 | 141.0 | 124.4 | 0.77 | 0.68 | 449.5 | 0.31 |
| <code>sherlock\: the \/i</code> | 406.4 | 233.3 | 0.57 | 198.2 | 199.0 | 226.7 | 1.00 | 1.14 | 532.7 | 0.37 |
| <code>sherlock\: the\\s\+\\w\+</code> | 509.8 | 353.0 | 0.69 | 322.4 | 248.6 | 231.7 | 0.77 | 0.72 | 505.2 | 0.49 |
| <code>sherlock\: zqj</code> | 30.8 | 16.1 | 0.52 | 26.7 | 16.5 | 10.3 | 0.62 | 0.38 | 6.39 | 2.58 |
