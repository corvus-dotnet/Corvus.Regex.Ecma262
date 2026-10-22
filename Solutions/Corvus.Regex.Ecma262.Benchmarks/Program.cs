// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

// "industry" and "--one" run the Industry benchmark under the interleaved protocol of the other harnesses (Driver.cs);
// anything else is BenchmarkDotNet's. The parameter column holds the whole workload name, so that no two workloads share
// a name in the results.
if (Corvus.Ecma262.RegularExpressions.Benchmarks.Driver.TryRun(args))
{
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance.WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(120)));

/// <summary>The entry point.</summary>
public partial class Program
{
}
