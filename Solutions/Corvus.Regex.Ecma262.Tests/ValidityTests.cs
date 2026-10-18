// <copyright file="ValidityTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The hand-written patterns of the Java port's EcmaRegexValidatorTest, as the Go engine's TestJavaHandWrittenPatterns
/// has them: each with whether it is valid with the u flag and whether it is valid in either grammar (the u flag
/// grammar, or the Annex B grammar of a pattern with no flag).
/// </summary>
[TestClass]
public class ValidityTests
{
    private static readonly (string Pattern, bool Unicode, bool Either)[] JavaHand =
    [
        ("", true, true), ("a", true, true), ("^a$", true, true), ("a|b", true, true), ("(a)", true, true),
        ("(?:a)", true, true), ("(?=a)", true, true), ("(?!a)", true, true), ("(?<=a)", true, true),
        ("(?<!a)", true, true), (@"(?<n>a)\k<n>", true, true), (@"\k<n>", false, true), ("(?<n>a)(?<n>b)", false, false),
        ("(?<$x_1>a)", true, true), ("(?<1a>a)", false, false), ("a{2}", true, true), ("a{2,}", true, true),
        ("a{2,3}", true, true), ("a{3,2}", false, false), ("a{", false, true), ("a{,2}", false, true),
        ("{", false, true), ("}", false, true), ("]", false, true), ("[", false, false), ("[]", true, true),
        ("[^]", true, true), ("[a-z]", true, true), ("[z-a]", false, false), (@"[\d-z]", false, true),
        (@"[a-\d]", false, true), (@"[\b]", true, true), (@"[\-]", true, true), (@"\-", false, true), (@"\a", false, true),
        (@"\c", false, true), (@"\cA", true, true), (@"\c1", false, true), (@"\0", true, true), (@"\01", false, true),
        (@"\1", false, true), (@"(a)\1", true, true), (@"(a)\2", false, true), (@"\x4", false, true), (@"\x41", true, true),
        (@"\u004", false, true), (@"A", true, true), (@"\u{1F600}", true, true), (@"\u{110000}", false, true),
        ("😀", true, true), ("[😀-🙏]", true, true), (@"\p{L}", true, true),
        (@"\p{Letter}", true, true), (@"\p{digit}", true, true), (@"\p{Nope}", false, true), (@"\p{gc=Lu}", true, true),
        (@"\p{Script=Greek}", true, true), (@"\p{sc=Grek}", true, true), (@"\p{Script=Nope}", false, true),
        (@"\P{ASCII}", true, true), (@"\p", false, true), (@"\p{", false, true), ("a**", false, false), ("a+?", true, true),
        ("*a", false, false), ("(?=a)*", false, true), ("^*", false, false), ("$+", false, false), (@"\b+", false, false),
        ("a)", false, false), ("(a", false, false), ("(?a)", false, false), (@"\/", true, true), (@"\.", true, true),
        (@"a\", false, false), ("[a", false, false), ("x{1}{2}", false, false), (@"\w+@\w+\.\w+", true, true),
        ("^[a-z][a-z0-9_]*$", true, true), (@"(?<a>.)\k<a>", true, true), (@"[\p{L}\d]", true, true),
        (@"\s\S\w\W\d\D", true, true),
    ];

    [TestMethod]
    public void TheHandWrittenPatternsAreValidOrNotInEachGrammar()
    {
        foreach ((string pattern, bool unicode, bool either) in JavaHand)
        {
            Assert.AreEqual(unicode, Regex.IsValid(pattern, RegexFlags.Unicode), "u flag: " + pattern);
            Assert.AreEqual(either, unicode || Regex.IsValid(pattern, RegexFlags.None), "either grammar: " + pattern);
        }
    }

    [TestMethod]
    public void AnInvalidPatternThrowsRegexParseExceptionWithTheOffset()
    {
        RegexParseException e = Assert.ThrowsExactly<RegexParseException>(() => new Regex("a{3,2}", RegexFlags.Unicode));
        Assert.AreEqual("Numbers out of order in {} quantifier", e.Reason);
        Assert.IsTrue(e.Offset >= 1 && e.Offset <= 6, "offset " + e.Offset);
    }

    [TestMethod]
    public void TheFlagsStringIsReadAndWritten()
    {
        Regex r = new("a", "yusmig");
        Assert.AreEqual(RegexFlags.Global | RegexFlags.IgnoreCase | RegexFlags.Multiline | RegexFlags.DotAll | RegexFlags.Unicode | RegexFlags.Sticky, r.Flags);
        Assert.AreEqual("gimsuy", r.FlagsString);
        Assert.ThrowsExactly<RegexParseException>(() => new Regex("a", "gg"));
        Assert.ThrowsExactly<RegexParseException>(() => new Regex("a", "x"));
        Assert.ThrowsExactly<RegexParseException>(() => new Regex("a", RegexFlags.Unicode | RegexFlags.UnicodeSets));
    }
}
