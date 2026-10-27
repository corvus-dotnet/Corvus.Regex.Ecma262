// <copyright file="EditionAndUnicodeVersionTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The edition of the grammar and the version of Unicode: ECMAScript 2020 refuses what later editions added and
/// nothing else, and each version of Unicode has its own scripts and case folding, in the interpreter and in the
/// generated code alike.
/// </summary>
[TestClass]
public partial class EditionAndUnicodeVersionTests
{
    // U+10570 and U+10597 are a capital and a small letter of Vithkuqi, a script of Unicode 14. U+A7C0 and U+A7C1 are
    // the capital and the small Old Polish O, also of Unicode 14.
    private const string VithkuqiPair = "\U00010570\U00010597";
    private const string OldPolishPair = "Ꟁꟁ";

    [GeneratedRegex(@"(.)\1", "u", UnicodeVersion = RegexUnicodeVersion.V13)]
    private static partial Regex BackreferenceUnicode13 { get; }

    [GeneratedRegex(@"(.)\1", "iu", UnicodeVersion = RegexUnicodeVersion.V13)]
    private static partial Regex FoldedBackreferenceUnicode13 { get; }

    [GeneratedRegex(@"(.)\1", "iu")]
    private static partial Regex FoldedBackreference { get; }

    [GeneratedRegex(@"(?:x|(.))+\1$", "i", UnicodeVersion = RegexUnicodeVersion.V13)]
    private static partial Regex FoldedLoopBackreferenceUnicode13 { get; }

    [GeneratedRegex(@"(?:x|(.))+\1$", "i")]
    private static partial Regex FoldedLoopBackreference { get; }

    [GeneratedRegex(@"\p{Script=Greek}+", "u", Edition = RegexEdition.Es2020, UnicodeVersion = RegexUnicodeVersion.Latest)]
    private static partial Regex Greek2020 { get; }

    [TestMethod]
    public void TheDefaultsAreTodaysGrammarAndV8sUnicode()
    {
        Regex r = new("a");
        Assert.AreEqual(RegexEdition.Current, r.Edition);
        Assert.AreEqual(RegexUnicodeVersion.V17, r.UnicodeVersion);
    }

    [TestMethod]
    [DataRow(RegexUnicodeVersion.Default, RegexUnicodeVersion.V17)]
    [DataRow(RegexUnicodeVersion.V13, RegexUnicodeVersion.V13)]
    [DataRow(RegexUnicodeVersion.V17, RegexUnicodeVersion.V17)]
    [DataRow(RegexUnicodeVersion.V18, RegexUnicodeVersion.V18)]
    [DataRow(RegexUnicodeVersion.Latest, RegexUnicodeVersion.V18)]
    public void AVersionIsResolvedWhenThePatternIsCompiled(RegexUnicodeVersion asked, RegexUnicodeVersion resolved)
    {
        Assert.AreEqual(resolved, new Regex("a", RegexFlags.None, Regex.InfiniteMatchTimeout, RegexEdition.Current, asked).UnicodeVersion);
    }

    [TestMethod]
    public void AVersionOrEditionThatIsNoneIsRefused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Regex("a", RegexFlags.None, Regex.InfiniteMatchTimeout, RegexEdition.Current, (RegexUnicodeVersion)16));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Regex("a", RegexFlags.None, Regex.InfiniteMatchTimeout, (RegexEdition)2020, RegexUnicodeVersion.Default));
    }

    [TestMethod]
    [DataRow(@"\p{Script=Vithkuqi}", RegexUnicodeVersion.V13, false)]
    [DataRow(@"\p{Script=Vithkuqi}", RegexUnicodeVersion.Default, true)]
    [DataRow(@"\p{Script=Yezidi}", RegexUnicodeVersion.V13, true)]
    [DataRow(@"\p{Script=Jurchen}", RegexUnicodeVersion.Default, false)]
    [DataRow(@"\p{scx=Jurc}", RegexUnicodeVersion.V18, true)]
    [DataRow(@"\p{Script=Jurchen}", RegexUnicodeVersion.Latest, true)]
    public void EachVersionHasItsOwnScripts(string pattern, RegexUnicodeVersion version, bool valid)
    {
        Assert.AreEqual(valid, Regex.IsValid(pattern, RegexFlags.Unicode, RegexEdition.Current, version), pattern);
    }

    [TestMethod]
    public void ALiteralInACaseInsensitivePatternFoldsAsItsVersionFolds()
    {
        Assert.IsTrue(Make(@"\u{10570}", RegexFlags.IgnoreCase | RegexFlags.Unicode, RegexUnicodeVersion.V17).IsMatch("\U00010597"));
        Assert.IsFalse(Make(@"\u{10570}", RegexFlags.IgnoreCase | RegexFlags.Unicode, RegexUnicodeVersion.V13).IsMatch("\U00010597"));
        Assert.IsTrue(Make(@"Ꟁ", RegexFlags.IgnoreCase, RegexUnicodeVersion.V17).IsMatch("ꟁ"));
        Assert.IsFalse(Make(@"Ꟁ", RegexFlags.IgnoreCase, RegexUnicodeVersion.V13).IsMatch("ꟁ"));
    }

    [TestMethod]
    [DataRow(RegexFlags.IgnoreCase | RegexFlags.Unicode, VithkuqiPair)]
    [DataRow(RegexFlags.IgnoreCase, OldPolishPair)]
    public void ABackreferenceInACaseInsensitivePatternFoldsAsItsVersionFolds(RegexFlags flags, string text)
    {
        foreach (string pattern in new[] { @"(.)\1", @"(?<=\1(.))", @"(?:x|(.))+\1$" })
        {
            Assert.IsTrue(Make(pattern, flags, RegexUnicodeVersion.V17).IsMatch(text), pattern + " under 17");
            Assert.IsTrue(Make(pattern, flags, RegexUnicodeVersion.V18).IsMatch(text), pattern + " under 18");
            Assert.IsFalse(Make(pattern, flags, RegexUnicodeVersion.V13).IsMatch(text), pattern + " under 13");
            string first = char.ConvertFromUtf32(char.ConvertToUtf32(text, 0));
            Assert.IsTrue(Make(pattern, flags, RegexUnicodeVersion.V13).IsMatch(first + first), pattern + " under 13, the same letter twice");
        }
    }

    [TestMethod]
    public void TheGeneratedCodeOfABackreferenceFoldsAsItsVersionFolds()
    {
        Assert.AreEqual(RegexUnicodeVersion.V13, FoldedBackreferenceUnicode13.UnicodeVersion);
        Assert.IsFalse(FoldedBackreferenceUnicode13.IsMatch(VithkuqiPair));
        Assert.IsTrue(FoldedBackreference.IsMatch(VithkuqiPair));
        Assert.IsFalse(BackreferenceUnicode13.IsMatch(VithkuqiPair));
        Assert.IsTrue(BackreferenceUnicode13.IsMatch("\U00010570\U00010570"));
        Assert.IsFalse(FoldedLoopBackreferenceUnicode13.IsMatch(OldPolishPair));
        Assert.IsTrue(FoldedLoopBackreference.IsMatch(OldPolishPair));
        Assert.AreEqual(1, FoldedBackreferenceUnicode13.Count(System.Text.Encoding.UTF8.GetBytes("\U00010597\U00010597")));
    }

    [TestMethod]
    public void TheGeneratedCodeCarriesTheEdition()
    {
        Assert.AreEqual(RegexEdition.Es2020, Greek2020.Edition);
        Assert.AreEqual(RegexUnicodeVersion.V18, Greek2020.UnicodeVersion);
        Assert.AreEqual("αβγ", Greek2020.Match("abc αβγ").Value);
    }

    [TestMethod]
    [DataRow("(?i:a)", "")]
    [DataRow("(?-i:a)", "i")]
    [DataRow("(?s:.)", "u")]
    [DataRow("(?<a>x)|(?<a>y)", "")]
    [DataRow("(?<a>x)|(?<a>y)", "u")]
    [DataRow(@"\p{Script=Yezidi}", "u")]
    [DataRow(@"\p{sc=Vith}", "u")]
    [DataRow(@"\p{Script_Extensions=Toto}", "u")]
    public void TheGrammarOf2020RefusesWhatLaterEditionsAdded(string pattern, string flags)
    {
        RegexFlags f = Regex.ParseFlags(flags);
        Assert.IsFalse(Regex.IsValid(pattern, f, RegexEdition.Es2020, RegexUnicodeVersion.Latest), pattern + " in 2020");
        Assert.IsTrue(Regex.IsValid(pattern, f, RegexEdition.Current, RegexUnicodeVersion.Latest), pattern + " today");
    }

    [TestMethod]
    [DataRow(@"\p{Script=Greek}", "u")]
    [DataRow(@"\p{L}\p{gc=Lu}\p{Lowercase}", "u")]
    [DataRow(@"\P{scx=Latn}", "iu")]
    [DataRow(@"\p{Script=Yezidi}", "")]
    [DataRow(@"(?<a>x)(?<b>y)\k<a>", "u")]
    [DataRow(@"(?<=a)(?<!b)", "s")]
    [DataRow(@"(?:i)", "")]
    public void TheGrammarOf2020AcceptsWhatItHad(string pattern, string flags)
    {
        Assert.IsTrue(Regex.IsValid(pattern, Regex.ParseFlags(flags), RegexEdition.Es2020, RegexUnicodeVersion.V13), pattern);
    }

    [TestMethod]
    [DataRow(RegexFlags.HasIndices)]
    [DataRow(RegexFlags.UnicodeSets)]
    public void TheGrammarOf2020HasNoDOrVFlag(RegexFlags flags)
    {
        Assert.ThrowsExactly<RegexParseException>(() => new Regex("a", flags, Regex.InfiniteMatchTimeout, RegexEdition.Es2020, RegexUnicodeVersion.Default));
        Assert.IsTrue(Regex.IsValid("a", flags, RegexEdition.Current, RegexUnicodeVersion.Default));
    }

    [TestMethod]
    public void ACreateWithATimeoutGivesTheRegexItsTimeoutWithoutThrowing()
    {
        Assert.IsTrue(Regex.TryCreate("a+", RegexFlags.Unicode, TimeSpan.FromMilliseconds(50), out Regex? regex, out string? error));
        Assert.IsNull(error);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), regex!.MatchTimeout);
        Assert.IsFalse(Regex.TryCreate("a{2,1}", RegexFlags.Unicode, TimeSpan.FromMilliseconds(50), out regex, out error));
        Assert.IsNull(regex);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void ACreateWithTheEditionAndVersionRefusesWithoutThrowing()
    {
        Assert.IsFalse(Regex.TryCreate("(?i:a)", RegexFlags.Unicode, TimeSpan.FromSeconds(1), RegexEdition.Es2020, RegexUnicodeVersion.V13, out Regex? regex, out string? error));
        Assert.IsNull(regex);
        Assert.IsNotNull(error);
        Assert.IsTrue(Regex.TryCreate(@"\p{Script=Yezidi}", RegexFlags.Unicode, TimeSpan.FromSeconds(1), RegexEdition.Current, RegexUnicodeVersion.V13, out regex, out error));
        Assert.AreEqual(RegexUnicodeVersion.V13, regex!.UnicodeVersion);
        Assert.AreEqual(TimeSpan.FromSeconds(1), regex.MatchTimeout);
    }

    private static Regex Make(string pattern, RegexFlags flags, RegexUnicodeVersion version) => new(pattern, flags, Regex.InfiniteMatchTimeout, RegexEdition.Current, version);
}
