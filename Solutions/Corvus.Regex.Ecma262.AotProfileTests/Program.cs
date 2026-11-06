// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Text;

namespace Corvus.Ecma262.RegularExpressions.AotProfileTests;

/// <summary>
/// Matches patterns that the automaton answers, as UTF-16 and as UTF-8, in a native AOT program compiled with a
/// static profile, and checks every answer.
/// </summary>
public static class Program
{
    private static readonly (string Pattern, string Text, bool Expected)[] Cases =
    [
        ("^[0-9]+(ns|ms|us|µs|s|m|h)$", "30s", true),
        ("^[0-9]+(ns|ms|us|µs|s|m|h)$", "1ms", true),
        ("^[0-9]+(ns|ms|us|µs|s|m|h)$", "250µs", true),
        ("^[0-9]+(ns|ms|us|µs|s|m|h)$", "10h", true),
        ("^[0-9]+(ns|ms|us|µs|s|m|h)$", "1x", false),
        ("^[0-9]+(ns|ms|us|µs|s|m|h)$", "s", false),
        ("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)$", "1.2", true),
        ("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)$", "10.0", true),
        ("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)$", "01.2", false),
        ("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", "1.120.3-beta.1", true),
        ("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", "1.2", false),
        ("^(es3|es5|es6|es2015|es2016|es2017|es2018|es2019|es2020|esnext)$", "es2017", true),
        ("^(es3|es5|es6|es2015|es2016|es2017|es2018|es2019|es2020|esnext)$", "es4", false),
        ("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$", "sap.ui.core", true),
        ("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$", "sap", false),
        ("\\b(foo|bar)\\b", "a bar b", true),
        ("\\b(foo|bar)\\b", "abarb", false),
        ("^\\w+@\\w+\\.(com|org|net)$", "a@b.org", true),
        ("^\\w+@\\w+\\.(com|org|net)$", "a@b.io", false),
    ];

    /// <summary>Matches every case and reports the wrong answers.</summary>
    /// <returns>0 when every answer is right, otherwise 1.</returns>
    public static int Main()
    {
        int wrong = 0;
        foreach ((string pattern, string text, bool expected) in Cases)
        {
            var expression = new Regex(pattern, RegexFlags.Unicode, TimeSpan.FromSeconds(10), RegexEdition.Es2020, RegexUnicodeVersion.Default);
            bool utf16 = expression.IsMatch(text);
            bool utf8 = expression.IsMatch(Encoding.UTF8.GetBytes(text));
            if (utf16 != expected || utf8 != expected)
            {
                wrong++;
                Console.WriteLine($"Wrong: /{pattern}/u on \"{text}\" gave {utf16} (UTF-16) and {utf8} (UTF-8), not {expected}");
            }
        }

        Console.WriteLine($"{Cases.Length - wrong} of {Cases.Length} answers right");
        return wrong == 0 ? 0 : 1;
    }
}
