// <copyright file="Common.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// dotnet/performance's Perf.Regex.Common (MIT): the members of Regex on a few patterns and a Lorem ipsum text.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Net = System.Text.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Benchmarks;

/// <summary>Each member on its pattern, .NET's interpreter and compiled engine against this library.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CommonMembers
{
    private const string EmailPattern = @"^([a-zA-Z0-9_\-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([a-zA-Z0-9\-]+\.)+))([a-zA-Z]{2,12}|[0-9]{1,3})(\]?)$";
    private const string DatePattern = @"\b\d{1,2}\/\d{1,2}\/\d{2,4}\b";
    private const string IpPattern = @"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])";
    private const string UriPattern = @"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?";
    private const string WordsPattern = @"tempus|magna|semper";
    private const string SetPattern = @"\w{10,}";
    private const string BoundaryPattern = @"\b\w{10,}\b";
    private const string WarningPattern = @"(^(.*)(\(([0-9]+),([0-9]+)\)): )(error|warning) ([A-Z]+[0-9]+) ?: (.*)";

    private static readonly string LoremIpsum =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer posuere eget magna a consectetur. Integer a libero volutpat, tempus lectus ac, aliquam enim. Etiam a ipsum nec mi vestibulum scelerisque. " +
        "Ut orci felis, efficitur molestie posuere id, eleifend vel urna. Duis lectus velit, iaculis nec ex non, consectetur blandit ex. Vestibulum nec mi suscipit, tempor purus at, convallis ligula. Sed auctor lobortis " +
        "porta. Donec nec quam non elit aliquet tristique sit amet id tortor. Nullam vitae cursus metus, vitae pulvinar nisl. Mauris dui nisi, lobortis eget placerat vel, tempus eu mauris. Suspendisse pretium egestas urna " +
        "vitae molestie.Mauris id odio mollis, sollicitudin odio in, tincidunt libero. Duis dolor nunc, placerat eu tincidunt eu, vehicula a arcu.Curabitur vitae eros libero. Nullam in eros enim. Praesent odio lorem, fringilla " +
        "ut eros id, tristique semper ipsum. Nunc accumsan magna nulla, sit amet pellentesque neque eleifend tempus. Pellentesque a fermentum nisi. Curabitur non facilisis diam. Pellentesque habitant morbi tristique senectus et " +
        "netus et malesuada fames ac turpis egestas. Curabitur finibus enim leo, vel luctus lacus volutpat sit amet. Suspendisse potenti. Curabitur nec ex lobortis, tincidunt nisl in, semper lacus. Maecenas bibendum suscipit elit, " +
        "et convallis augue pharetra eu.Donec vitae vestibulum diam. Suspendisse cursus vel augue quis elementum. Ut tempus quam purus. Orci varius natoque penatibus et magnis dis parturient montes, nascetur ridiculus mus.Proin lacus " +
        "diam, maximus a vulputate at, vehicula sit amet ipsum. Morbi lobortis libero dui, eu volutpat felis pellentesque in. Praesent sit amet venenatis nisl, quis semper lorem.Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
        "Etiam tempor lectus vitae est fermentum, et molestie leo egestas. Sed vitae magna nulla. Proin a aliquet mauris, id fringilla lorem.Fusce pellentesque, sapien et imperdiet scelerisque, sapien orci facilisis elit, sit " +
        "amet tristique nisi neque vitae mi. Maecenas sit amet imperdiet elit, euismod maximus est. Donec maximus, ex non efficitur bibendum, elit mi porta orci, vel commodo ipsum orci eu nisl. Integer maximus urna ac finibus blandit. " +
        "In hac habitasse platea dictumst.Suspendisse augue libero, lacinia eu lobortis venenatis, pretium eget mi.Cras eget feugiat est, in venenatis lectus. Sed luctus, sapien cursus semper ullamcorper, urna sapien consequat orci, " +
        "vitae congue mauris enim tempus neque. Sed in sagittis lacus. Nullam sodales interdum enim, venenatis sodales velit vehicula quis.Aliquam congue eu ex facilisis vestibulum. Nunc congue justo nulla, sit amet imperdiet mi " +
        "sodales in. Integer quis magna a sem euismod mollis ut ac eros. Vestibulum tincidunt scelerisque lacus. Ut ornare diam et purus gravida, quis aliquam dolor ultrices. Nulla elit arcu, eleifend id erat quis, tempus ultricies " +
        "lorem.Etiam suscipit magna vel nunc malesuada, nec faucibus nisi hendrerit. Nam sollicitudin, nisi at sodales egestas, urna odio blandit leo, non tristique lectus nunc ut libero. Aliquam pulvinar nulla vitae nibh venenatis, " +
        "sed euismod ligula egestas. Pellentesque malesuada congue sapien sit amet venenatis.Nulla blandit mi sit amet laoreet consectetur.Aliquam sodales non turpis ut suscipit. Morbi nec pretium risus, imperdiet accumsan nunc.Sed at " +
        "arcu augue. Curabitur dapibus aliquet felis et blandit. Maecenas.";

    private static readonly byte[] LoremIpsumUtf8 = System.Text.Encoding.UTF8.GetBytes(LoremIpsum);

    private Net.Regex netEmail = null!, netDate = null!, netIp = null!, netUri = null!, netWords = null!, netSet = null!, netBoundary = null!;
    private Regex email = null!, date = null!, ip = null!, uri = null!, words = null!, set = null!, boundary = null!, wordsGlobal = null!;

    /// <summary>Gets or sets a value indicating whether .NET compiles its patterns to IL.</summary>
    [Params(false, true)]
    public bool Compiled { get; set; }

    /// <summary>Compiles the patterns.</summary>
    [GlobalSetup]
    public void Setup()
    {
        Net.RegexOptions o = this.Compiled ? Net.RegexOptions.Compiled : Net.RegexOptions.None;
        this.netEmail = new(EmailPattern, o);
        this.netDate = new(DatePattern, o);
        this.netIp = new(IpPattern, o);
        this.netUri = new(UriPattern, o);
        this.netWords = new(WordsPattern, o);
        this.netSet = new(SetPattern, o);
        this.netBoundary = new(BoundaryPattern, o);
        this.email = new(EmailPattern, RegexFlags.Unicode);
        this.date = new(DatePattern, RegexFlags.Unicode);
        this.ip = new(IpPattern, RegexFlags.Unicode);
        this.uri = new(UriPattern, RegexFlags.Unicode);
        this.words = new(WordsPattern, RegexFlags.Unicode);
        this.wordsGlobal = new(WordsPattern, RegexFlags.Unicode | RegexFlags.Global);
        this.set = new(SetPattern, RegexFlags.Unicode);
        this.boundary = new(BoundaryPattern, RegexFlags.Unicode);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Email_IsMatch")]
    public bool Net_Email_IsMatch() => this.netEmail.IsMatch("yay.performance@dot.net");

    [Benchmark, BenchmarkCategory("Email_IsMatch")]
    public bool Corvus_Email_IsMatch() => this.email.IsMatch("yay.performance@dot.net");

    [Benchmark, BenchmarkCategory("Email_IsMatch")]
    public bool Corvus_Email_IsMatch_Utf8() => this.email.IsMatchUnchecked("yay.performance@dot.net"u8);

    [Benchmark(Baseline = true), BenchmarkCategory("Email_IsNotMatch")]
    public bool Net_Email_IsNotMatch() => this.netEmail.IsMatch("yay.performance@dot.net#");

    [Benchmark, BenchmarkCategory("Email_IsNotMatch")]
    public bool Corvus_Email_IsNotMatch() => this.email.IsMatch("yay.performance@dot.net#");

    [Benchmark(Baseline = true), BenchmarkCategory("Date_IsMatch")]
    public bool Net_Date_IsMatch() => this.netDate.IsMatch("Today is 11/18/2019");

    [Benchmark, BenchmarkCategory("Date_IsMatch")]
    public bool Corvus_Date_IsMatch() => this.date.IsMatch("Today is 11/18/2019");

    [Benchmark(Baseline = true), BenchmarkCategory("IP_IsMatch")]
    public bool Net_IP_IsMatch() => this.netIp.IsMatch("012.345.678.910");

    [Benchmark, BenchmarkCategory("IP_IsMatch")]
    public bool Corvus_IP_IsMatch() => this.ip.IsMatch("012.345.678.910");

    [Benchmark(Baseline = true), BenchmarkCategory("Uri_IsMatch")]
    public bool Net_Uri_IsMatch() => this.netUri.IsMatch("http://example.org");

    [Benchmark, BenchmarkCategory("Uri_IsMatch")]
    public bool Corvus_Uri_IsMatch() => this.uri.IsMatch("http://example.org");

    [Benchmark(Baseline = true), BenchmarkCategory("MatchesSet")]
    public int Net_MatchesSet() => this.netSet.Matches(LoremIpsum).Count;

    [Benchmark, BenchmarkCategory("MatchesSet")]
    public int Corvus_MatchesSet() => this.set.Count(LoremIpsum);

    [Benchmark, BenchmarkCategory("MatchesSet")]
    public int Corvus_MatchesSet_Utf8() => this.set.CountUnchecked(LoremIpsumUtf8);

    [Benchmark(Baseline = true), BenchmarkCategory("MatchesBoundary")]
    public int Net_MatchesBoundary() => this.netBoundary.Matches(LoremIpsum).Count;

    [Benchmark, BenchmarkCategory("MatchesBoundary")]
    public int Corvus_MatchesBoundary() => this.boundary.Count(LoremIpsum);

    [Benchmark(Baseline = true), BenchmarkCategory("MatchesWords")]
    public int Net_MatchesWords() => this.netWords.Matches(LoremIpsum).Count;

    [Benchmark, BenchmarkCategory("MatchesWords")]
    public int Corvus_MatchesWords() => this.words.Count(LoremIpsum);

    [Benchmark(Baseline = true), BenchmarkCategory("MatchWord")]
    public Net.Match Net_MatchWord() => this.netWords.Match(LoremIpsum);

    [Benchmark, BenchmarkCategory("MatchWord")]
    public Match Corvus_MatchWord() => this.words.Match(LoremIpsum);

    [Benchmark(Baseline = true), BenchmarkCategory("ReplaceWords")]
    public string Net_ReplaceWords() => this.netWords.Replace(LoremIpsum, "amoveatur");

    [Benchmark, BenchmarkCategory("ReplaceWords")]
    public string Corvus_ReplaceWords() => this.wordsGlobal.Replace(LoremIpsum, "amoveatur");

    [Benchmark(Baseline = true), BenchmarkCategory("SplitWords")]
    public string[] Net_SplitWords() => this.netWords.Split(LoremIpsum);

    [Benchmark, BenchmarkCategory("SplitWords")]
    public string?[] Corvus_SplitWords() => this.words.Split(LoremIpsum);

    [Benchmark(Baseline = true), BenchmarkCategory("Ctor")]
    public Net.Regex Net_Ctor() => new(WarningPattern, this.Compiled ? Net.RegexOptions.Compiled : Net.RegexOptions.None);

    [Benchmark, BenchmarkCategory("Ctor")]
    public Regex Corvus_Ctor() => new(WarningPattern, RegexFlags.Unicode);
}
