// <copyright file="Industry.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// The pattern sets of dotnet/performance's Perf.Regex.Industry benchmarks (MIT; adapted in turn from
// mariomka/regex-benchmark, rust-lang/regex, rust-leipzig/regex-performance, the Boost regex documentation and
// BurntSushi/rebar), each counted over the same text .NET counts it over. A .NET pattern with an inline option
// ((?i), (?s), (?m)) is the same pattern with that flag here, since ECMA-262 has no inline options; every pattern
// is read with the u flag, under which \p{...} is a property escape as it is in .NET.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Net = System.Text.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Benchmarks;

/// <summary>One pattern of a set, with the text it is counted over and its flags.</summary>
/// <param name="Name">The set.</param>
/// <param name="Pattern">The pattern, in ECMA-262 syntax.</param>
/// <param name="Flags">The ECMA-262 flags beside u and g.</param>
/// <param name="Options">The .NET options that mean the same.</param>
/// <param name="File">The text's file.</param>
public sealed record Workload(string Name, string Pattern, string Flags, Net.RegexOptions Options, string File)
{
    /// <summary>The pattern as .NET reads it.</summary>
    public string NetPattern => this.Pattern;

    /// <inheritdoc/>
    public override string ToString() => this.Name + ": " + (this.Pattern.Length > 40 ? this.Pattern.Substring(0, 37) + "..." : this.Pattern) + (this.Flags.Length > 0 ? " /" + this.Flags : string.Empty);
}

/// <summary>The industry sets.</summary>
public static class Workloads
{
    /// <summary>mariomka/regex-benchmark: email, URI and IPv4 over its text.</summary>
    public static readonly Workload[] Mariomka =
    [
        new("mariomka", @"[\w\.+-]+@[\w\.-]+\.[\w\.-]+", "", Net.RegexOptions.None, "mariomka.txt.gz"),
        new("mariomka", @"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?", "", Net.RegexOptions.None, "mariomka.txt.gz"),
        new("mariomka", @"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", "", Net.RegexOptions.None, "mariomka.txt.gz"),
    ];

    /// <summary>rust-lang/regex's Sherlock set.</summary>
    public static readonly Workload[] Sherlock =
    [
        S(@"Sherlock"),
        S(@"Holmes"),
        S(@"Sherlock Holmes"),
        S(@"Sherlock", "i"),
        S(@"Holmes", "i"),
        S(@"Sherlock Holmes", "i"),
        S(@"Sherlock\s+Holmes"),
        S(@"Sherlock|Street"),
        S(@"Sherlock|Holmes"),
        S(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker"),
        S(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker", "i"),
        S(@"Sher[a-z]+|Hol[a-z]+"),
        S(@"Sher[a-z]+|Hol[a-z]+", "i"),
        S(@"Sherlock|Holmes|Watson"),
        S(@"Sherlock|Holmes|Watson", "i"),
        S(@"zqj"),
        S(@"aqj"),
        S(@"aei"),
        S(@"the"),
        S(@"The"),
        S(@"the", "i"),
        S(@"the\s+\w+"),
        S(@".*"),
        S(@"[^\n]*"),
        S(@".*", "s"),
        S(@"\p{L}"),
        S(@"\p{Lu}"),
        S(@"\p{Ll}"),
        S(@"\w+"),
        S(@"\w+\s+Holmes"),
        S(@"\w+\s+Holmes\s+\w+"),
        S(@"Holmes.{0,25}Watson|Watson.{0,25}Holmes"),
        S(@"^Sherlock Holmes|Sherlock Holmes$", "m"),
        S(@"\b\w+n\b"),
        S(@"[a-q][^u-z]{13}x"),
        S(@"[a-zA-Z]+ing"),
        S(@"\s[a-zA-Z]{0,12}ing\s"),
    ];

    /// <summary>rust-leipzig/regex-performance over Twain's text.</summary>
    public static readonly Workload[] Leipzig =
    [
        L("Twain"),
        L("Twain", "i"),
        L("[a-z]shing"),
        L("Huck[a-zA-Z]+|Saw[a-zA-Z]+"),
        L("Tom|Sawyer|Huckleberry|Finn"),
        L("Tom|Sawyer|Huckleberry|Finn", "i"),
        L(".{0,2}(Tom|Sawyer|Huckleberry|Finn)"),
        L(".{2,4}(Tom|Sawyer|Huckleberry|Finn)"),
        L("Tom.{10,25}river|river.{10,25}Tom"),
        L(@"([A-Za-z]awyer|[A-Za-z]inn)\s"),
        L(@"\p{Sm}"),
    ];

    /// <summary>BurntSushi/rebar's literal and alternation searches over Russian and Chinese subtitles.</summary>
    public static readonly Workload[] Rebar =
    [
        new("rebar-ru", "Шерлок Холмс", "", Net.RegexOptions.None, "opensubtitles-ru.txt.gz"),
        new("rebar-ru", "Шерлок Холмс", "i", Net.RegexOptions.IgnoreCase, "opensubtitles-ru.txt.gz"),
        new("rebar-ru", "Шерлок Холмс|Джон Уотсон|Ирен Адлер|инспектор Лестрейд|профессор Мориарти", "", Net.RegexOptions.None, "opensubtitles-ru.txt.gz"),
        new("rebar-ru", "Шерлок Холмс|Джон Уотсон|Ирен Адлер|инспектор Лестрейд|профессор Мориарти", "i", Net.RegexOptions.IgnoreCase, "opensubtitles-ru.txt.gz"),
        new("rebar-zh", "夏洛克·福尔摩斯", "", Net.RegexOptions.None, "opensubtitles-zh.txt.gz"),
        new("rebar-zh", "夏洛克·福尔摩斯|约翰华生|阿德勒|雷斯垂德|莫里亚蒂教授", "", Net.RegexOptions.None, "opensubtitles-zh.txt.gz"),
    ];

    /// <summary>Every workload that counts matches over a text.</summary>
    public static IEnumerable<Workload> All => Mariomka.Concat(Sherlock).Concat(Leipzig).Concat(Rebar);

    private static Workload S(string pattern, string flags = "") => new("sherlock", pattern, flags, ToOptions(flags), "sherlock.txt.gz");

    private static Workload L(string pattern, string flags = "") => new("leipzig", pattern, flags, ToOptions(flags), "3200.txt.gz");

    private static Net.RegexOptions ToOptions(string flags)
    {
        Net.RegexOptions o = Net.RegexOptions.None;
        if (flags.Contains('i'))
        {
            o |= Net.RegexOptions.IgnoreCase;
        }

        if (flags.Contains('s'))
        {
            o |= Net.RegexOptions.Singleline;
        }

        if (flags.Contains('m'))
        {
            o |= Net.RegexOptions.Multiline;
        }

        return o;
    }
}

/// <summary>
/// Counts the matches of each industry pattern over its text: .NET's interpreter, .NET compiled, .NET
/// NonBacktracking where the pattern allows it, and this library over UTF-16 and over UTF-8.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class IndustryCount
{
    private Net.Regex net = null!;
    private Net.Regex netCompiled = null!;
    private Net.Regex? netNonBacktracking;
    private Regex corvus = null!;
    private string text = null!;
    private byte[] utf8 = null!;

    /// <summary>Gets or sets the workload.</summary>
    [ParamsSource(nameof(Workloads))]
    public Workload Workload { get; set; } = null!;

    /// <summary>Gets the workloads.</summary>
    public IEnumerable<Workload> Workloads => RegularExpressions.Benchmarks.Workloads.All;

    /// <summary>Compiles the patterns and reads the text.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.net = new Net.Regex(this.Workload.NetPattern, this.Workload.Options);
        this.netCompiled = new Net.Regex(this.Workload.NetPattern, this.Workload.Options | Net.RegexOptions.Compiled);
        try
        {
            this.netNonBacktracking = new Net.Regex(this.Workload.NetPattern, this.Workload.Options | Net.RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            this.netNonBacktracking = null;
        }

        this.corvus = new Regex(this.Workload.Pattern, this.Workload.Flags + "gu");
        this.text = Inputs.Text(this.Workload.File);
        this.utf8 = Inputs.Utf8(this.Workload.File);
        int expected = this.net.Count(this.text);
        int ours = this.corvus.Count(this.text);
        int ours8 = this.corvus.Count(this.utf8);
        if (ours != expected || ours8 != expected)
        {
            throw new InvalidOperationException($"{this.Workload}: .NET counts {expected}, this library {ours} (UTF-16) and {ours8} (UTF-8)");
        }
    }

    /// <summary>.NET's interpreter.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Baseline = true, Description = ".NET interpreter")]
    public int NetInterpreter() => this.net.Count(this.text);

    /// <summary>.NET compiled to IL.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = ".NET Compiled")]
    public int NetCompiled() => this.netCompiled.Count(this.text);

    /// <summary>.NET's NonBacktracking engine, or -1 when the pattern is beyond it.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = ".NET NonBacktracking")]
    public int NetNonBacktracking() => this.netNonBacktracking is null ? -1 : this.netNonBacktracking.Count(this.text);

    /// <summary>This library over the string.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = "Corvus UTF-16")]
    public int CorvusUtf16() => this.corvus.Count(this.text);

    /// <summary>This library over the UTF-8 bytes.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = "Corvus UTF-8")]
    public int CorvusUtf8() => this.corvus.Count(this.utf8);
}

/// <summary>The Boost documentation's simple IsMatch set: short texts, anchored patterns.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class BoostIsMatch
{
    private static readonly (string Input, string Pattern)[] Cases =
    [
        (@"abc", @"abc"),
        (@"100- this is a line of ftp response which contains a message string", @"^([0-9]+)(\-| |$)(.*)$"),
        (@"1234-5678-1234-456", @"(\d{4}[- ]){3}\d{3,4}"),
        (@"john@johnmaddock.co.uk", @"^([a-zA-Z0-9_\-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([a-zA-Z0-9\-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$"),
        (@"foo12@foo.edu", @"^([a-zA-Z0-9_\-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([a-zA-Z0-9\-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$"),
        (@"bob.smith@foo.tv", @"^([a-zA-Z0-9_\-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([a-zA-Z0-9\-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$"),
        (@"EH10 2QQ", @"^[a-zA-Z]{1,2}[0-9][0-9A-Za-z]{0,1} {0,1}[0-9][A-Za-z]{2}$"),
        (@"G1 1AA", @"^[a-zA-Z]{1,2}[0-9][0-9A-Za-z]{0,1} {0,1}[0-9][A-Za-z]{2}$"),
        (@"SW1 1ZZ", @"^[a-zA-Z]{1,2}[0-9][0-9A-Za-z]{0,1} {0,1}[0-9][A-Za-z]{2}$"),
        (@"4/1/2001", @"^\d{1,2}/\d{1,2}/\d{4}$"),
        (@"12/12/2001", @"^\d{1,2}/\d{1,2}/\d{4}$"),
        (@"123", @"^[-+]?\d*\.?\d*$"),
        (@"+3.14159", @"^[-+]?\d*\.?\d*$"),
        (@"-3.14159", @"^[-+]?\d*\.?\d*$"),
    ];

    private Net.Regex net = null!;
    private Net.Regex netCompiled = null!;
    private Net.Regex? netNonBacktracking;
    private Regex corvus = null!;
    private string input = null!;
    private byte[] utf8 = null!;

    /// <summary>Gets or sets the case.</summary>
    [Params(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)]
    public int Id { get; set; }

    /// <summary>Compiles the patterns.</summary>
    [GlobalSetup]
    public void Setup()
    {
        (this.input, string pattern) = Cases[this.Id];
        this.net = new Net.Regex(pattern);
        this.netCompiled = new Net.Regex(pattern, Net.RegexOptions.Compiled);
        try
        {
            this.netNonBacktracking = new Net.Regex(pattern, Net.RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            this.netNonBacktracking = null;
        }

        this.corvus = new Regex(pattern, RegexFlags.Unicode);
        this.utf8 = System.Text.Encoding.UTF8.GetBytes(this.input);
        if (this.net.IsMatch(this.input) != this.corvus.IsMatch(this.input) || this.corvus.IsMatch(this.utf8) != this.net.IsMatch(this.input))
        {
            throw new InvalidOperationException("The answers differ for case " + this.Id);
        }
    }

    /// <summary>.NET's interpreter.</summary>
    /// <returns>The answer.</returns>
    [Benchmark(Baseline = true, Description = ".NET interpreter")]
    public bool NetInterpreter() => this.net.IsMatch(this.input);

    /// <summary>.NET compiled to IL.</summary>
    /// <returns>The answer.</returns>
    [Benchmark(Description = ".NET Compiled")]
    public bool NetCompiled() => this.netCompiled.IsMatch(this.input);

    /// <summary>.NET's NonBacktracking engine.</summary>
    /// <returns>The answer.</returns>
    [Benchmark(Description = ".NET NonBacktracking")]
    public bool NetNonBacktracking() => this.netNonBacktracking is not null && this.netNonBacktracking.IsMatch(this.input);

    /// <summary>This library over the string.</summary>
    /// <returns>The answer.</returns>
    [Benchmark(Description = "Corvus UTF-16")]
    public bool CorvusUtf16() => this.corvus.IsMatch(this.input);

    /// <summary>This library over the UTF-8 bytes.</summary>
    /// <returns>The answer.</returns>
    [Benchmark(Description = "Corvus UTF-8")]
    public bool CorvusUtf8() => this.corvus.IsMatch(this.utf8);
}
