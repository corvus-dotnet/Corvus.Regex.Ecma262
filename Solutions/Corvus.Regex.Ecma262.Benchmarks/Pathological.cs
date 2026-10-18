// <copyright file="Pathological.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Net = System.Text.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Benchmarks;

/// <summary>
/// Patterns that make a backtracking matcher take time that doubles with each character of the text, on texts made
/// for them. .NET's backtracking engines run until their timeout (set to one second here) and throw; the memory
/// of failed places lets this library finish. A result of -1 is a timeout.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public class Pathological
{
    private static readonly (string Pattern, string Text)[] Cases =
    [
        (@"^(a|aa)+$", new string('a', 30) + "b"),
        (@"^(a+)+$", new string('a', 30) + "b"),
        (@"^(?:a*)*b$", new string('a', 30)),
        (@"^(?:(?:(?:a*)*)*)*b$", new string('a', 24)),
        (@"^(x+x+)+y$", new string('x', 24)),
        (@"^(?:\d+|\w+)+!$", new string('1', 26)),
        (@"^(?:[a-z]+\d)+$", "abc1" + new string('a', 24)),
    ];

    private Net.Regex net = null!;
    private Net.Regex netCompiled = null!;
    private Regex corvus = null!;
    private string text = null!;
    private byte[] utf8 = null!;

    /// <summary>Gets or sets the case.</summary>
    [Params(0, 1, 2, 3, 4, 5, 6)]
    public int Id { get; set; }

    /// <summary>Compiles the patterns.</summary>
    [GlobalSetup]
    public void Setup()
    {
        (string pattern, this.text) = Cases[this.Id];
        this.utf8 = System.Text.Encoding.UTF8.GetBytes(this.text);
        this.net = new Net.Regex(pattern, Net.RegexOptions.None, TimeSpan.FromSeconds(1));
        this.netCompiled = new Net.Regex(pattern, Net.RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        this.corvus = new Regex(pattern, RegexFlags.Unicode, TimeSpan.FromSeconds(10));
    }

    /// <summary>.NET's interpreter.</summary>
    /// <returns>1, 0, or -1 for a timeout.</returns>
    [Benchmark(Baseline = true, Description = ".NET interpreter")]
    public int NetInterpreter() => Timed(this.net, this.text);

    /// <summary>.NET compiled to IL.</summary>
    /// <returns>1, 0, or -1 for a timeout.</returns>
    [Benchmark(Description = ".NET Compiled")]
    public int NetCompiled() => Timed(this.netCompiled, this.text);

    /// <summary>This library over the string.</summary>
    /// <returns>1 or 0.</returns>
    [Benchmark(Description = "Corvus UTF-16")]
    public int CorvusUtf16() => this.corvus.IsMatch(this.text) ? 1 : 0;

    /// <summary>This library over the UTF-8 bytes.</summary>
    /// <returns>1 or 0.</returns>
    [Benchmark(Description = "Corvus UTF-8")]
    public int CorvusUtf8() => this.corvus.IsMatch(this.utf8) ? 1 : 0;

    private static int Timed(Net.Regex r, string text)
    {
        try
        {
            return r.IsMatch(text) ? 1 : 0;
        }
        catch (Net.RegexMatchTimeoutException)
        {
            return -1;
        }
    }
}
