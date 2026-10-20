# Benchmarks: the Industry workloads

Commit 535de98, 2026-10-20. Host: WSL2 on Windows on an Intel Core i7-13800H, the VM pinned to the twelve performance cores (vmmemWSL affinity 0xFFF), the run under `taskset -c 0-11` after a settle of at least three minutes, the host probe at 13.6 to 14.2 ns before and 13.2 to 14.2 ns after (12.5 to 16 is the accepted band), .NET 10, BenchmarkDotNet short job, the `GeneratedCount` benchmark of `Solutions/Corvus.Regex.Ecma262.Benchmarks`.

The interactive page of this run is at [corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/](https://corvus-oss.org/Corvus.Regex.Ecma262/benchmarks/); its data is `industry.json` beside this file, and `tools/benchmarks/industry-results.py` writes both from a run.

The workloads are the Industry set of dotnet/performance (`Perf.Regex.Industry`: the Sherlock Holmes, Leipzig and mariomka texts and the rebar names), the set behind .NET's own "regex performance improvements in .NET N" tables. Each is run five ways: .NET's interpreter (`RegexOptions.None`), .NET's source generator (`[GeneratedRegex]`), this library's interpreter over UTF-16, and this library's source generator over UTF-16 and over UTF-8. `Count` over the text, one call per operation. A ratio below 1.0 is this library being faster.

The Corvus rows carry the `u` flag (the benchmark adds `gu` to every row, for the interpreter rows too), which .NET has no mode for: classes decode surrogate pairs and the `i` flag folds `s` to the long s and `k` to the Kelvin sign. Counts agree on 53 of the 57 rows; the four apart are `$` and `.` at CRLF, where ECMA-262 counts CR as a line terminator, and `\w`, which .NET takes as Unicode letters and digits. The UTF-8 rows match the same text as bytes, with nothing transcoded.

## Summary

- Interpreter against .NET's interpreter, over UTF-16: median 0.46, geometric mean 0.17, at or below 1.0 on 55 of 57.
- Generated code against .NET's generated code, over UTF-16: median 0.85, geometric mean 0.53, at or below 1.0 on 45 of 57.
- Generated code over UTF-8 against .NET's generated code: median 0.53, geometric mean 0.40, at or below 1.0 on 51 of 57.

## The rows

Times are microseconds for one `Count` over the text.

| Workload | .NET interpreter | Corvus interpreter | ratio | .NET generated | Corvus generated UTF-16 | Corvus generated UTF-8 | ratio UTF-16 | ratio UTF-8 |
|---|---|---|---|---|---|---|---|---|
| leipzig: ([A-Za-z]awyer\|[A-Za-z]inn)\\s | 422717.8 | 2902.3 | 0.01 | 10412.4 | 2972.2 | 1575.0 | 0.29 | 0.15 |
| leipzig: .{0,2}(Tom\|Sawyer\|Huckleberry\|Finn) | 1959717.0 | 2828.8 | 0.00 | 116695.2 | 2887.1 | 1175.9 | 0.02 | 0.01 |
| leipzig: .{2,4}(Tom\|Sawyer\|Huckleberry\|Finn) | 1940677.9 | 2898.4 | 0.00 | 134095.3 | 3169.7 | 1234.2 | 0.02 | 0.01 |
| leipzig: Huck[a-zA-Z]+\|Saw[a-zA-Z]+ | 3966.9 | 2690.1 | 0.68 | 2755.1 | 2485.2 | 1132.9 | 0.90 | 0.41 |
| leipzig: Tom.{10,25}river\|river.{10,25}Tom | 25324.9 | 3283.6 | 0.13 | 7661.5 | 3021.1 | 1358.4 | 0.39 | 0.18 |
| leipzig: Tom\|Sawyer\|Huckleberry\|Finn | 7578.3 | 2635.4 | 0.35 | 3147.7 | 2608.5 | 967.8 | 0.83 | 0.31 |
| leipzig: Tom\|Sawyer\|Huckleberry\|Finn /i | 193990.2 | 2846.8 | 0.01 | 2756.1 | 2885.5 | 2059.9 | 1.05 | 0.75 |
| leipzig: Twain | 2347.4 | 2493.9 | 1.06 | 2218.2 | 2262.6 | 640.6 | 1.02 | 0.29 |
| leipzig: Twain /i | 2842.6 | 2263.5 | 0.80 | 2293.8 | 2326.2 | 896.5 | 1.01 | 0.39 |
| leipzig: [a-z]shing | 2463.9 | 2125.7 | 0.86 | 2079.9 | 2100.2 | 658.3 | 1.01 | 0.32 |
| leipzig: \\p{Sm} | 12842.3 | 1974.3 | 0.15 | 2028.2 | 2164.2 | 641.2 | 1.07 | 0.32 |
| mariomka: (?:(?:25[0-5]\|2[0-4][0-9]\|[01]?[0-9][... | 5974.2 | 4886.4 | 0.82 | 2647.5 | 2372.3 | 1891.5 | 0.90 | 0.71 |
| mariomka: [\\w\\.+-]+@[\\w\\.-]+\\.[\\w\\.-]+ | 352.6 | 330.2 | 0.94 | 303.4 | 327.0 | 153.1 | 1.08 | 0.50 |
| mariomka: [\\w]+://[^/\\s?#]+[^\\s?#]+(?:\\?[^\\s#]*... | 1402.9 | 1106.7 | 0.79 | 830.4 | 665.5 | 372.7 | 0.80 | 0.45 |
| rebar-ru: Шерлок Холмс | 67.2 | 51.5 | 0.77 | 57.5 | 51.9 | 50.1 | 0.90 | 0.87 |
| rebar-ru: Шерлок Холмс /i | 246.5 | 64.0 | 0.26 | 114.7 | 67.9 | 114.9 | 0.59 | 1.00 |
| rebar-ru: Шерлок Холмс\|Джон Уотсон\|Ирен Адлер\|и... | 3562.6 | 126.2 | 0.04 | 1358.2 | 127.6 | 125.1 | 0.09 | 0.09 |
| rebar-ru: Шерлок Холмс\|Джон Уотсон\|Ирен Адлер\|и... /i | 16380.6 | 189.7 | 0.01 | 1216.5 | 165.6 | 304.3 | 0.14 | 0.25 |
| rebar-zh: 夏洛克·福尔摩斯 | 17.4 | 16.7 | 0.96 | 17.1 | 17.3 | 22.2 | 1.01 | 1.29 |
| rebar-zh: 夏洛克·福尔摩斯\|约翰华生\|阿德勒\|雷斯垂德\|莫里亚蒂教授 | 50.6 | 25.4 | 0.50 | 24.6 | 24.4 | 46.9 | 0.99 | 1.91 |
| sherlock: .* | 641.4 | 426.7 | 0.67 | 311.2 | 445.0 | 404.1 | 1.43 | 1.30 |
| sherlock: .* /s | 584.4 | 0.0 | 0.00 | 0.0 | 0.0 | 0.0 | 1.50 | 1.00 |
| sherlock: Holmes | 46.6 | 27.4 | 0.59 | 36.4 | 25.1 | 18.6 | 0.69 | 0.51 |
| sherlock: Holmes /i | 77.9 | 33.5 | 0.43 | 39.5 | 32.8 | 32.0 | 0.83 | 0.81 |
| sherlock: Holmes.{0,25}Watson\|Watson.{0,25}Holmes | 176.1 | 53.7 | 0.30 | 38.8 | 36.5 | 25.4 | 0.94 | 0.65 |
| sherlock: Sher[a-z]+\|Hol[a-z]+ | 112.6 | 56.4 | 0.50 | 45.9 | 33.0 | 24.3 | 0.72 | 0.53 |
| sherlock: Sher[a-z]+\|Hol[a-z]+ /i | 2633.8 | 91.6 | 0.03 | 65.4 | 70.0 | 62.9 | 1.07 | 0.96 |
| sherlock: Sherlock | 35.4 | 21.3 | 0.60 | 30.7 | 21.6 | 15.4 | 0.71 | 0.50 |
| sherlock: Sherlock /i | 57.3 | 26.2 | 0.46 | 31.9 | 27.5 | 28.5 | 0.86 | 0.89 |
| sherlock: Sherlock Holmes | 31.4 | 21.5 | 0.69 | 28.2 | 20.8 | 16.8 | 0.74 | 0.60 |
| sherlock: Sherlock Holmes /i | 59.7 | 27.1 | 0.45 | 29.9 | 27.2 | 27.5 | 0.91 | 0.92 |
| sherlock: Sherlock\\s+Holmes | 33.4 | 24.9 | 0.75 | 29.4 | 21.3 | 13.5 | 0.72 | 0.46 |
| sherlock: Sherlock\|Holmes | 104.4 | 48.5 | 0.46 | 35.3 | 35.2 | 22.2 | 1.00 | 0.63 |
| sherlock: Sherlock\|Holmes\|Watson | 157.5 | 48.0 | 0.30 | 46.1 | 44.3 | 38.3 | 0.96 | 0.83 |
| sherlock: Sherlock\|Holmes\|Watson /i | 4331.8 | 65.2 | 0.02 | 74.3 | 64.0 | 58.5 | 0.86 | 0.79 |
| sherlock: Sherlock\|Holmes\|Watson\|Irene\|Adler\|Jo... | 1244.0 | 51.0 | 0.04 | 119.0 | 62.3 | 49.9 | 0.52 | 0.42 |
| sherlock: Sherlock\|Holmes\|Watson\|Irene\|Adler\|Jo... /i | 14226.0 | 83.7 | 0.01 | 216.0 | 80.4 | 70.1 | 0.37 | 0.32 |
| sherlock: Sherlock\|Street | 44.7 | 24.1 | 0.54 | 22.7 | 22.5 | 14.1 | 0.99 | 0.62 |
| sherlock: The | 52.2 | 41.1 | 0.79 | 40.2 | 36.9 | 20.6 | 0.92 | 0.51 |
| sherlock: [^\\n]* | 626.5 | 294.7 | 0.47 | 349.2 | 297.5 | 298.6 | 0.85 | 0.86 |
| sherlock: [a-q][^u-z]{13}x | 31.3 | 32.0 | 1.02 | 21.4 | 24.4 | 19.2 | 1.14 | 0.90 |
| sherlock: [a-zA-Z]+ing | 7086.1 | 228.2 | 0.03 | 2788.6 | 103.7 | 102.7 | 0.04 | 0.04 |
| sherlock: \\b\\w+n\\b | 11854.6 | 1806.8 | 0.15 | 5313.6 | 983.1 | 996.5 | 0.19 | 0.19 |
| sherlock: \\p{Ll} | 11359.1 | 4103.8 | 0.36 | 4192.7 | 4110.9 | 4210.8 | 0.98 | 1.00 |
| sherlock: \\p{Lu} | 880.9 | 279.1 | 0.32 | 300.4 | 281.9 | 257.9 | 0.94 | 0.86 |
| sherlock: \\p{L} | 12358.8 | 4347.4 | 0.35 | 4495.2 | 4467.1 | 4468.4 | 0.99 | 0.99 |
| sherlock: \\s[a-zA-Z]{0,12}ing\\s | 8036.8 | 680.5 | 0.08 | 2901.5 | 455.8 | 455.6 | 0.16 | 0.16 |
| sherlock: \\w+ | 4925.5 | 1791.6 | 0.36 | 2842.2 | 1781.5 | 2073.6 | 0.63 | 0.73 |
| sherlock: \\w+\\s+Holmes | 5082.7 | 41.2 | 0.01 | 2275.4 | 34.9 | 26.3 | 0.02 | 0.01 |
| sherlock: \\w+\\s+Holmes\\s+\\w+ | 5189.6 | 39.9 | 0.01 | 2394.0 | 33.6 | 24.0 | 0.01 | 0.01 |
| sherlock: ^Sherlock Holmes\|Sherlock Holmes$ /m | 33.0 | 22.8 | 0.69 | 28.4 | 20.2 | 13.1 | 0.71 | 0.46 |
| sherlock: aei | 30.5 | 25.7 | 0.84 | 26.9 | 25.8 | 15.5 | 0.96 | 0.58 |
| sherlock: aqj | 30.6 | 17.8 | 0.58 | 26.1 | 18.7 | 11.7 | 0.72 | 0.45 |
| sherlock: the | 309.8 | 157.1 | 0.51 | 182.1 | 141.0 | 124.4 | 0.77 | 0.68 |
| sherlock: the /i | 406.4 | 233.3 | 0.57 | 198.2 | 199.0 | 226.7 | 1.00 | 1.14 |
| sherlock: the\\s+\\w+ | 509.8 | 353.0 | 0.69 | 322.4 | 248.6 | 231.7 | 0.77 | 0.72 |
| sherlock: zqj | 30.8 | 16.1 | 0.52 | 26.7 | 16.5 | 10.3 | 0.62 | 0.38 |
