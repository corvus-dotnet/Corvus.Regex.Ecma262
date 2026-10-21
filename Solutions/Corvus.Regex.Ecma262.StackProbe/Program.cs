// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Asks the library about patterns built to overflow the stack: deep (groups, lookarounds, modifier groups, quantified
// groups and the classes of the v flag, each nested to the limit, one past it and far past it), wide (many groups,
// classes, alternatives, backreferences, set operations and string alternatives side by side) and long (the longest
// pattern allowed, and one past it), in every grammar, through every member over UTF-16 and UTF-8. The probes are
// those of the Rust crate's tools/src/bin/stack-probe.rs.
//
//   StackProbe [--stacks 8M,1M,256K] [--jobs 4] [--filter text]
//   StackProbe --list
//   StackProbe --child <index> <stack>       (STACK_PROBE_TIMES=1 prints the time of each member to stderr)
//
// Each probe runs in a process of its own on a thread with each of the stacks, since a stack overflow ends the
// process and no caller can catch it. A probe is answered (compiled and matched), refused (not valid, or too large,
// which a caller can catch), or a fault: the process died or made no answer within two minutes. Any fault makes the
// exit status 1. Each child's heap is capped at 2 GB, so a probe that needs more is a fault (out of memory) too.
using System.Buffers;
using System.Diagnostics;
using System.Text;
using Corvus.Ecma262.RegularExpressions;

if (args.Length == 1 && args[0] == "--list")
{
    List<Probe> listed = Probes();
    for (int i = 0; i < listed.Count; i++)
    {
        Console.WriteLine($"{i}\t{listed[i].Name}");
    }

    return 0;
}

if (args.Length == 3 && args[0] == "--child")
{
    Child(int.Parse(args[1]), int.Parse(args[2]));
    return 0;
}

string stacksArgument = "8M,1M,256K";
int jobs = 4;
string filter = string.Empty;
for (int k = 0; k + 1 < args.Length; k += 2)
{
    switch (args[k])
    {
        case "--stacks": stacksArgument = args[k + 1]; break;
        case "--jobs": jobs = int.Parse(args[k + 1]); break;
        case "--filter": filter = args[k + 1]; break;
        default: throw new ArgumentException("unknown argument " + args[k]);
    }
}

(string Name, int Size)[] stacks = stacksArgument.Split(',').Select(s => (s, Size(s))).ToArray();
List<Probe> all = Probes();
List<(int Probe, int Stack)> work = Enumerable.Range(0, all.Count)
    .Where(i => all[i].Name.Contains(filter, StringComparison.Ordinal))
    .SelectMany(i => Enumerable.Range(0, stacks.Length).Select(s => (i, s)))
    .ToList();
string self = typeof(Probe).Assembly.Location;
int next = -1;
List<(int Probe, int Stack, string Outcome)> results = [];
Stopwatch started = Stopwatch.StartNew();
Thread[] workers = Enumerable.Range(0, jobs).Select(worker => new Thread(() =>
{
    while (true)
    {
        int n = Interlocked.Increment(ref next);
        if (n >= work.Count)
        {
            return;
        }

        (int i, int s) = work[n];
        ProcessStartInfo start = new(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };

        // Each child's heap is capped, so that a probe that wants more memory than the box can spare is a fault of its
        // own (out of memory) rather than a box that runs out.
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x80000000";
        start.ArgumentList.Add(self);
        start.ArgumentList.Add("--child");
        start.ArgumentList.Add(i.ToString());
        start.ArgumentList.Add(stacks[s].Size.ToString());
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        string outcome;
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            outcome = "FAULT no answer in two minutes";
        }
        else
        {
            outcome = process.ExitCode == 0 ? output.Result.Trim() : "FAULT died: exit code " + process.ExitCode;
        }

        lock (results)
        {
            results.Add((i, s, outcome));
        }
    }
})).ToArray();
foreach (Thread worker in workers)
{
    worker.Start();
}

foreach (Thread worker in workers)
{
    worker.Join();
}

int answered = 0, refused = 0, faults = 0;
foreach ((int i, int s, string outcome) in results.OrderBy(r => r.Probe).ThenBy(r => r.Stack))
{
    if (outcome.StartsWith("answered", StringComparison.Ordinal))
    {
        answered++;
    }
    else if (outcome.StartsWith("refused", StringComparison.Ordinal))
    {
        refused++;
    }
    else
    {
        faults++;
        Console.WriteLine($"{all[i].Name} on {stacks[s].Name}: {outcome}");
    }
}

Console.WriteLine($"{work.Count / stacks.Length} probes on {stacks.Length} stacks: {answered} answered, {refused} refused, {faults} faults, in {started.Elapsed.TotalSeconds:F0} s");
return faults > 0 ? 1 : 0;

static int Size(string text)
{
    int number = int.Parse(text[..^1]);
    return text[^1] switch
    {
        'K' => number << 10,
        'M' => number << 20,
        _ => throw new ArgumentException("a stack size such as 256K or 8M"),
    };
}

static string Nest(string open, string body, string close, int depth) =>
    string.Concat(Enumerable.Repeat(open, depth)) + body + string.Concat(Enumerable.Repeat(close, depth));

static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

static string Text(int n) => new string('a', Math.Clamp(n, 64, 1 << 20)) + "b";

static List<Probe> Probes()
{
    List<Probe> all = [];
    int[] depths = [32, 33, 200, 201, 1000, 5000, 200_000];
    string[] grammars = ["", "u", "v", "i", "iv"];

    (string Kind, string Open, string Close)[] groups =
    [
        ("group", "(", ")"),
        ("non-capturing", "(?:", ")"),
        ("lookahead", "(?=", ")"),
        ("negative lookahead", "(?!", ")"),
        ("lookbehind", "(?<=", ")"),
        ("negative lookbehind", "(?<!", ")"),
        ("modifier i", "(?i:", ")"),
        ("modifier -i", "(?-i:", ")"),
        ("star", "(?:", ")*"),
        ("counted", "(?:", "){2}"),
        ("lazy plus", "(?:", ")+?"),
        ("alternative", "(?:b|", ")"),
    ];
    foreach ((string kind, string open, string close) in groups)
    {
        foreach (int depth in depths)
        {
            foreach (string flags in grammars)
            {
                all.Add(new($"deep {kind} x{depth} /{flags}", flags, () => (Nest(open, "a", close, depth), Text(depth))));
            }
        }
    }

    foreach (int depth in depths)
    {
        foreach (string flags in grammars)
        {
            all.Add(new($"deep named x{depth} /{flags}", flags, () => (string.Concat(Enumerable.Range(0, depth).Select(i => $"(?<g{i}>")) + "a" + new string(')', depth), Text(depth))));
            all.Add(new($"deep backreference x{depth} /{flags}", flags, () => (Nest("(", "a", @")\1", depth), Text(depth))));
        }
    }

    (string Kind, string Open, string Body, string Close)[] classes =
    [
        ("class", "[", "a", "]"),
        ("negated class", "[^", "a", "]"),
        ("difference", "[a--", "[b]", "]"),
        ("intersection", "[a&&", "[a]", "]"),
        ("string alternatives", @"[\q{ab|c}", "a", "]"),
        ("class in a group", "(?:[", "a", "])"),
    ];
    foreach ((string kind, string open, string body, string close) in classes)
    {
        foreach (int depth in depths)
        {
            foreach (string flags in grammars)
            {
                all.Add(new($"deep {kind} x{depth} /{flags}", flags, () => (Nest(open, body, close, depth), Text(depth))));
            }
        }
    }

    foreach (int depth in depths)
    {
        all.Add(new($"deep [ unclosed x{depth} /v", "v", () => (new string('[', depth), Text(64))));
        all.Add(new($"deep ( unclosed x{depth} /v", "v", () => (new string('(', depth), Text(64))));
    }

    (string Kind, Func<int, string> Pattern)[] wide =
    [
        ("alternatives", w => Repeat("a|", w) + "b"),
        ("empty groups", w => Repeat("()", w)),
        ("groups", w => Repeat("(a)", w)),
        ("named groups", w => string.Concat(Enumerable.Range(0, w).Select(i => $"(?<g{i}>a)"))),
        ("classes", w => Repeat("[a]", w)),
        ("lookaheads", w => Repeat("(?=a)", w) + "a"),
        ("backreferences", w => "(a)" + Repeat(@"\1", w)),
        ("optional", w => Repeat("a?", w)),
        ("optional groups", w => Repeat("(?:a?)", w)),
        ("ranges in a class", w => "[" + Repeat("a-b", w) + "]"),
        ("properties", w => Repeat(@"\p{L}", w)),
        ("stars", w => Repeat("a*", w)),
        ("alternatives in a star", w => "(?:" + Repeat("a|", w) + "b)*"),
        ("differences", w => "[a" + Repeat("--b", w) + "]"),
        ("intersections", w => "[a" + Repeat("&&a", w) + "]"),
        ("string alternatives", w => @"[\q{" + Repeat("a|", w) + "b}]"),
    ];
    foreach (int width in new[] { 1000, 100_000, 300_000 })
    {
        foreach ((string kind, Func<int, string> pattern) in wide)
        {
            // A text as long as the pattern is wide, so that the groups can all take part, but for the two whose
            // match is empty or one character: over a long text their work is the pattern's width times the text's
            // length, by the matches the members ask for (split puts every group of every match in its answer), which
            // is the work any engine does and no measure of its stack.
            int length = kind is "empty groups" or "lookaheads" ? 64 : width;
            foreach (string flags in grammars)
            {
                all.Add(new($"wide {kind} x{width} /{flags}", flags, () => (pattern(width), Text(length))));
            }
        }
    }

    foreach (int length in new[] { 1 << 20, (1 << 20) + 1 })
    {
        foreach (string flags in grammars)
        {
            all.Add(new($"long literal x{length} /{flags}", flags, () => (new string('a', length), Text(length))));
        }
    }

    return all;
}

static void Child(int index, int stack)
{
    Probe probe = Probes()[index];
    string answer = "FAULT no answer";
    Thread thread = new(
        () =>
        {
            try
            {
                answer = Ask(probe);
            }
            catch (OutOfMemoryException)
            {
                answer = "FAULT out of memory";
            }
            catch (Exception e)
            {
                answer = "FAULT threw " + e.GetType().Name + ": " + e.Message;
            }
        },
        stack);
    thread.Start();
    thread.Join();
    Console.WriteLine(answer);
}

static string Ask(Probe probe)
{
    (string pattern, string text) = probe.Build();
    long compiling = Stopwatch.GetTimestamp();
    RegexFlags flags = Regex.ParseFlags(probe.Flags);
    bool valid = Regex.IsValid(pattern, flags);
    Regex re;
    try
    {
        re = new Regex(pattern, flags, TimeSpan.FromSeconds(5));
    }
    catch (RegexPatternTooLargeException)
    {
        return valid ? "FAULT IsValid accepted a pattern the constructor refused as too large" : "refused too large";
    }
    catch (RegexParseException)
    {
        return valid ? "FAULT IsValid accepted a pattern the constructor refused" : "refused not valid";
    }

    if (!valid)
    {
        return "FAULT IsValid refused a pattern the constructor accepted";
    }

    if (Environment.GetEnvironmentVariable("STACK_PROBE_TIMES") is not null)
    {
        Console.Error.WriteLine($"compiled: {Stopwatch.GetElapsedTime(compiling).TotalMilliseconds:F0} ms");
    }

    byte[] utf8 = Encoding.UTF8.GetBytes(text);
    ValueCapture[] captures = new ValueCapture[re.GroupCount + 1];
    List<string> answers = [];
    bool timed = Environment.GetEnvironmentVariable("STACK_PROBE_TIMES") is not null;
    int asked = 0;
    void Answer(Func<object> member)
    {
        long began = Stopwatch.GetTimestamp();
        try
        {
            answers.Add(member().ToString() ?? string.Empty);
            if (timed)
            {
                Console.Error.WriteLine($"member {asked}: {Stopwatch.GetElapsedTime(began).TotalMilliseconds:F0} ms, {GC.GetTotalMemory(false) >> 20} MB");
            }
        }
        catch (RegexMatchTimeoutException)
        {
            answers.Add("timeout");
        }

        asked++;
    }

    Answer(() => re.IsMatch(text));
    Answer(() => re.IsMatch(utf8.AsSpan()));
    Answer(() => re.Match(text).Groups.Count);
    Answer(() => re.Matches(text).Take(1000).Count());
    Answer(() => re.Count(text.AsSpan()));
    Answer(() => re.Count(utf8.AsSpan()));
    Answer(() => re.TryMatch(text.AsSpan(), 0, captures, out _));
    Answer(() => re.TryMatch(utf8.AsSpan(), 0, captures, out _));
    Answer(() =>
    {
        int n = 0;
        foreach (ValueMatch m in re.EnumerateMatches(text.AsSpan(), captures))
        {
            if (++n == 1000)
            {
                break;
            }
        }

        return n;
    });
    Answer(() =>
    {
        int n = 0;
        foreach (ValueMatch m in re.EnumerateMatches(utf8.AsSpan(), captures))
        {
            if (++n == 1000)
            {
                break;
            }
        }

        return n;
    });
    Answer(() => re.Replace(text, "$1x").Length);
    Answer(() =>
    {
        ArrayBufferWriter<char> written = new();
        re.Replace(text.AsSpan(), "$1x".AsSpan(), written);
        return written.WrittenCount;
    });
    Answer(() =>
    {
        ArrayBufferWriter<byte> written = new();
        re.Replace(utf8.AsSpan(), "$1x"u8, written);
        return written.WrittenCount;
    });
    Answer(() => re.Split(text).Length);
    Answer(() =>
    {
        int n = 0;
        foreach (ValueMatch r in re.EnumerateSplits(text.AsSpan()))
        {
            n++;
        }

        return n;
    });
    return "answered " + string.Join(' ', answers);
}

/// <summary>A probe, whose pattern and text are built only by the process that asks about it.</summary>
internal sealed record Probe(string Name, string Flags, Func<(string Pattern, string Text)> Build);
