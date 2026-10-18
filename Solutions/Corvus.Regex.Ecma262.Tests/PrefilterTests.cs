// <copyright file="PrefilterTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The scans that find where a match can start, and the loops that keep no choice, give the same answers as the
/// plain search: the faults the data set found while they were written, and the shapes each is for.
/// </summary>
[TestClass]
public class PrefilterTests
{
    [TestMethod]
    [DataRow(@"\ude00", "u", "😀,", false, "the low half of a pair is inside a character by code point")]
    [DataRow(@"\ude00", "", "😀,", true, "and a code unit without the u flag")]
    [DataRow(@"\ud83d", "", "😀", true, "the high half is read at the first byte over UTF-8")]
    [DataRow(@"[\ude00\ude01]x", "", "😀x", true, "a small set of low surrogates")]
    [DataRow(@"[^a]x", "u", "😀x", true, "a large set lands on a lead byte, not inside the character")]
    [DataRow(@"[a-z]shing", "u", "the fishing trip", true, "a required literal after a fixed ASCII term")]
    [DataRow(@"[a-z]shing", "u", "the shing trip", false, "the start the literal allows must match")]
    [DataRow(@"[a-z]shing", "u", "xshing shing", true, "")]
    [DataRow(@"[a-q][^u-z]{13}x", "u", "abcdefghijklmnx", true, "")]
    [DataRow(@"twain", "iu", "Mark TWAIN wrote", true, "a case-insensitive ASCII prefix")]
    [DataRow(@"twain", "iu", "Mark TWAIM wrote", false, "")]
    [DataRow(@"sawyer", "iu", "tom ſawyer", true, "the long s folds to s under the u flag, so this is not a plain case prefix")]
    [DataRow(@"sawyer", "i", "tom ſawyer", false, "and without it the long s stays itself")]
    [DataRow(@"[\w.+-]+@[\w.-]+", "u", "mail someone.else@example.com now", true, "an atomic loop before a literal")]
    [DataRow(@"[\w.+-]+@[\w.-]+", "u", "mail someone@@example now", false, "the loop cannot take the @, so no run precedes the second")]
    [DataRow(@"\w+\s+Holmes", "u", "Mr Sherlock  Holmes", true, "")]
    [DataRow(@"a*ab", "u", "aaab", true, "a loop that is not atomic still gives back")]
    [DataRow(@"a*", "u", "", true, "")]
    [DataRow(@"[^\n]*x", "u", "éé€ x", true, "a set that holds everything beyond ASCII")]
    [DataRow(@"[^\n]*x", "u", "é\n x", true, "")]
    [DataRow(@"^[^\n]*x$", "u", "é\n x", false, "")]
    [DataRow(@"Шерлок Холмс|Джон Уотсон", "u", "где Джон Уотсон", true, "a small first set beyond ASCII")]
    [DataRow(@"夏洛克|约翰", "u", "是约翰吗", true, "")]
    [DataRow(@"[\w.+-]+@[\w.-]+\.[\w.-]+", "u", "x ab@c.d", true, "a literal after a loop: the run before the literal is walked back")]
    [DataRow(@"[\w.+-]+@[\w.-]+\.[\w.-]+", "u", "x @c.d", false, "and a loop of at least one needs a run")]
    [DataRow(@"[\w.+-]+@[\w.-]+\.[\w.-]+", "u", "a@b a@c.d", true, "a failed occurrence moves on to the next")]
    [DataRow(@"[\w]*://x", "u", "see ://x", true, "a loop of at least zero needs none")]
    [DataRow(@"Sherlock", "iu", "a ſherlock", true, "the long s folds to s under the u flag: an extra beyond the ordinal comparison")]
    [DataRow(@"Sherlock", "iu", "a SHERLOC\u212a", true, "and the Kelvin sign to k")]
    [DataRow(@"Sherlock", "iu", "a SHERLOCK", true, "")]
    [DataRow(@"Sherlock", "iu", "a ſherloc", false, "")]
    [DataRow(@"Sherlock", "i", "a ſherlock", false, "without the u flag the long s is itself")]
    [DataRow(@"\w+\s+Holmes", "u", "Sherlock  Holmes", true, "a failed start skips to the end of its run")]
    [DataRow(@"\w+\s+Holmes", "u", "SherlockHolmes Holmes", true, "")]
    [DataRow(@"\w+\s+Holmes", "u", "Sherlock Holme", false, "")]
    [DataRow(@"(\w+)\s+Holmes", "u", "x Sherlock Holmes", true, "")]
    [DataRow(@"[^u-z]{3}xy", "u", "ééé\u00e9xy", true, "a required literal after a fixed prefix beyond ASCII, which only UTF-16 can offset")]
    [DataRow(@"[^u-z]{3}xy", "u", "éxy", false, "")]
    [DataRow(@"[^u-z]{3}xy", "u", "\ud83d\ude00\ud83d\ude00\ud83d\ude00xy", true, "by code point a supplementary character is one member, two units")]
    [DataRow(@"[^u-z]{3}xy", "", "\ud83d\ude00\ud83d\ude00\ud83d\ude00xy", true, "and by code unit it is two members")]
    [DataRow(@"[^u-z]{3}xy", "", "a\ud83d\ude00xy", true, "")]
    [DataRow(@"[^u-z]{3}xy", "u", "a\ud83d\ude00xy", false, "")]
    [DataRow(@"(.*)$", "u", "\nabd", true, "a start the loop took nothing at begins no run to skip")]
    [DataRow(@"[a-zA-Z]+ing", "u", "sing singing s", true, "a leading loop that keeps its choices still lets a failed start skip its run")]
    [DataRow(@"[a-zA-Z]+ing", "u", "sin sinn", false, "")]
    [DataRow(@"\b\w+n\b", "u", "the lion sleeps", true, "an assertion before the leading loop stops the skipping")]
    [DataRow(@"\b\w+n\b", "u", "the lions sleep", false, "")]
    [DataRow(@"\B\w+.{0,2}\S$", "u", "ab c", true, "a start inside the word holds \\B where the word's start does not")]
    [DataRow(@"the", "u", "In the end", true, "a literal alone")]
    [DataRow(@"the", "yu", "In the end", false, "sticky: not at the start")]
    [DataRow(@"\p{Lu}", "u", "abcDef", true, "a class alone")]
    [DataRow(@"\p{Lu}", "yu", "abcDef", false, "")]
    [DataRow(@"\p{Lu}", "yu", "Def", true, "")]
    [DataRow(@"[\ud800-\udbff]", "", "a\ud83d\ude00", true, "a class alone of surrogate units, found over UTF-8 at the first byte")]
    [DataRow(@"[\udc00-\udfff]", "", "a\ud83d\ude00", true, "and at the third")]
    [DataRow(@"[\udc00-\udfff]", "u", "a\ud83d\ude00", false, "by code point the pair is one character")]
    [DataRow(@"the", "iu", "In THE end", true, "a case-insensitive literal alone")]
    [DataRow(@"the", "iyu", "In THE end", false, "")]
    [DataRow(@"[\ud83d\ude00-\ud83d\ude4f]", "u", "\ud83d\ude00,", true, "a trail-byte scan can land on a middle byte of the member it is looking for")]
    [DataRow(@"[\u0428\u0448]x", "u", "\u0448x", true, "")]
    [DataRow(@"[\u0428\u0448]x", "u", "\u0418x", false, "")]
    [DataRow(@"\S{2}\ud801\udc00$", "u", "\ud801\udc00\ud801\udc00\ud801\udc00", true, "the literal's first occurrence has too few characters before it; a later one does")]
    [DataRow(@"\S{2}\ud801\udc00$", "u", "\ud801\udc00\ud801\udc00", false, "")]
    [DataRow(@"[^u-z]{13}x", "u", "\ud83d\ude00abcdefghijklx", true, "a required literal reached by walking back over characters of two units")]
    [DataRow(@"[a-z]*x", "u", "1ax", true, "")]
    public void TheScansAgreeWithThePlainSearchOverBothEncodings(string pattern, string flags, string text, bool expected, string why)
    {
        Regex r = new(pattern, flags);
        Assert.AreEqual(expected, r.IsMatch(text), $"UTF-16 {why}");
        Assert.AreEqual(expected, r.IsMatch(Encoding.UTF8.GetBytes(text)), $"UTF-8 {why}");
        Assert.AreEqual(expected, r.Match(text).Success, $"Match {why}");
    }

    [TestMethod]
    public void TheCountOfMatchesIsTheSameOverBothEncodingsAndAsNetCountsThem()
    {
        string text = "Шерлок Холмс and Джон Уотсон met 夏洛克 at 10.0.0.1, then x@y.z and TWAIN twain Twain, ſherlock \u212aing.";
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        foreach ((string pattern, string flags, string netPattern, System.Text.RegularExpressions.RegexOptions options) in new[]
        {
            (@"Шерлок Холмс|Джон Уотсон|夏洛克", "gu", @"Шерлок Холмс|Джон Уотсон|夏洛克", System.Text.RegularExpressions.RegexOptions.None),
            (@"twain", "giu", @"twain", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            (@"[\w.+-]+@[\w.-]+\.[\w.-]+", "gu", @"[a-zA-Z0-9_.+-]+@[a-zA-Z0-9_.-]+\.[a-zA-Z0-9_.-]+", System.Text.RegularExpressions.RegexOptions.None),
            (@"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", "gu", @"(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9])", System.Text.RegularExpressions.RegexOptions.None),
            (@"\w+", "gu", @"[a-zA-Z0-9_]+", System.Text.RegularExpressions.RegexOptions.None),
            (@"[a-z]+n", "gu", @"[a-z]+n", System.Text.RegularExpressions.RegexOptions.None),
            (@"\w+\s+and", "gu", @"[a-zA-Z0-9_]+\s+and", System.Text.RegularExpressions.RegexOptions.None),
            (@"[^u-z]{3}in", "gu", @"[^u-z]{3}in", System.Text.RegularExpressions.RegexOptions.None),
            (@"[a-zA-Z]+in", "gu", @"[a-zA-Z]+in", System.Text.RegularExpressions.RegexOptions.None),
            (@"\b\w+n\b", "gu", @"\b[a-zA-Z0-9_]+n\b", System.Text.RegularExpressions.RegexOptions.None),
            (@"n", "gu", @"n", System.Text.RegularExpressions.RegexOptions.None),
            (@"[a-z]", "gu", @"[a-z]", System.Text.RegularExpressions.RegexOptions.None),
            (@"and", "giu", @"and", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        })
        {
            int expected = System.Text.RegularExpressions.Regex.Count(text, netPattern, options);
            Regex r = new(pattern, flags);
            Assert.AreEqual(expected, r.Count(text), pattern + " UTF-16");
            Assert.AreEqual(expected, r.Count(utf8), pattern + " UTF-8");
        }
    }
}
