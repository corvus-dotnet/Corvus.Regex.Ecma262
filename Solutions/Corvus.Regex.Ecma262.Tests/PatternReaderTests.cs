// <copyright file="PatternReaderTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// <see cref="Regex.IsValidPattern(ReadOnlySpan{char}, RegexFlags, RegexEdition, RegexUnicodeVersion)"/>, the reader of
/// the grammar that allocates nothing: it answers as the parser does wherever the parser is not stopped by a limit of
/// size, over UTF-16 and UTF-8, and it allocates nothing and cannot overflow the stack. (The data-set runner holds it
/// to V8's verdict on every pattern of the test data.)
/// </summary>
[TestClass]
public class PatternReaderTests
{
    private const string B = "\\";

    private static readonly string[] Patterns =
    [
        string.Empty, "a", "^a$", "a|b", "(a)", "(?:a)", "(?=a)", "(?!a)", "(?<=a)", "(?<!a)", "(?=a)*", "(?<=a)*",
        "a{2}", "a{2,}", "a{2,3}", "a{3,2}", "a{", "a{,2}", "{", "}", "]", "[", "[]", "[^]", "[a-z]", "[z-a]",
        B + "d-z", "[" + B + "d-z]", "[a-" + B + "d]", "[" + B + "b]", "[" + B + "-]", B + "-", B + "a", B + "c",
        B + "cA", "[" + B + "c1]", B + "0", B + "01", B + "1", "(a)" + B + "1", "(a)" + B + "2", B + "x4", B + "x41",
        B + "u004", B + "u" + "0041", B + "u{1F600}", B + "u{110000}", "\U0001F600", "[\U0001F600-\U0001F64F]",
        "[\U0001F64F-\U0001F600]", B + "p{L}", B + "P{Lu}", B + "p{Script=Latin}", B + "p{sc=Hrkt}", B + "p{Foo}",
        B + "p{}", B + "p", B + "k", B + "k<a>", B + "k<a>(?<a>x)", "(?<a>x)" + B + "k<a>", "(?<a>x)" + B + "k<b>",
        "(?<a>x)(?<a>y)", "(?<a>x)|(?<a>y)", "(?:(?<a>x)|(?<a>y))" + B + "k<a>", "((?<a>x)|y)(?<a>z)",
        "(?<" + B + "u" + "0061>x)" + B + "k<a>", "(?<a" + B + "u" + "{62}>x)" + B + "k<ab>", "(?<1a>x)", "(?<>x)",
        "(?<a", "(?i:a)", "(?ims:a)", "(?i-m:a)", "(?-i:a)", "(?ii:a)", "(?i-i:a)", "(?-:a)", "(?i)a", "(?x:a)",
        "(?<=a)+", "(?!a)+", B + "b+", "a**", "a+?", "a??", "*", "+a", "a)", "(a", "((a)", ")", "a|", "|", "a||b",
        "[a&&b]", "[a--b]", "[[a-z]--[aeiou]]", "[[a-z]&&[a-m]]", "[a--b&&c]", "[a-z--b]", "[" + B + "q{abc}]",
        "[^" + B + "q{abc}]", "[^" + B + "q{a}]", "[" + B + "q{}]", "[^" + B + "q{a|}]", "[" + B + "p{RGI_Emoji}]",
        "[^" + B + "p{RGI_Emoji}]", B + "P{RGI_Emoji}", B + "p{RGI_Emoji}", "[[" + B + "q{ab}]--a]", "[^[" + B + "q{ab}]--a]",
        "[^[" + B + "q{ab}]&&a]", "[a&&&b]", "[!!]", "[" + B + "!]", "[(]", "[a-]", "[-a]", "[[]]", "[^[^a]]", "[a[b]c]",
        "[" + B + "d-a]", "[a-" + B + "d]", "[a-" + B + "q{b}]", "[a-[b]]",
    ];

    private static readonly RegexFlags[] Grammars = [RegexFlags.None, RegexFlags.Unicode, RegexFlags.UnicodeSets, RegexFlags.Unicode | RegexFlags.IgnoreCase];

    [TestMethod]
    public void TheReaderAnswersAsTheParserDoes()
    {
        List<string> wrong = [];
        foreach (string pattern in Patterns)
        {
            foreach (RegexFlags flags in Grammars)
            {
                foreach (RegexEdition edition in new[] { RegexEdition.Current, RegexEdition.Es2020 })
                {
                    bool expected = Regex.TryCreate(pattern, flags, Regex.InfiniteMatchTimeout, edition, RegexUnicodeVersion.Default, out _, out _);
                    bool utf16 = Regex.IsValidPattern(pattern.AsSpan(), flags, edition, RegexUnicodeVersion.Default);
                    bool utf8 = Regex.IsValidPattern(Encoding.UTF8.GetBytes(pattern), flags, edition, RegexUnicodeVersion.Default);
                    if (utf16 != expected || utf8 != expected)
                    {
                        wrong.Add($"/{pattern}/ {flags} {edition}: the parser {expected}, the reader {utf16} (UTF-16) {utf8} (UTF-8)");
                    }
                }
            }
        }

        Assert.AreEqual(0, wrong.Count, string.Join("\n", wrong));
    }

    [TestMethod]
    public void TheReaderAllocatesNothing()
    {
        string deep = new string('(', 300) + "a" + new string(')', 300);
        string names = string.Concat(Enumerable.Range(0, 60).Select(k => "(?<n" + k + ">a)|"));
        (string Pattern, RegexFlags Flags)[] cases =
        [
            ("^[a-z][a-z0-9_]{0,29}$", RegexFlags.Unicode), (B + "p{Script=Greek}+" + B + "P{Lu}", RegexFlags.Unicode),
            ("(?<word>" + B + "w+)" + B + "s+" + B + "k<word>", RegexFlags.Unicode), (deep, RegexFlags.Unicode), (names + "x", RegexFlags.Unicode),
            ("[[a-z]--[aeiou]&&" + B + "q{ab|c}]", RegexFlags.UnicodeSets), ("[^" + B + "q{abc}]", RegexFlags.UnicodeSets),
            ("(?i:k)" + B + "u{1F600}", RegexFlags.Unicode), ("a{2,1}", RegexFlags.None), (B + "c1" + B + "01" + B + "k", RegexFlags.None),
        ];

        byte[][] utf8 = cases.Select(c => Encoding.UTF8.GetBytes(c.Pattern)).ToArray();

        // Once to warm up: the tables of Unicode and of group names are made on first use, and the pool's arrays.
        bool any = false;
        for (int k = 0; k < cases.Length; k++)
        {
            any ^= Regex.IsValidPattern(cases[k].Pattern.AsSpan(), cases[k].Flags) ^ Regex.IsValidPattern(utf8[k], cases[k].Flags);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 100; round++)
        {
            for (int k = 0; k < cases.Length; k++)
            {
                any ^= Regex.IsValidPattern(cases[k].Pattern.AsSpan(), cases[k].Flags) ^ Regex.IsValidPattern(utf8[k], cases[k].Flags);
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0, allocated, any.ToString());
    }

    [TestMethod]
    public void ADeeplyNestedPatternIsReadWithoutOverflowingTheStack()
    {
        string groups = new string('(', 200_000) + "a" + new string(')', 200_000);
        Assert.IsTrue(Regex.IsValidPattern(groups.AsSpan(), RegexFlags.Unicode));
        Assert.IsFalse(Regex.IsValidPattern((groups + ")").AsSpan(), RegexFlags.Unicode));

        string classes = new string('[', 200_000) + "a" + new string(']', 200_000);
        Assert.IsTrue(Regex.IsValidPattern(classes.AsSpan(), RegexFlags.UnicodeSets));
        Assert.IsFalse(Regex.IsValidPattern((classes + "]").AsSpan(), RegexFlags.UnicodeSets));
    }

    [TestMethod]
    public void APatternOverTheEnginesLimitsIsAValidPattern()
    {
        // Nested past the 200 the engine compiles, and longer than the 1,048,576 characters it reads.
        string nested = new string('(', 201) + "a" + new string(')', 201);
        Assert.IsFalse(Regex.IsValid(nested, RegexFlags.Unicode));
        Assert.IsTrue(Regex.IsValidPattern(nested.AsSpan(), RegexFlags.Unicode));

        string longer = new('a', 1_100_000);
        Assert.IsFalse(Regex.IsValid(longer, RegexFlags.Unicode));
        Assert.IsTrue(Regex.IsValidPattern(longer.AsSpan(), RegexFlags.Unicode));
    }

    [TestMethod]
    public void BytesThatAreNotUtf8AreNotAPattern()
    {
        Assert.IsFalse(Regex.IsValidPattern(new byte[] { (byte)'a', 0xC0, 0x80 }, RegexFlags.Unicode));
        Assert.IsFalse(Regex.IsValidPattern(new byte[] { 0xED, 0xA0, 0x80 }, RegexFlags.None));
        Assert.IsTrue(Regex.IsValidPattern("a"u8, RegexFlags.Unicode));
    }

    [TestMethod]
    public void SettingsThatDoNotGoTogetherAreNotValid()
    {
        Assert.IsFalse(Regex.IsValidPattern("a".AsSpan(), RegexFlags.Unicode | RegexFlags.UnicodeSets));
        Assert.IsFalse(Regex.IsValidPattern("a".AsSpan(), RegexFlags.UnicodeSets, RegexEdition.Es2020, RegexUnicodeVersion.Default));
        Assert.IsFalse(Regex.IsValidPattern("a".AsSpan(), RegexFlags.HasIndices, RegexEdition.Es2020, RegexUnicodeVersion.Default));
        Assert.IsFalse(Regex.IsValidPattern("a".AsSpan(), (RegexFlags)0x1000));
        Assert.IsFalse(Regex.IsValidPattern("a".AsSpan(), RegexFlags.None, (RegexEdition)9, RegexUnicodeVersion.Default));
        Assert.IsFalse(Regex.IsValidPattern("a".AsSpan(), RegexFlags.None, RegexEdition.Current, (RegexUnicodeVersion)99));
    }
}
