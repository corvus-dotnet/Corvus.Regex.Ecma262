// <copyright file="UnicodeSetsTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The v flag: the class grammar of set notation and strings, the properties of strings, and the case folding that
/// comes before the complement. The data set (unicode-sets.jsonl) is the gate; these are the points of the grammar
/// by name, and the two places where V8 departs from ECMA-262 and the data set is silent.
/// </summary>
[TestClass]
public partial class UnicodeSetsTests
{
    [GeneratedRegex(@"[[a-z]--[aeiou]]+", "gv")]
    private static partial Regex Consonants();

    [GeneratedRegex(@"^[\q{abc|ab|a}x]$", "giv")]
    private static partial Regex Strings();

    /// <summary>Each pattern with whether it is valid with v, with u and with no flag.</summary>
    private static readonly (string Pattern, bool Sets, bool Unicode, bool Legacy)[] Validity =
    [
        (@"[[a-z]--[aeiou]]", true, false, true), (@"[\p{L}&&\p{ASCII}]", true, true, true), (@"[a&&b]", true, true, true),
        (@"[\q{ab|c}]", true, false, true), (@"\p{RGI_Emoji}", true, false, true), (@"[\p{RGI_Emoji}--\q{\u{1F44D}}]", true, false, false),
        (@"[a!!b]", false, true, true), (@"[(]", false, true, true), (@"[a-]", false, true, true), (@"[a--]", false, false, false),
        (@"[a&&b--c]", false, false, false), (@"[ab--c]", false, false, false), (@"[a-z&&[^m]]", false, false, true),
        (@"[^\q{ab}]", false, false, true), (@"[^\q{a}]", true, false, true), (@"[^\p{RGI_Emoji}]", false, false, true),
        (@"\P{RGI_Emoji}", false, false, true), (@"\q{a}", false, false, true), (@"[\q{a}-b]", false, false, false),
        (@"[\&\&]", true, false, true), (@"[\!]", true, false, true), (@"[]", true, true, true), (@"[^]", true, true, true),
        (@"[\q{}]", true, false, true), (@"[\b]", true, true, true), (@"[\p{Nope}]", false, false, true),
    ];

    [TestMethod]
    public void EachPatternIsValidOrNotInEachOfTheThreeGrammars()
    {
        foreach ((string pattern, bool sets, bool unicode, bool legacy) in Validity)
        {
            Assert.AreEqual(sets, Regex.IsValid(pattern, RegexFlags.UnicodeSets), "v flag: " + pattern);
            Assert.AreEqual(unicode, Regex.IsValid(pattern, RegexFlags.Unicode), "u flag: " + pattern);
            Assert.AreEqual(legacy, Regex.IsValid(pattern, RegexFlags.None), "no flag: " + pattern);
        }
    }

    [TestMethod]
    [DataRow(@"^[[a-z]--[aeiou]]$", "v", "a", false)]
    [DataRow(@"^[[a-z]--[aeiou]]$", "v", "b", true)]
    [DataRow(@"^[\p{L}&&\p{ASCII}]$", "v", "é", false)]
    [DataRow(@"^[\p{L}&&\p{ASCII}]$", "v", "e", true)]
    [DataRow(@"^[\q{abc|ab|a}]$", "v", "ab", true)]
    [DataRow(@"^[\q{ab|a}]b$", "v", "ab", true)]
    [DataRow(@"^[\q{ab}--\q{ab}]$", "v", "ab", false)]
    [DataRow(@"^[\q{ab|cd}&&\q{cd|ef}]$", "v", "cd", true)]
    [DataRow(@"^[\q{ab|cd}&&\q{cd|ef}]$", "v", "ab", false)]
    [DataRow(@"^[\q{}]$", "v", "", true)]
    [DataRow(@"^[a&&b]$", "v", "&", false)]
    [DataRow(@"^[a&&b]$", "u", "&", true)]
    [DataRow(@"^[\&\&]$", "v", "&", true)]
    [DataRow(@"^\p{RGI_Emoji}$", "v", "\U0001F44D\U0001F3FD", true)]
    [DataRow(@"^\p{RGI_Emoji_Flag_Sequence}$", "v", "\U0001F1EB\U0001F1F7", true)]
    [DataRow(@"^\p{Emoji_Keycap_Sequence}$", "v", "1️⃣", true)]
    [DataRow(@"^\p{RGI_Emoji_ZWJ_Sequence}$", "v", "\U0001F468‍\U0001F469‍\U0001F467", true)]
    [DataRow(@"^\p{Basic_Emoji}$", "v", "✈", false)]
    [DataRow(@"^\p{Basic_Emoji}$", "v", "✈️", true)]
    [DataRow(@"^[\p{RGI_Emoji}--\p{RGI_Emoji_Flag_Sequence}]$", "v", "\U0001F1EB\U0001F1F7", false)]
    [DataRow(@"^\P{Lu}$", "iv", "a", false)]
    [DataRow(@"^\P{Lu}$", "iu", "a", true)]
    [DataRow(@"^[^\p{Lu}]$", "iv", "A", false)]
    [DataRow(@"^[\p{Lu}--[A]]$", "iv", "a", false)]
    [DataRow(@"^[[a-z]--[aeiou]]$", "iv", "A", false)]
    [DataRow(@"^[[a-z]--[aeiou]]$", "iv", "B", true)]
    [DataRow(@"^[\q{ab}]$", "iv", "AB", true)]
    [DataRow(@"^[s]$", "iv", "ſ", true)]
    public void IsMatchFollowsEcma262OverBothEncodings(string pattern, string flags, string text, bool expected)
    {
        Regex r = new(pattern, flags);
        Assert.AreEqual(expected, r.IsMatch(text), $"/{pattern}/{flags} on {text} (UTF-16)");
        Assert.AreEqual(expected, r.IsMatch(Encoding.UTF8.GetBytes(text)), $"/{pattern}/{flags} on {text} (UTF-8)");
    }

    /// <summary>Where V8 13.6 departs from ECMA-262 and the data set marks its records: a string of one character is a character of the class, and \p{ASCII} folds as the range it is.</summary>
    [TestMethod]
    [DataRow(@"^[\q{k}]$", "iv", "K", true)]
    [DataRow(@"^[\q{s}]$", "iv", "S", true)]
    [DataRow(@"^[\p{ASCII}]$", "iv", "ſ", true)]
    [DataRow(@"^[\p{Lu}--\p{ASCII}]$", "iv", "ſ", false)]
    public void TheSpecificationIsFollowedWhereV8Departs(string pattern, string flags, string text, bool expected)
    {
        Regex r = new(pattern, flags);
        Assert.AreEqual(expected, r.IsMatch(text), $"/{pattern}/{flags} on {text}");
    }

    [TestMethod]
    public void TheFlagIsReadAndWrittenAndExcludesU()
    {
        Regex r = new("a", "giv");
        Assert.AreEqual(RegexFlags.Global | RegexFlags.IgnoreCase | RegexFlags.UnicodeSets, r.Flags);
        Assert.AreEqual("giv", r.FlagsString);
        Assert.IsTrue(r.IsUnicode);
        Assert.ThrowsExactly<RegexParseException>(() => new Regex("a", "uv"));
    }

    [TestMethod]
    public void AClassWithStringsMatchesTheLongestStringFirst()
    {
        Regex r = new(@"[\q{abc|ab|a}]", "gv");
        Assert.AreEqual("abc", r.Match("xabcx").Value);
        Assert.AreEqual(1, r.Count("abc"));
        Assert.AreEqual(2, r.Count("abcab"));
    }

    [TestMethod]
    public void TheGeneratorReadsTheFlag()
    {
        Assert.AreEqual(RegexFlags.Global | RegexFlags.UnicodeSets, Consonants().Flags);
        Assert.AreEqual(3, Consonants().Count("sherlock"));
        Assert.AreEqual(3, Consonants().Count(Encoding.UTF8.GetBytes("sherlock")));
        Assert.IsTrue(Strings().IsMatch("AB"));
        Assert.IsTrue(Strings().IsMatch("x"));
        Assert.IsFalse(Strings().IsMatch("abx"));
    }
}
