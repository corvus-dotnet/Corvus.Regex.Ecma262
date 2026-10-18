// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.AotSample;

/// <summary>
/// A native AOT program that uses a generated regular expression over UTF-16 and UTF-8: the gate of phase 4 is
/// that it publishes and runs.
/// </summary>
public static partial class Program
{
    [GeneratedRegex(@"\b(?<word>[a-z]+)\b", "giu")]
    private static partial Regex Words();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}", "gu")]
    private static partial Regex Dates { get; }

    /// <summary>Counts the words and the dates in the text given, or in a sample.</summary>
    /// <param name="args">The text, as one argument, or none.</param>
    /// <returns>0.</returns>
    public static int Main(string[] args)
    {
        string text = args.Length > 0 ? args[0] : "The game, Watson, is afoot: it began on 1891-04-04 and ended on 1891-04-05.";
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        int words = Words().Count(text);
        int words8 = Words().Count(utf8);
        int dates = Dates.Count(text);
        int dates8 = Dates.Count(utf8);
        Console.WriteLine($"words {words} (UTF-8 {words8}), dates {dates} (UTF-8 {dates8})");
        Span<ValueCapture> captures = stackalloc ValueCapture[Words().GroupCount + 1];
        foreach (ValueMatch m in Words().EnumerateMatches(text.AsSpan(), captures))
        {
            Console.Write(text.AsSpan(captures[1].Index, captures[1].Length));
            Console.Write(' ');
        }

        Console.WriteLine();
        return words == words8 && dates == dates8 ? 0 : 1;
    }
}
