// <copyright file="CaptureTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>Match, Groups, Matches, Replace, Split, Escape and the span forms, with the answers ECMAScript gives.</summary>
[TestClass]
public class CaptureTests
{
    [TestMethod]
    public void AMatchReportsItsGroups()
    {
        Match m = new Regex(@"(\d+)-(\d+)?-(?<name>x)?").Match("ab 12--x cd");
        Assert.IsTrue(m.Success);
        Assert.AreEqual(3, m.Index);
        Assert.AreEqual("12--x", m.Value);
        Assert.AreEqual(4, m.Groups.Count);
        Assert.AreEqual("12", m.Groups[1].Value);
        Assert.IsFalse(m.Groups[2].Success);
        Assert.AreEqual(string.Empty, m.Groups[2].Value);
        Assert.AreEqual(0, m.Groups[2].Captures.Count);
        Assert.AreEqual("x", m.Groups["name"].Value);
        Assert.AreEqual("name", m.Groups[3].Name);
        Assert.AreEqual(1, m.Groups[1].Captures.Count);
        Assert.IsFalse(m.Groups["nope"].Success);
        Assert.AreEqual("1", m.Groups[1].Name);
    }

    [TestMethod]
    public void AGroupUnderAQuantifierKeepsItsLastIteration()
    {
        Match m = new Regex(@"^(?:(a)|b)+$").Match("ab");
        Assert.IsTrue(m.Success);
        Assert.IsFalse(m.Groups[1].Success, "the last iteration cleared the group");
        Match m2 = new Regex(@"^(?:(a)|b)+$").Match("ba");
        Assert.AreEqual("a", m2.Groups[1].Value);
    }

    [TestMethod]
    public void DuplicateNamesGiveTheGroupThatTookPart()
    {
        Regex r = new(@"(?:(?<y>\d{4})-(?<m>\d{2})|(?<m>\d{2})/(?<y>\d{4}))", RegexFlags.Unicode);
        Assert.AreEqual("2024", r.Match("2024-05").Groups["y"].Value);
        Assert.AreEqual("05", r.Match("2024-05").Groups["m"].Value);
        Assert.AreEqual("1999", r.Match("12/1999").Groups["y"].Value);
        Assert.AreEqual("12", r.Match("12/1999").Groups["m"].Value);
    }

    [TestMethod]
    public void ALookbehindCapturesBackwards()
    {
        Match m = new Regex(@"(?<=(\d+)(\d+))$").Match("1053");
        Assert.IsTrue(m.Success);
        Assert.AreEqual("1", m.Groups[1].Value);
        Assert.AreEqual("053", m.Groups[2].Value);
    }

    [TestMethod]
    public void MatchesIsTheGlobalSearch()
    {
        MatchCollection ms = new Regex(@"a*").Matches("baaac");
        string[] values = ms.Select(m => m.Value).ToArray();
        CollectionAssert.AreEqual(new[] { "", "aaa", "", "" }, values);
        Assert.AreEqual(4, ms.Count);
        Assert.AreEqual(1, ms[1].Index);
    }

    [TestMethod]
    public void AnEmptyMatchAdvancesByACodePointWithTheUFlagAndACodeUnitWithout()
    {
        Assert.AreEqual(3, new Regex(@"(?:)", RegexFlags.Unicode).Matches("\ud83d\ude00a").Count);
        Assert.AreEqual(4, new Regex(@"(?:)").Matches("\ud83d\ude00a").Count);
        Assert.AreEqual(3, new Regex(@"(?:)", RegexFlags.Unicode).Count(Encoding.UTF8.GetBytes("\ud83d\ude00a")));
        Assert.AreEqual(4, new Regex(@"(?:)").Count(Encoding.UTF8.GetBytes("\ud83d\ude00a")));
    }

    [TestMethod]
    public void StickyMatchesStopAtTheFirstGap()
    {
        Assert.AreEqual(2, new Regex(@"a", RegexFlags.Sticky | RegexFlags.Global).Matches("aaba").Count);
        Assert.AreEqual(0, new Regex(@"a", RegexFlags.Sticky).Matches("baa").Count);
    }

    [TestMethod]
    public void ReplaceFollowsGetSubstitution()
    {
        Regex r = new(@"(\d)(\d)?(?<x>\d)?");
        Assert.AreEqual("[1|2||1]-45", r.Replace("12-45", "[$1|$2|$<x>|$01]"));
        Assert.AreEqual("[1][3]5", new Regex(@"(\d)\d", RegexFlags.Global).Replace("12345", "[$1]"));
        Assert.AreEqual("a$b", new Regex("x").Replace("axb", "$$"));
        Assert.AreEqual("a<x><a><b>b", new Regex("x").Replace("axb", "<$&><$`><$'>"));
        Assert.AreEqual("a$3$<n>b", new Regex("(x)").Replace("axb", "$3$<n>"), "a group that does not exist, and no named groups");
        Assert.AreEqual("ab", new Regex("(?<n>x)").Replace("axb", "$<m>"), "an unknown name is the empty string");
        Assert.AreEqual("a$<nb", new Regex("(?<n>x)").Replace("axb", "$<n"), "no closing bracket");
        Assert.AreEqual("[1][2][3][4]", new Regex(@"(\d)(\d)(\d)(\d)?", RegexFlags.Global).Replace("1234", m => m.Groups.Skip(1).Where(g => g.Success).Select(g => "[" + g.Value + "]").Aggregate((a, b) => a + b)));
        Assert.AreEqual("12", new Regex(@"(\d)(\d)").Replace("12", "$12"), "$12 reads as group 12 only when there are twelve groups, else as group 1 then 2");
    }

    [TestMethod]
    public void SplitFollowsSymbolSplit()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, new Regex(",").Split("a,b,c"));
        CollectionAssert.AreEqual(new[] { "a", ",", "b", ",", "c" }, new Regex("(,)").Split("a,b,c"));
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, new Regex("").Split("abc"));
        CollectionAssert.AreEqual(new[] { "", "b" }, new Regex("a").Split("ab"));
        CollectionAssert.AreEqual(new[] { "a", "" }, new Regex("b").Split("ab"));
        CollectionAssert.AreEqual(new[] { "a", "b" }, new Regex(",").Split("a,b,c", 2));
        CollectionAssert.AreEqual(new string[0], new Regex(",").Split("a,b", 0));
        CollectionAssert.AreEqual(new[] { "" }, new Regex("x").Split(""));
        CollectionAssert.AreEqual(new string[0], new Regex("").Split(""));
        CollectionAssert.AreEqual(new string?[] { "a", null, "b" }, new Regex("(x)?,").Split("a,b"));
        CollectionAssert.AreEqual(new[] { "\ud83d", "\ude00" }, new Regex("").Split("\ud83d\ude00"));
        CollectionAssert.AreEqual(new[] { "\ud83d\ude00" }, new Regex("", RegexFlags.Unicode).Split("\ud83d\ude00"));
    }

    [TestMethod]
    public void EscapeIsRegExpEscape()
    {
        Assert.AreEqual(@"\x61b", Regex.Escape("ab"));
        Assert.AreEqual(@"\.\*\(\)\[\]\{\}\|\^\$\\\/\?\+", Regex.Escape(@".*()[]{}|^$\/?+"));
        Assert.AreEqual(@"\x61\x2cb\x2d\x3d\x3c\x3e\x23\x26\x21\x25\x3a\x3b\x40\x7e\x27\x60\x22", Regex.Escape("a,b-=<>#&!%:;@~'`\""));
        Assert.AreEqual(@"\t\n\v\f\r\x20\u2028\ufeff", Regex.Escape("\t\n\v\f\r \u2028\ufeff"));
        Assert.AreEqual(@"\ud800" + "\ud83d\ude00", Regex.Escape("\ud800\ud83d\ude00"), "a lone surrogate is escaped, a pair is itself");
        string text = "a.b-c d\ud800";
        Assert.AreEqual(text, Regex.Unescape(Regex.Escape(text)));
        Assert.IsTrue(new Regex("^" + Regex.Escape(text) + "$", RegexFlags.Unicode).IsMatch(text));
    }

    [TestMethod]
    public void TheSpanFormsReportTheSameMatchesWithoutAllocating()
    {
        Regex r = new(@"(\w)(\d)?", RegexFlags.Global);
        string text = "a1 b c3 déf";
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        List<(int, int)> expected = r.Matches(text).Select(m => (m.Index, m.Length)).ToList();
        List<(int, int)> actual = [];
        foreach (ValueMatch m in r.EnumerateMatches(text.AsSpan()))
        {
            actual.Add((m.Index, m.Length));
        }

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(expected.Count, r.Count(utf8));

        ValueCapture[] caps = new ValueCapture[r.GroupCount + 1];
        Assert.IsTrue(r.TryMatch(text.AsSpan(), 3, caps, out ValueMatch vm));
        Assert.AreEqual(3, vm.Index);
        Assert.AreEqual(1, vm.Length);
        Assert.IsTrue(caps[1].Success);
        Assert.IsFalse(caps[2].Success);
        Assert.IsTrue(r.TryMatch(utf8, 0, caps, out vm));
        Assert.AreEqual(2, caps[0].Length);

        long allocated = Allocations.Least(
            () =>
            {
                r.Count(text.AsSpan());
                r.Count(utf8);
                r.TryMatch(text.AsSpan(), 0, caps, out _);
                r.TryMatch(utf8, 0, caps, out _);
            },
            200,
            out string windows);
        Assert.AreEqual(0, allocated, $"allocated {windows} bytes in three windows of 200 passes");
    }

    [TestMethod]
    public void AStartInsideASurrogatePairIsTheCharacterWithTheUFlagAndAUnitWithout()
    {
        // RegExpBuiltinExec: with the u flag, lastIndex inside a pair means the character that holds it.
        Regex u = new(@"()", RegexFlags.Unicode | RegexFlags.Sticky);
        Assert.AreEqual(0, u.Match("\ud83d\ude00,", 1).Index);
        Regex dot = new(@".", RegexFlags.Unicode);
        Assert.AreEqual(2, dot.Match("\ud83d\ude00,", 1).Length);
        Regex legacy = new(@"()", RegexFlags.Sticky);
        Assert.AreEqual(1, legacy.Match("\ud83d\ude00,", 1).Index);

        byte[] utf8 = Encoding.UTF8.GetBytes("\ud83d\ude00,");
        Span<ValueCapture> caps = stackalloc ValueCapture[1];
        Assert.IsTrue(dot.TryMatch(utf8, 2, caps, out ValueMatch vm));
        Assert.AreEqual(0, vm.Index);
        Assert.AreEqual(4, vm.Length);
        Regex legacyDot = new(@".", RegexFlags.Sticky);
        Assert.IsTrue(legacyDot.TryMatch(utf8, 2, caps, out vm), "the third byte is the low surrogate's position");
        Assert.AreEqual(2, vm.Index);
        Assert.AreEqual(2, vm.Length);
    }

    [TestMethod]
    public void NextMatchAndResult()
    {
        Match m = new Regex(@"(\w)").Match("ab");
        Assert.AreEqual("a", m.Value);
        Match n = m.NextMatch();
        Assert.AreEqual("b", n.Value);
        Assert.IsFalse(n.NextMatch().Success);
        Assert.AreEqual("<b>a", n.Result("<$1>$`"));
        Assert.IsFalse(Match.Empty.Success);
        Assert.AreEqual(string.Empty, Match.Empty.Value);
    }

    [TestMethod]
    public void GroupNamesAndNumbers()
    {
        Regex r = new(@"(a)(?<x>b)(?<y>c)");
        CollectionAssert.AreEqual(new[] { "x", "y" }, r.GetGroupNames());
        Assert.AreEqual(2, r.GroupNumberFromName("x"));
        Assert.AreEqual(-1, r.GroupNumberFromName("z"));
        Assert.AreEqual("y", r.GroupNameFromNumber(3));
        Assert.AreEqual("1", r.GroupNameFromNumber(1));
    }
}
