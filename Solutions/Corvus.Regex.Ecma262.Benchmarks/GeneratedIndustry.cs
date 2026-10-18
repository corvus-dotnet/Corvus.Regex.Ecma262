// <copyright file="GeneratedIndustry.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Written by tools/benchmarks/gen-generated-industry.py from the workloads of Industry.cs: the same patterns as
// generated regular expressions, this library's and .NET's.

using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Benchmarks;

/// <summary>The industry workloads as generated regular expressions: this library's source generator against .NET's, counting the matches over a text.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByParams)]
public partial class GeneratedCount
{
    private static readonly Regex[] CorvusAll =
    [
        Corvus_0(),
        Corvus_1(),
        Corvus_2(),
        Corvus_3(),
        Corvus_4(),
        Corvus_5(),
        Corvus_6(),
        Corvus_7(),
        Corvus_8(),
        Corvus_9(),
        Corvus_10(),
        Corvus_11(),
        Corvus_12(),
        Corvus_13(),
        Corvus_14(),
        Corvus_15(),
        Corvus_16(),
        Corvus_17(),
        Corvus_18(),
        Corvus_19(),
        Corvus_20(),
        Corvus_21(),
        Corvus_22(),
        Corvus_23(),
        Corvus_24(),
        Corvus_25(),
        Corvus_26(),
        Corvus_27(),
        Corvus_28(),
        Corvus_29(),
        Corvus_30(),
        Corvus_31(),
        Corvus_32(),
        Corvus_33(),
        Corvus_34(),
        Corvus_35(),
        Corvus_36(),
        Corvus_37(),
        Corvus_38(),
        Corvus_39(),
        Corvus_40(),
        Corvus_41(),
        Corvus_42(),
        Corvus_43(),
        Corvus_44(),
        Corvus_45(),
        Corvus_46(),
        Corvus_47(),
        Corvus_48(),
        Corvus_49(),
        Corvus_50(),
        Corvus_51(),
        Corvus_52(),
        Corvus_53(),
        Corvus_54(),
        Corvus_55(),
        Corvus_56(),
    ];

    private static readonly global::System.Text.RegularExpressions.Regex[] NetAll =
    [
        Net_0(),
        Net_1(),
        Net_2(),
        Net_3(),
        Net_4(),
        Net_5(),
        Net_6(),
        Net_7(),
        Net_8(),
        Net_9(),
        Net_10(),
        Net_11(),
        Net_12(),
        Net_13(),
        Net_14(),
        Net_15(),
        Net_16(),
        Net_17(),
        Net_18(),
        Net_19(),
        Net_20(),
        Net_21(),
        Net_22(),
        Net_23(),
        Net_24(),
        Net_25(),
        Net_26(),
        Net_27(),
        Net_28(),
        Net_29(),
        Net_30(),
        Net_31(),
        Net_32(),
        Net_33(),
        Net_34(),
        Net_35(),
        Net_36(),
        Net_37(),
        Net_38(),
        Net_39(),
        Net_40(),
        Net_41(),
        Net_42(),
        Net_43(),
        Net_44(),
        Net_45(),
        Net_46(),
        Net_47(),
        Net_48(),
        Net_49(),
        Net_50(),
        Net_51(),
        Net_52(),
        Net_53(),
        Net_54(),
        Net_55(),
        Net_56(),
    ];

    private global::System.Text.RegularExpressions.Regex netGenerated = null!;
    private global::System.Text.RegularExpressions.Regex netInterpreter = null!;
    private Regex corvusGenerated = null!;
    private Regex corvusInterpreter = null!;
    private string text = null!;
    private byte[] utf8 = null!;

    /// <summary>Gets or sets the workload.</summary>
    [ParamsSource(nameof(Workloads))]
    public Workload Workload { get; set; } = null!;

    /// <summary>Gets the workloads.</summary>
    public IEnumerable<Workload> Workloads => RegularExpressions.Benchmarks.Workloads.Selected;

    /// <summary>Takes the generated regular expressions of the workload and reads the text.</summary>
    [GlobalSetup]
    public void Setup()
    {
        int k = Array.IndexOf(RegularExpressions.Benchmarks.Workloads.All.ToArray(), this.Workload);
        this.netGenerated = NetAll[k];
        this.corvusGenerated = CorvusAll[k];
        this.netInterpreter = new global::System.Text.RegularExpressions.Regex(this.Workload.NetPattern, this.Workload.Options);
        this.corvusInterpreter = new Regex(this.Workload.Pattern, this.Workload.Flags + "gu");
        this.text = Inputs.Text(this.Workload.File);
        this.utf8 = Inputs.Utf8(this.Workload.File);
        if (this.corvusGenerated.Count(this.text) != this.corvusInterpreter.Count(this.text) || this.corvusGenerated.Count(this.utf8) != this.corvusInterpreter.Count(this.utf8))
        {
            throw new InvalidOperationException($"{this.Workload}: the generated code and the interpreter count differently");
        }
    }

    /// <summary>.NET's interpreter.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Baseline = true, Description = ".NET interpreter")]
    public int NetInterpreter() => this.netInterpreter.Count(this.text);

    /// <summary>.NET's source generator.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = ".NET generated")]
    public int NetGenerated() => this.netGenerated.Count(this.text);

    /// <summary>This library's interpreter over the string.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = "Corvus UTF-16")]
    public int CorvusInterpreter() => this.corvusInterpreter.Count(this.text);

    /// <summary>This library's generated code over the string.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = "Corvus generated UTF-16")]
    public int CorvusGenerated() => this.corvusGenerated.Count(this.text);

    /// <summary>This library's generated code over the UTF-8 bytes.</summary>
    /// <returns>The count.</returns>
    [Benchmark(Description = "Corvus generated UTF-8")]
    public int CorvusGeneratedUtf8() => this.corvusGenerated.Count(this.utf8);


    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"[\w\.+-]+@[\w\.-]+\.[\w\.-]+", "gu")]
    private static partial Regex Corvus_0();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"[\w\.+-]+@[\w\.-]+\.[\w\.-]+", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_0();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?", "gu")]
    private static partial Regex Corvus_1();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_1();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", "gu")]
    private static partial Regex Corvus_2();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_2();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock", "gu")]
    private static partial Regex Corvus_3();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_3();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Holmes", "gu")]
    private static partial Regex Corvus_4();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Holmes", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_4();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock Holmes", "gu")]
    private static partial Regex Corvus_5();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock Holmes", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_5();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock", "igu")]
    private static partial Regex Corvus_6();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_6();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Holmes", "igu")]
    private static partial Regex Corvus_7();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Holmes", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_7();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock Holmes", "igu")]
    private static partial Regex Corvus_8();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock Holmes", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_8();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock\s+Holmes", "gu")]
    private static partial Regex Corvus_9();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock\s+Holmes", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_9();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock|Street", "gu")]
    private static partial Regex Corvus_10();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock|Street", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_10();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes", "gu")]
    private static partial Regex Corvus_11();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_11();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker", "gu")]
    private static partial Regex Corvus_12();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_12();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker", "igu")]
    private static partial Regex Corvus_13();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson|Irene|Adler|John|Baker", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_13();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sher[a-z]+|Hol[a-z]+", "gu")]
    private static partial Regex Corvus_14();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sher[a-z]+|Hol[a-z]+", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_14();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sher[a-z]+|Hol[a-z]+", "igu")]
    private static partial Regex Corvus_15();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sher[a-z]+|Hol[a-z]+", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_15();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson", "gu")]
    private static partial Regex Corvus_16();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_16();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson", "igu")]
    private static partial Regex Corvus_17();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Sherlock|Holmes|Watson", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_17();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"zqj", "gu")]
    private static partial Regex Corvus_18();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"zqj", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_18();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"aqj", "gu")]
    private static partial Regex Corvus_19();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"aqj", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_19();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"aei", "gu")]
    private static partial Regex Corvus_20();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"aei", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_20();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"the", "gu")]
    private static partial Regex Corvus_21();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"the", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_21();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"The", "gu")]
    private static partial Regex Corvus_22();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"The", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_22();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"the", "igu")]
    private static partial Regex Corvus_23();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"the", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_23();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"the\s+\w+", "gu")]
    private static partial Regex Corvus_24();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"the\s+\w+", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_24();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@".*", "gu")]
    private static partial Regex Corvus_25();

    [global::System.Text.RegularExpressions.GeneratedRegex(@".*", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_25();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"[^\n]*", "gu")]
    private static partial Regex Corvus_26();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"[^\n]*", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_26();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@".*", "sgu")]
    private static partial Regex Corvus_27();

    [global::System.Text.RegularExpressions.GeneratedRegex(@".*", global::System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial global::System.Text.RegularExpressions.Regex Net_27();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\p{L}", "gu")]
    private static partial Regex Corvus_28();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\p{L}", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_28();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\p{Lu}", "gu")]
    private static partial Regex Corvus_29();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\p{Lu}", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_29();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\p{Ll}", "gu")]
    private static partial Regex Corvus_30();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\p{Ll}", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_30();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\w+", "gu")]
    private static partial Regex Corvus_31();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\w+", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_31();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\w+\s+Holmes", "gu")]
    private static partial Regex Corvus_32();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\w+\s+Holmes", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_32();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\w+\s+Holmes\s+\w+", "gu")]
    private static partial Regex Corvus_33();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\w+\s+Holmes\s+\w+", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_33();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"Holmes.{0,25}Watson|Watson.{0,25}Holmes", "gu")]
    private static partial Regex Corvus_34();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"Holmes.{0,25}Watson|Watson.{0,25}Holmes", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_34();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"^Sherlock Holmes|Sherlock Holmes$", "mgu")]
    private static partial Regex Corvus_35();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"^Sherlock Holmes|Sherlock Holmes$", global::System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial global::System.Text.RegularExpressions.Regex Net_35();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\b\w+n\b", "gu")]
    private static partial Regex Corvus_36();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\b\w+n\b", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_36();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"[a-q][^u-z]{13}x", "gu")]
    private static partial Regex Corvus_37();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"[a-q][^u-z]{13}x", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_37();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"[a-zA-Z]+ing", "gu")]
    private static partial Regex Corvus_38();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"[a-zA-Z]+ing", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_38();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\s[a-zA-Z]{0,12}ing\s", "gu")]
    private static partial Regex Corvus_39();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\s[a-zA-Z]{0,12}ing\s", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_39();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Twain", "gu")]
    private static partial Regex Corvus_40();

    [global::System.Text.RegularExpressions.GeneratedRegex("Twain", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_40();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Twain", "igu")]
    private static partial Regex Corvus_41();

    [global::System.Text.RegularExpressions.GeneratedRegex("Twain", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_41();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("[a-z]shing", "gu")]
    private static partial Regex Corvus_42();

    [global::System.Text.RegularExpressions.GeneratedRegex("[a-z]shing", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_42();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Huck[a-zA-Z]+|Saw[a-zA-Z]+", "gu")]
    private static partial Regex Corvus_43();

    [global::System.Text.RegularExpressions.GeneratedRegex("Huck[a-zA-Z]+|Saw[a-zA-Z]+", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_43();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Tom|Sawyer|Huckleberry|Finn", "gu")]
    private static partial Regex Corvus_44();

    [global::System.Text.RegularExpressions.GeneratedRegex("Tom|Sawyer|Huckleberry|Finn", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_44();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Tom|Sawyer|Huckleberry|Finn", "igu")]
    private static partial Regex Corvus_45();

    [global::System.Text.RegularExpressions.GeneratedRegex("Tom|Sawyer|Huckleberry|Finn", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_45();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(".{0,2}(Tom|Sawyer|Huckleberry|Finn)", "gu")]
    private static partial Regex Corvus_46();

    [global::System.Text.RegularExpressions.GeneratedRegex(".{0,2}(Tom|Sawyer|Huckleberry|Finn)", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_46();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(".{2,4}(Tom|Sawyer|Huckleberry|Finn)", "gu")]
    private static partial Regex Corvus_47();

    [global::System.Text.RegularExpressions.GeneratedRegex(".{2,4}(Tom|Sawyer|Huckleberry|Finn)", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_47();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Tom.{10,25}river|river.{10,25}Tom", "gu")]
    private static partial Regex Corvus_48();

    [global::System.Text.RegularExpressions.GeneratedRegex("Tom.{10,25}river|river.{10,25}Tom", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_48();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"([A-Za-z]awyer|[A-Za-z]inn)\s", "gu")]
    private static partial Regex Corvus_49();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"([A-Za-z]awyer|[A-Za-z]inn)\s", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_49();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex(@"\p{Sm}", "gu")]
    private static partial Regex Corvus_50();

    [global::System.Text.RegularExpressions.GeneratedRegex(@"\p{Sm}", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_50();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Шерлок Холмс", "gu")]
    private static partial Regex Corvus_51();

    [global::System.Text.RegularExpressions.GeneratedRegex("Шерлок Холмс", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_51();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Шерлок Холмс", "igu")]
    private static partial Regex Corvus_52();

    [global::System.Text.RegularExpressions.GeneratedRegex("Шерлок Холмс", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_52();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Шерлок Холмс|Джон Уотсон|Ирен Адлер|инспектор Лестрейд|профессор Мориарти", "gu")]
    private static partial Regex Corvus_53();

    [global::System.Text.RegularExpressions.GeneratedRegex("Шерлок Холмс|Джон Уотсон|Ирен Адлер|инспектор Лестрейд|профессор Мориарти", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_53();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("Шерлок Холмс|Джон Уотсон|Ирен Адлер|инспектор Лестрейд|профессор Мориарти", "igu")]
    private static partial Regex Corvus_54();

    [global::System.Text.RegularExpressions.GeneratedRegex("Шерлок Холмс|Джон Уотсон|Ирен Адлер|инспектор Лестрейд|профессор Мориарти", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial global::System.Text.RegularExpressions.Regex Net_54();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("夏洛克·福尔摩斯", "gu")]
    private static partial Regex Corvus_55();

    [global::System.Text.RegularExpressions.GeneratedRegex("夏洛克·福尔摩斯", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_55();

    [global::Corvus.Ecma262.RegularExpressions.GeneratedRegex("夏洛克·福尔摩斯|约翰华生|阿德勒|雷斯垂德|莫里亚蒂教授", "gu")]
    private static partial Regex Corvus_56();

    [global::System.Text.RegularExpressions.GeneratedRegex("夏洛克·福尔摩斯|约翰华生|阿德勒|雷斯垂德|莫里亚蒂教授", global::System.Text.RegularExpressions.RegexOptions.None)]
    private static partial global::System.Text.RegularExpressions.Regex Net_56();
}
