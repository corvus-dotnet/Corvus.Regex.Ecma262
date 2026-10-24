// <copyright file="AutomataTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The automata the search runs over ASCII text (see <c>Adfa</c>): the anchored automaton that finds where a match
/// ends, the forward automaton and its window, the reverse automaton of the terms before a literal and the check of
/// the terms after it, and the two-position search for literals whose bytes are common. Each search finds what the
/// plain search finds, by both engines over both encodings.
/// </summary>
[TestClass]
public class AutomataTests
{
    private static readonly string[] Texts =
    [
        string.Empty,
        "the thing sing singing  bring\tring\nking. Thing,ing ingot sing",
        "Huckleberry Finn and Tom Sawyer; Huck Saw Sawyers HuckHuck Sawé Huckß",
        "10.0.0.1 256.1.1.1 192.168.001.10 1.2.3 99.99.99.99x 1.1.1.1.1 25.250.255.249",
        "abc abcx abx ab cdx ab cd x aab aaab bbbbbbbbbbbbab",
        "éing ésing 中ing \U0001F600 sing x straße thing é sing ",
        string.Concat(Enumerable.Repeat("a sing ", 200)) + "end",
        "foo foobar barfoo foo_ fooé xfoo",
        "Tom went to the river; Sam to a lake. Tomxxriver Saméélake",
        "1px 22em 3pt 44pxé Walking Talking talking",
        "ab\ncd\nab cd",
        "see http://example.com/a/very/long/path/that/runs/on/and/on?query=1#frag and ftp://h\u00e9st/x?y and http://host/ending",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab aaaaaaaaaaaaaaaaaaaaaaaa\u00e9aaaab xyz@abcdefghijklmnopqrstuvwxyz.com",
    ];

    [TestMethod]
    [DataRow(@"\s[a-zA-Z]{0,12}ing\s", "gu", "a literal after a bounded loop: the reverse automaton and the rest check")]
    [DataRow(@"\s[a-zA-Z]{0,12}ing\s", "g", "")]
    [DataRow(@"[a-z]{1,4}ing\b", "gu", "")]
    [DataRow(@"\w{1,8}?ing", "gu", "a lazy loop before the literal")]
    [DataRow(@"\b[a-z]{0,6}ing", "gu", "a boundary before the loop, which the reverse automaton does not take")]
    [DataRow(@"[^\n]{1,4}ing", "gu", "a set beyond ASCII before the literal: the reverse automaton gives up there")]
    [DataRow(@"\p{L}{1,4}ing", "gu", "")]
    [DataRow(@"[a-z]{0,63}ing", "gu", "a window too wide for the reverse search")]
    [DataRow(@"Huck[a-zA-Z]+|Saw[a-zA-Z]+", "gu", "literals whose bytes are common: two positions compared at every vector")]
    [DataRow(@"Huck[a-zA-Z]+|Saw[a-zA-Z]+", "giu", "")]
    [DataRow(@"Zap|Xen", "gu", "literals that start with rare bytes, searched for alone")]
    [DataRow(@"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", "gu", "the forward automaton and its window")]
    [DataRow(@"(?:\d{1,3}\.){3}\d{1,3}", "gu", "")]
    [DataRow(@"(?:ab|a)c?x", "gu", "")]
    [DataRow(@"a+?b", "gu", "a lazy loop: the anchored automaton keeps the shortest")]
    [DataRow(@"x*", "gu", "a pattern that matches the empty string")]
    [DataRow(@"()(.a){0,2}$", "gu", "an empty match at the end of the text, where the forward search tries its last start")]
    [DataRow(@"^ab|cd$", "gu", "anchors, which make the start states differ")]
    [DataRow(@"^ab|cd$", "gmu", "line anchors, which no automaton takes")]
    [DataRow(@"\bfoo\B", "gu", "boundaries")]
    [DataRow(@"\bfoo\b", "giu", "a boundary that folds, which no automaton takes")]
    [DataRow(@"(?:Tom|Sam).{2,12}(?:river|lake)", "gu", "an alternation with a window after it")]
    [DataRow(@".{0,3}(?:ab|cd)x", "gu", "")]
    [DataRow(@"(?:ab|cd).{0,3}x", "gu", "")]
    [DataRow(@"(\w+)\s(\w+)", "gu", "groups, which the anchored automaton does not track")]
    [DataRow(@"(a|b)\1", "gu", "a backreference, for which there is no automaton")]
    [DataRow(@"(?=ab)a\w", "gu", "a lookahead, which no automaton takes")]
    [DataRow(@"[ab]*a[ab]{11}", "gu", "a pattern whose automaton has too many states")]
    [DataRow(@"(?:a?)+b", "gu", "a loop whose body can be empty, which no automaton takes")]
    [DataRow(@"\d+(?:px|em)", "gu", "")]
    [DataRow(@"[A-Z][a-z]+ing", "gu", "")]
    [DataRow(@"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?", "gu", "long runs in states that loop on large sets: the accelerated states")]
    [DataRow(@"[^\s]+b", "gu", "a run whose state loops with a match ending at none of its units")]
    [DataRow(@"a[^ ]*", "gu", "a run whose state loops with a match ending at each of its units")]
    [DataRow(@"[\w.+-]+@[\w.-]+\.[\w.-]+", "gu", "")]
    public void TheAutomataFindWhatThePlainSearchFinds(string pattern, string flags, string why)
    {
        Regex plain = Plain(pattern, flags);
        (Regex interpreted, Regex generated) = Engines.Both(pattern, flags);
        foreach (string text in Texts)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            string expected16 = Matches16(plain, text);
            string expected8 = Matches8(plain, utf8);
            string expectedGroups = Groups(plain, text);
            foreach ((Regex r, string engine) in new[] { (interpreted, "interpreted"), (generated, "generated") })
            {
                Assert.AreEqual(expected16, Matches16(r, text), $"UTF-16 {engine} over \"{text}\": {why}");
                Assert.AreEqual(expected8, Matches8(r, utf8), $"UTF-8 {engine} over \"{text}\": {why}");
                Assert.AreEqual(expectedGroups, Groups(r, text), $"groups {engine} over \"{text}\": {why}");
            }
        }
    }

    [TestMethod]
    public void EachSearchHasTheAutomataItIsFor()
    {
        SearchAutomata window = Bounds(@"\s[a-zA-Z]{0,12}ing\s", "u");
        Assert.IsNotNull(window.Anchored, "a program that tracks no group has the anchored automaton");
        Assert.IsNotNull(window.Reverse, "a literal after a bounded loop has the reverse automaton");
        Assert.IsNotNull(window.Rest, "and the check of the terms after it, which are more than the literal");
        Assert.IsNull(window.Forward, "and no forward search");

        SearchAutomata ends = Bounds(@"[a-z]{1,4}ing", "u");
        Assert.IsNotNull(ends.Reverse);
        Assert.IsNull(ends.Rest, "every literal ends its alternative: the occurrence is all the rest there is");

        SearchAutomata scan = Bounds(@"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", "u");
        Assert.IsNotNull(scan.Forward, "a search that tries every start its first set allows has the forward automaton");
        Assert.IsNotNull(scan.Skip, "and a window of digits and dots, rare in text");
        Assert.IsNull(scan.Reverse);

        Assert.IsNull(Bounds(@"[ab]*a[ab]{11}", "u").Anchored, "too many states");
        Assert.IsNull(Bounds(@"(?:a?)+b", "u").Anchored, "an iteration that can be empty");
        Assert.IsNull(Bounds(@"\bfoo\b", "iu").Anchored, "a boundary that folds");
        Assert.IsNull(Bounds(@"(?=ab)a\w", "u").Anchored, "a lookahead");
        Assert.IsNull(Bounds(@"^ab", "mu").Anchored, "a line anchor");
        SearchAutomata backreference = Bounds(@"(a|b)\1", "u");
        Assert.IsNull(backreference.Anchored, "a backreference");
        Assert.IsNull(backreference.Forward);

        Regex groups = new(@"(\w+)\s(\w+)", "u");
        Assert.IsNotNull(groups.Programs().Bounds.Automata().Anchored, "the bounds of a match track no group");
        Assert.IsNull(groups.Programs().Captures.Automata().Anchored, "but the captures do");
        Assert.IsNotNull(groups.Programs().Captures.Automata().Forward, "and the forward search finds their starts all the same");
    }

    [TestMethod]
    public void TheRestCheckIsDroppedWhereItSeldomRulesAnOccurrenceOut()
    {
        RestCheck rest = Bounds(@"\s[a-zA-Z]{0,12}ing\s", "u").Rest!;
        Assert.IsTrue(rest.RulesOut<char, Utf16Encoding>("ingx", 0), "ing then no space");
        Assert.IsFalse(rest.RulesOut<char, Utf16Encoding>("ing ", 0), "ing then a space");
        for (int k = 0; k < 64; k++)
        {
            Assert.IsFalse(rest.RulesOut<char, Utf16Encoding>("ing ", 0));
        }

        Assert.IsFalse(rest.RulesOut<char, Utf16Encoding>("ingx", 0), "one in sixty-four ruled out: the check no longer pays, and is not made");
        RestCheck kept = Bounds(@"\s[a-zA-Z]{0,12}ing\s", "u").Rest!;
        for (int k = 0; k < 64; k++)
        {
            Assert.IsTrue(kept.RulesOut<byte, Utf8Encoding>("ingx"u8, 0));
        }

        Assert.IsTrue(kept.RulesOut<byte, Utf8Encoding>("ingx"u8, 0), "every one ruled out: the check is kept");
        Assert.IsFalse(kept.RulesOut<byte, Utf8Encoding>("ingé"u8, 0), "a unit beyond ASCII rules nothing out");
    }

    [TestMethod]
    public void TheNeedleComparesTwoPositionsWhereNoPositionsBytesAreRare()
    {
        MultiNeedle<byte> common = new([Needles.Utf8("Huck"), Needles.Utf8("Saw")]);
        Assert.IsTrue(common.Two, "H or S, then another position, compared at every vector");
        Assert.AreEqual(12, common.IndexOf("x Huc Sa Hu Sawyer"u8, 0, out int length), "neither Huc nor Sa nor Hu is a literal");
        Assert.AreEqual(3, length);

#if NET
        Assert.AreEqual(12, common.IndexOfNarrowPair("x Huc Sa Hu Sawyer"u8, 0, out length), "the generated code's entry, over a text shorter than a vector");
        Assert.AreEqual(3, length);
        byte[] longText = Encoding.UTF8.GetBytes(new string('x', 100) + "Hu Sa Hxc Swxw Huck Saw");
        Assert.AreEqual(115, common.IndexOfNarrowPair(longText, 0, out length), "and over vectors: H then c at +2 lets Hxc through, which no literal is");
        Assert.AreEqual(4, length);
        Assert.AreEqual(120, common.IndexOfNarrowPair(longText, 116, out length));
        Assert.AreEqual(3, length);
        Assert.AreEqual(-1, common.IndexOfNarrowPair(longText, 121, out _));
#endif

        MultiNeedle<byte> rare = new([Needles.Utf8("Zap"), Needles.Utf8("Xen")]);
        Assert.IsFalse(rare.Two, "Z or X, rare enough to search for alone");
        Assert.AreEqual(5, rare.IndexOf("a Za Xen Zap"u8, 0));
#if NET
        Assert.AreEqual(5, rare.IndexOfNarrowPair("a Za Xen Zap"u8, 0, out length), "a needle with no narrow pair searches as it does");
        Assert.AreEqual(3, length);
#endif

        MultiNeedle<byte> one = new([Needles.Utf8("Ha"), Needles.Utf8("S")]);
        Assert.IsFalse(one.Two, "a literal of one byte leaves one position, searched for alone");
        Assert.AreEqual(2, one.IndexOf("x S Ha"u8, 0));

        MultiNeedle<ushort> utf16 = new([Needles.Utf16("Huck"), Needles.Utf16("Saw")]);
        Assert.IsFalse(utf16.Two, "over UTF-16 the cost model alone decides");
    }

    [TestMethod]
    [DataRow(".*", "gs", true, "the dot with the s flag: the run is the rest of the text")]
    [DataRow(".*", "gsu", true, "")]
    [DataRow(".*", "g", false, "the dot without it: each line, and an empty match after each")]
    [DataRow(@"[\s\S]*", "gu", true, "a class that holds every character")]
    [DataRow(@"[^\n]*", "gu", true, "")]
    [DataRow(@"\w+", "gu", true, "a loop of a class")]
    [DataRow(@"\p{L}", "gu", true, "a class alone")]
    [DataRow(@"\p{L}", "gyu", true, "sticky")]
    [DataRow("the", "gu", true, "a literal alone")]
    [DataRow("the", "gyu", true, "")]
    [DataRow(@"x*", "gu", false, "empty matches between the halves of no pair")]
    [DataRow(@"x*", "g", false, "and, by code unit, between the halves of each")]
    [DataRow(@"[ab]*", "gu", true, "a loop of a class with empty matches: by code point, none between the halves of a pair")]
    [DataRow(@"[ab]*", "g", true, "and by code unit, one there")]
    [DataRow("Sherlock|Holmes", "gu", true, "an alternation of literals")]
    public void TheCountOfASimpleShapeIsTheEnumerationsCount(string pattern, string flags, bool stateless, string why)
    {
        Regex r = new(pattern, flags);
        Assert.AreEqual(stateless, r.BoundsWithoutState, "a simple shape with no cursor is found with no state");
        foreach (string text in Texts.Concat(["the\nthe x\U0001F600x\nSherlock Holmes\n", "\U0001F600\U0001F600"]))
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            int expected16 = 0;
            foreach (ValueMatch m in r.EnumerateMatches(text.AsSpan()))
            {
                expected16++;
            }

            int expected8 = 0;
            foreach (ValueMatch m in r.EnumerateMatches(utf8))
            {
                expected8++;
            }

            Assert.AreEqual(expected16, r.Count(text), $"UTF-16 over \"{text}\": {why}");
            Assert.AreEqual(expected8, r.Count(utf8), $"UTF-8 over \"{text}\": {why}");
            Assert.AreEqual(r.Matches(text).Count, r.Count(text), $"against the matches over \"{text}\": {why}");
        }
    }

    /// <summary>A pattern's bounds automata.</summary>
    private static SearchAutomata Bounds(string pattern, string flags) => new Regex(pattern, flags).Programs().Bounds.Automata();

    /// <summary>The pattern with no automata: its programs' automata are built at once, with the automata off.</summary>
    private static Regex Plain(string pattern, string flags)
    {
        Regex r = new(pattern, flags);
        SearchAutomata.Off = true;
        try
        {
            (Program bounds, Program captures) = r.Programs();
            Assert.IsNull(bounds.Automata().Anchored);
            Assert.IsNull(captures.Automata().Forward);
        }
        finally
        {
            SearchAutomata.Off = false;
        }

        return r;
    }

    private static string Matches16(Regex r, string text)
    {
        StringBuilder sb = new();
        foreach (ValueMatch m in r.EnumerateMatches(text.AsSpan()))
        {
            sb.Append(m.Index).Append(':').Append(m.Length).Append('|');
        }

        return sb.ToString();
    }

    private static string Matches8(Regex r, byte[] utf8)
    {
        StringBuilder sb = new();
        foreach (ValueMatch m in r.EnumerateMatches(utf8))
        {
            sb.Append(m.Index).Append(':').Append(m.Length).Append('|');
        }

        return sb.ToString();
    }

    private static string Groups(Regex r, string text)
    {
        StringBuilder sb = new();
        Match m = r.Match(text);
        while (m.Success)
        {
            foreach (Group g in m.Groups)
            {
                sb.Append(g.Success ? $"{g.Index}:{g.Length}" : "-").Append(',');
            }

            sb.Append('|');
            m = m.NextMatch();
        }

        return sb.ToString();
    }
}
