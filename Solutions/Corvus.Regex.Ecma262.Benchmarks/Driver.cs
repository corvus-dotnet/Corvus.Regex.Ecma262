// <copyright file="Driver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// The Industry benchmark under the interleaved protocol of every other Corvus ECMA-262 engine's harness: a port of the
// Rust harness's driver (corvus-regex-ecma262-rs, tools/src/bin/industry.rs), which the C, C++, Python, Ruby, PHP,
// Swift, R and Fortran harnesses port too, so that every source on the central benchmark page is measured the same way.
//
//   Corvus.Regex.Ecma262.Benchmarks industry <workloads.json> [--engines corvus-utf16,corvus-utf8,...] [--v8 <industry.mjs>]
//            [--rounds 3] [--seconds 0.7] [--passes 7] [--spread 0.05] [--filter <substring of the id>]
//            [--out <results.json>]
//
// The protocol is interleaved, so that what changes on the host over a run (its clock, its heat, its other work) falls
// on every engine alike: the workloads are the outer loop, each workload's rounds next and the engines innermost, their
// order turned by one each round. Each figure is a child process of its own, so that one engine's caches, allocations
// and compiled code do not reach another's figures; it warms for two fifths of its seconds, then times `passes` passes
// of as many counts as fit the rest and reports their median. A workload's figure for an engine is the median of its
// rounds, with the fastest and the slowest round as its spread; a figure whose spread, (slowest - fastest) / median,
// is over --spread is listed in the run's facts (spread_over) and on stderr. A child that does not finish in two
// minutes is stopped and recorded as such.
//
// The engines: this library's interpreter over the string (corvus-utf16) and over its UTF-8 bytes (corvus-utf8), its
// generated code over each (corvus-generated-utf16, corvus-generated-utf8), each with the workload's flags and gu, as
// the BenchmarkDotNet classes Industry.cs and GeneratedIndustry.cs build them; .NET's interpreter (net-interpreter)
// and its source generator (net-generated), with the workload's .NET options; and V8 (--v8, the V8 harness run by
// node), with gu added. An engine written name@path is run by the harness at path (another build of this one), so that
// two builds can be timed side by side in the same rounds: an interleaved A/B.
//
// A .NET figure warms for at least 1.5 s before the protocol's warming and timing (see WarmFloor): .NET's tiered
// compiler needs that long to reach its final code, where the other harnesses' engines are compiled ahead.
//
// The texts are those of the reference run, read through Inputs (cached under the user's profile, or fetched from
// dotnet/performance when missing).
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Net = System.Text.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Benchmarks;

/// <summary>The generated regular expressions of each workload, for the driver.</summary>
public partial class GeneratedCount
{
    /// <summary>This library's generated regular expression of the workload at an index of <see cref="Workloads.All"/>.</summary>
    /// <param name="k">The index.</param>
    /// <returns>The regular expression.</returns>
    internal static Regex CorvusGeneratedAt(int k) => CorvusAll[k];

    /// <summary>.NET's generated regular expression of the workload at an index of <see cref="Workloads.All"/>.</summary>
    /// <param name="k">The index.</param>
    /// <returns>The regular expression.</returns>
    internal static Net.Regex NetGeneratedAt(int k) => NetAll[k];
}

/// <summary>The Industry benchmark under the interleaved protocol.</summary>
internal static class Driver
{
    private static readonly string[] DefaultEngines = ["corvus-utf16", "corvus-utf8", "corvus-generated-utf16", "corvus-generated-utf8", "net-interpreter", "net-generated"];

    /// <summary>Runs the driver when the arguments ask for it.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>Whether the arguments were the driver's.</returns>
    public static bool TryRun(string[] args)
    {
        if (args.Length > 0 && args[0] == "--one")
        {
            One(args[1], int.Parse(args[2], CultureInfo.InvariantCulture), args[3], double.Parse(args[4], CultureInfo.InvariantCulture), int.Parse(args[5], CultureInfo.InvariantCulture));
            return true;
        }

        if (args.Length > 1 && args[0] == "industry")
        {
            Run(args[1..]);
            return true;
        }

        return false;
    }

    private sealed record Entry(string Id, string Pattern, string Flags, string File);

    private static List<Entry> ReadWorkloads(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return [.. doc.RootElement.EnumerateArray().Select(w => new Entry(
            w.GetProperty("id").GetString()!,
            w.GetProperty("pattern").GetString()!,
            w.GetProperty("flags").GetString()!,
            w.GetProperty("file").GetString()!))];
    }

    /// <summary>The index in <see cref="Workloads.All"/> of the workload with the entry's pattern, flags and text, or -1.</summary>
    private static int IndexOf(Entry e)
    {
        int k = 0;
        foreach (Workload w in Workloads.All)
        {
            if (w.Pattern == e.Pattern && w.Flags == e.Flags && w.File == e.File)
            {
                return k;
            }

            k++;
        }

        return -1;
    }

    /// <summary>
    /// The least a figure warms for. .NET compiles a method again as it is used (tiered compilation, with dynamic
    /// profile-guided optimisation between the tiers, and loops replaced on the stack), and needs some hundreds of
    /// milliseconds of steady use to reach its final code: with the protocol's two fifths of 0.7 s alone, a figure can be
    /// that of code not yet fully compiled (sherlock's Holmes.{0,25}Watson|Watson.{0,25}Holmes over UTF-16 measured
    /// 442 us, against 55 to 60 us warmed for 0.8 s or more). So a figure warms for this long at least; the counts it
    /// times are sized from the last two fifths of its seconds of warming, as the protocol sizes them, and timed as the
    /// protocol times them. CORVUS_REGEX_WARM_SECONDS sets it.
    /// </summary>
    private static readonly double WarmFloor = double.TryParse(Environment.GetEnvironmentVariable("CORVUS_REGEX_WARM_SECONDS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double w) ? w : 1.5;

    /// <summary>Times a count: warm, then passes. The median, the fastest and the slowest pass in nanoseconds per count, the count, and the counts timed.</summary>
    private static (double Median, double Min, double Max, int Count, long Reps) Measure(double seconds, int passes, Func<int> count)
    {
        int expected = count();
        double window = seconds * 0.4;
        long start = Stopwatch.GetTimestamp();
        long windowStart = start + (long)(Math.Max(0, WarmFloor - window) * Stopwatch.Frequency);
        long warmEnd = start + (long)(Math.Max(window, WarmFloor) * Stopwatch.Frequency);
        long reps = 0;
        long now;
        while ((now = Stopwatch.GetTimestamp()) < warmEnd)
        {
            if (count() != expected)
            {
                throw new InvalidOperationException("the count changed");
            }

            if (now >= windowStart)
            {
                reps++;
            }
        }

        long perPass = Math.Max(1, (long)Math.Round(reps * 0.6 / passes));
        double[] times = new double[passes];
        long sink = 0;
        for (int p = 0; p < passes; p++)
        {
            long t0 = Stopwatch.GetTimestamp();
            for (long r = 0; r < perPass; r++)
            {
                sink += count();
            }

            times[p] = Stopwatch.GetElapsedTime(t0).TotalNanoseconds / perPass;
        }

        GC.KeepAlive(sink);
        Array.Sort(times);
        return (times[passes / 2], times[0], times[passes - 1], expected, perPass * passes);
    }

    /// <summary>Runs one workload on one engine in this process and prints its figures as one line of JSON.</summary>
    private static void One(string path, int index, string engine, double seconds, int passes)
    {
        Entry e = ReadWorkloads(path)[index];
        int k = IndexOf(e);
        string line;
        try
        {
            Func<int> count = engine switch
            {
                "corvus-utf16" => Counter(new Regex(e.Pattern, e.Flags + "gu"), Inputs.Text(e.File)),
                "corvus-utf8" => Counter(new Regex(e.Pattern, e.Flags + "gu"), Inputs.Utf8(e.File)),
                "corvus-generated-utf16" => Counter(Generated(k, e), Inputs.Text(e.File)),
                "corvus-generated-utf8" => Counter(Generated(k, e), Inputs.Utf8(e.File)),
                "net-interpreter" => NetCounter(new Net.Regex(Known(k, e).NetPattern, Known(k, e).Options), Inputs.Text(e.File)),
                "net-generated" => NetCounter(NetGenerated(k, e), Inputs.Text(e.File)),
                _ => throw new ArgumentException("unknown engine " + engine),
            };
            (double median, double min, double max, int n, long reps) = Measure(seconds, passes, count);
            line = string.Create(CultureInfo.InvariantCulture, $"{{\"median\":{median:F1},\"min\":{min:F1},\"max\":{max:F1},\"count\":{n},\"reps\":{reps}}}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            line = "{\"error\":" + JsonSerializer.Serialize(ex.Message) + "}";
        }

        Console.WriteLine(line);
    }

    private static Func<int> Counter(Regex re, string text) => () => re.Count(text);

    private static Func<int> Counter(Regex re, byte[] utf8) => () => re.Count(utf8);

    private static Func<int> NetCounter(Net.Regex re, string text) => () => re.Count(text);

    private static Workload Known(int k, Entry e) => k >= 0 ? Workloads.All.ElementAt(k) : throw new ArgumentException("no workload of this harness has the pattern " + e.Pattern);

    private static Regex Generated(int k, Entry e)
    {
        _ = Known(k, e);
        return GeneratedCount.CorvusGeneratedAt(k);
    }

    private static Net.Regex NetGenerated(int k, Entry e)
    {
        _ = Known(k, e);
        return GeneratedCount.NetGeneratedAt(k);
    }

    private static string FirstLine(string command, string arguments)
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo(command, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            string first = p.StandardOutput.ReadLine() ?? string.Empty;
            p.WaitForExit();
            return first;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static string Field(string file, string prefix, char separator, int part)
    {
        try
        {
            string? l = File.ReadLines(file).FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            return l is null ? string.Empty : l.Split(separator)[part].Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>What the run is: the host, the runtime, the affinity, the load and the commit, read from the run itself.</summary>
    private static Dictionary<string, string> Facts(string seconds, string passes)
    {
        string commit = Environment.GetEnvironmentVariable("CORVUS_REGEX_COMMIT") is { Length: > 0 } c ? c : FirstLine("git", "rev-parse HEAD");
        return new()
        {
            ["cpu"] = Field("/proc/cpuinfo", "model name", ':', 1),
            ["cores"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["affinity"] = Field("/proc/self/status", "Cpus_allowed_list", ':', 1),
            ["os"] = FirstLine("uname", "-sr"),
            ["load"] = Field("/proc/loadavg", string.Empty, ' ', 0),
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["commit"] = commit,
            ["date"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["seconds"] = seconds,
            ["passes"] = passes,
            ["runner"] = Environment.GetEnvironmentVariable("RUNNER_NAME") ?? string.Empty,
            ["runner_os"] = Environment.GetEnvironmentVariable("RUNNER_OS") ?? string.Empty,
            ["image"] = $"{Environment.GetEnvironmentVariable("ImageOS")} {Environment.GetEnvironmentVariable("ImageVersion")}".Trim(),
            ["corvus-regex-ecma262"] = typeof(Regex).Assembly.GetName().Version?.ToString() ?? string.Empty,
        };
    }

    /// <summary>One figure: an engine over a workload in a child process. The child's line of JSON, or one with the error.</summary>
    private static string Figure(string exe, string? v8, string path, int index, string engine, string seconds, string passes)
    {
        // An engine named name@path is name as the harness at path runs it: two builds side by side.
        int at = engine.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            exe = engine[(at + 1)..];
            engine = engine[..at];
        }

        ProcessStartInfo start = engine == "v8" ? new ProcessStartInfo("node") : new ProcessStartInfo(exe);
        start.RedirectStandardOutput = true;
        if (engine == "v8")
        {
            foreach (string a in new[] { v8 ?? throw new ArgumentException("the V8 harness (--v8)"), "--one", path, index.ToString(CultureInfo.InvariantCulture), seconds, passes })
            {
                start.ArgumentList.Add(a);
            }
        }
        else
        {
            foreach (string a in new[] { "--one", path, index.ToString(CultureInfo.InvariantCulture), engine, seconds, passes })
            {
                start.ArgumentList.Add(a);
            }
        }

        Process child;
        try
        {
            child = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return "{\"error\":" + JsonSerializer.Serialize(ex.Message) + "}";
        }

        using (child)
        {
            Task<string?> read = child.StandardOutput.ReadLineAsync();
            string line = read.Wait(TimeSpan.FromMinutes(2))
                ? read.Result ?? "{\"error\":\"the process ended without figures\"}"
                : "{\"error\":\"did not finish in two minutes\"}";
            try
            {
                child.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It has ended.
            }

            child.WaitForExit();
            return line;
        }
    }

    private static double Median(List<double> sorted)
    {
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[(n / 2) - 1] + sorted[n / 2]) / 2.0;
    }

    private static double? Number(string? line, string name)
    {
        if (line is null)
        {
            return null;
        }

        try
        {
            using JsonDocument d = JsonDocument.Parse(line);
            return d.RootElement.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(string? line, string name)
    {
        if (line is null)
        {
            return null;
        }

        try
        {
            using JsonDocument d = JsonDocument.Parse(line);
            return d.RootElement.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Run(string[] args)
    {
        string? Opt(string name)
        {
            int k = Array.IndexOf(args, name);
            return k >= 0 && k + 1 < args.Length ? args[k + 1] : null;
        }

        string path = Path.GetFullPath(args[0]);
        string? v8 = Opt("--v8");
        string[] engines = Opt("--engines")?.Split(',') ?? (v8 is null ? DefaultEngines : [.. DefaultEngines, "v8"]);
        int rounds = int.Parse(Opt("--rounds") ?? "3", CultureInfo.InvariantCulture);
        string seconds = Opt("--seconds") ?? "0.7";
        string passes = Opt("--passes") ?? "7";
        double limit = double.Parse(Opt("--spread") ?? "0.05", CultureInfo.InvariantCulture);
        string? filter = Opt("--filter");
        List<Entry> all = ReadWorkloads(path);
        List<int> chosen = [.. Enumerable.Range(0, all.Count).Where(k => filter is null || all[k].Id.Contains(filter, StringComparison.Ordinal))];
        string exe = Environment.ProcessPath!;

        // figures[e][k]: each round's median; lines[e][k]: the last line of each (for the count and errors).
        List<double>[][] figures = [.. engines.Select(_ => all.Select(_ => new List<double>()).ToArray())];
        string?[][] lines = [.. engines.Select(_ => new string?[all.Count])];
        string v8Engine = string.Empty;
        int done = 0;
        foreach (int k in chosen)
        {
            for (int round = 0; round < rounds; round++)
            {
                for (int turn = 0; turn < engines.Length; turn++)
                {
                    int e = (turn + round) % engines.Length;
                    string line = Figure(exe, v8, path, k, engines[e], seconds, passes);
                    if (Number(line, "median") is double m)
                    {
                        figures[e][k].Add(m);
                    }
                    else
                    {
                        Console.Error.WriteLine($"{engines[e],-24} {all[k].Id,-62} {line}");
                    }

                    if (engines[e] == "v8" && Text(line, "engine") is string name)
                    {
                        v8Engine = name;
                    }

                    lines[e][k] = line;
                }
            }

            Console.Error.WriteLine($"workload {++done} of {chosen.Count} done");
        }

        List<string> over = [];
        List<string> differ = [];
        int v8Index = Array.IndexOf(engines, "v8");
        StringBuilder results = new();
        for (int e = 0; e < engines.Length; e++)
        {
            string engine = engines[e];
            List<string> rows = [];
            foreach (int k in chosen)
            {
                string line = lines[e][k] ?? string.Empty;
                List<double> times = figures[e][k];
                string inOrder = string.Join(",", times.Select(t => t.ToString("F1", CultureInfo.InvariantCulture)));
                string entry;
                if (times.Count < rounds)
                {
                    // A round with no figure (an error, a limit) makes the figure an error.
                    entry = line.Contains("\"error\"", StringComparison.Ordinal) ? line : "{\"error\":\"a round gave no figure\"}";
                }
                else
                {
                    List<double> sorted = [.. times.Order()];
                    double m = Median(sorted), lo = sorted[0], hi = sorted[^1];
                    double spread = m > 0 ? (hi - lo) / m : 0;
                    if (spread > limit)
                    {
                        over.Add(string.Create(CultureInfo.InvariantCulture, $"{engine} {all[k].Id} {spread * 100:F1}%"));
                        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SPREAD {engine,-24} {all[k].Id,-62} {spread * 100:F1}% over {limit * 100:F1}%"));
                    }

                    double count = Number(line, "count") ?? 0;
                    string figure = string.Create(CultureInfo.InvariantCulture, $"\"median\":{m:F1},\"min\":{lo:F1},\"max\":{hi:F1},\"count\":{count},\"rounds\":{times.Count},\"spread\":{spread:F4},\"each\":[{inOrder}]");
                    double? oracle = v8Index >= 0 ? Number(lines[v8Index][k], "count") : null;
                    if (oracle is double o && o != count && engine != "v8")
                    {
                        differ.Add(string.Create(CultureInfo.InvariantCulture, $"{engine} {all[k].Id} {count} vs V8 {o}"));
                        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"COUNT  {engine,-24} {all[k].Id,-62} {count} against V8's {o}"));

                        // .NET's engines do not claim ECMA-262: a count of theirs that differs is marked, not refused.
                        entry = engine.StartsWith("net-", StringComparison.Ordinal)
                            ? string.Create(CultureInfo.InvariantCulture, $"{{{figure},\"differs\":\"{engine} {count}, V8 {o}\"}}")
                            : string.Create(CultureInfo.InvariantCulture, $"{{\"error\":\"counted {count} where V8 counts {o}\"}}");
                    }
                    else
                    {
                        entry = "{" + figure + "}";
                    }
                }

                rows.Add(JsonSerializer.Serialize(all[k].Id) + ":" + entry);
            }

            if (results.Length > 0)
            {
                results.Append(',');
            }

            results.Append(JsonSerializer.Serialize(engine)).Append(":{").AppendJoin(",", rows).Append('}');
        }

        Dictionary<string, string> facts = Facts(seconds, passes);
        facts["rounds"] = rounds.ToString(CultureInfo.InvariantCulture);
        facts["protocol"] = "interleaved";
        facts["spread_limit"] = limit.ToString(CultureInfo.InvariantCulture);
        facts["spread_over"] = string.Join("; ", over);
        facts["counts_differ"] = string.Join("; ", differ);
        facts["v8"] = v8Engine;
        string end = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string output = "{\"facts\":" + JsonSerializer.Serialize(facts) + ",\"end\":" + JsonSerializer.Serialize(end) + ",\"results\":{" + results + "}}\n";
        if (Opt("--out") is string file)
        {
            File.WriteAllText(file, output);
        }
        else
        {
            Console.Write(output);
        }
    }
}
