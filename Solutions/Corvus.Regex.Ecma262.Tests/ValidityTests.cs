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
    [DataRow("(", "")]
    [DataRow("(", "u")]
    [DataRow("(", "v")]
    [DataRow("(?:", "")]
    [DataRow("(?:", "u")]
    [DataRow("(?:", "v")]
    public void GroupsNestTwoHundredDeepAndNoDeeper(string open, string flags)
    {
        // The limit is on the groups: the pattern's own disjunction is not one of them.
        string atLimit = Nest(open, 200);
        (Regex interpreted, Regex generated) = Engines.Both(atLimit, flags);
        Assert.IsTrue(interpreted.IsMatch("hello"), "interpreted: " + open + " " + flags);
        Assert.IsTrue(generated.IsMatch("hello"), "generated: " + open + " " + flags);
        Assert.IsFalse(interpreted.IsMatch("help"), "interpreted: " + open + " " + flags);
        Assert.IsFalse(generated.IsMatch("help"), "generated: " + open + " " + flags);

        RegexPatternTooLargeException e = Assert.ThrowsExactly<RegexPatternTooLargeException>(() => new Regex(Nest(open, 201), flags));
        StringAssert.Contains(e.Message, "nest more than 200 deep");

        static string Nest(string open, int depth) => string.Concat(Enumerable.Repeat(open, depth)) + "hello" + new string(')', depth);
    }

    /// <summary>Runs a test on a thread with a small stack, as a thread of a host may have, and rethrows what it throws.</summary>
    private static void OnASmallStack(Action test, int stack = 256 * 1024)
    {
        Exception? failure = null;
        Thread thread = new(
            () =>
            {
                try
                {
                    test();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            },
            stack);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [TestMethod]
    [DataRow("[", "]", "a", 200, "a")]
    [DataRow("[^", "]", "a", 200, "a")]
    [DataRow("[a--", "]", "[b]", 199, "a")]
    [DataRow("[a&&", "]", "[a]", 199, "a")]
    [DataRow("[\\q{ab|c}", "]", "a", 200, "ab")]
    public void ClassesNestTwoHundredDeepAndNoDeeperOnASmallStack(string open, string close, string body, int depth, string text)
    {
        // A class of the v flag's grammar may hold a class, and the reader goes a level deeper for each, so the classes
        // a class holds count against the limit of nesting as groups do. The deepest the limit allows is read and
        // matched on a thread with a small stack (it is read on a stack of its own), and anything deeper is refused,
        // which a caller can catch, rather than overflowing the stack, which ends the process.
        OnASmallStack(() =>
        {
            foreach (string flags in new[] { "v", "iv" })
            {
                // The body of a difference or an intersection is a class of its own, a level deeper. A negated class
                // of a negated class is the class again: 200 of them hold a.
                Regex atLimit = new(Nest(open, close, body, depth), flags);
                Assert.IsTrue(atLimit.IsMatch(text), open + " " + flags);

                RegexPatternTooLargeException e = Assert.ThrowsExactly<RegexPatternTooLargeException>(() => new Regex(Nest(open, close, body, depth + 1), flags), open + " " + flags);
                StringAssert.Contains(e.Message, "nest more than 200 deep");

                // Far deeper, and open with nothing to close them: refused, by the limit of nesting or by another limit
                // of size that is met first.
                Assert.ThrowsExactly<RegexPatternTooLargeException>(() => new Regex(Nest(open, close, body, 200_000), flags), open + " " + flags);
                Assert.ThrowsExactly<RegexPatternTooLargeException>(() => new Regex(new string('[', 200_000), flags), open + " " + flags);
            }
        });

        static string Nest(string open, string close, string body, int depth) =>
            "[" + string.Concat(Enumerable.Repeat(open, depth)) + body + string.Concat(Enumerable.Repeat(close, depth)) + "]";
    }

    [TestMethod]
    [DataRow("(", "", "<hello>")]
    [DataRow("(?<=", "u", "hello<>")]
    [DataRow("(?:", "v", "<hello>")]
    public void TheDeepestGroupsAreReadOnASmallStack(string open, string flags, string replaced)
    {
        // Two hundred groups, each inside the last, take more stack to read and compile than a small thread has: they
        // are read and compiled on a stack of their own, and so are the program of every group and the automaton
        // that are built from the tree when a match first needs them.
        OnASmallStack(() =>
        {
            Regex r = new(string.Concat(Enumerable.Repeat(open, 200)) + "hello" + new string(')', 200), flags);
            Assert.IsTrue(r.IsMatch("hello"));
            Assert.AreEqual(open == "(" ? 201 : 1, r.Match("hello").Groups.Count);
            Assert.AreEqual(replaced, r.Replace("hello", "<$&>"));
        });
    }

    [TestMethod]
    public void ATooDeepPatternIsRefusedWhereNoThreadOfItsOwnCanReadIt()
    {
        // Where no thread can be started the reader runs on the stack of the thread that asks, and its recursive steps
        // check the stack as they go (DeepStack.Ensure): work that would go deeper than that stack allows is refused as
        // a pattern too large, which a caller can catch, rather than overflowing the stack. How much stack the reader
        // takes for 200 groups depends on the runtime's frames and on the size it gives a thread (200 groups fit in a
        // thread of 64 KB on one runtime and not on another), so the check is driven here by recursion with no end.
        OnASmallStack(() => Assert.ThrowsExactly<RegexPatternTooLargeException>(() => DeepStack.RunHere(() => Descend(0))));

        static int Descend(int depth)
        {
            DeepStack.Ensure();
            return Descend(depth + 1) + 1;
        }
    }

    [TestMethod]
    public void AGreatManyGroupsAreMatchedOnASmallStack()
    {
        // The pattern decides how many groups there are, so their slots are on the heap when there are many: a pattern
        // of 100,000 groups is matched, replaced and split on a thread with a small stack.
        OnASmallStack(() =>
        {
            Regex r = new(string.Concat(Enumerable.Repeat("(a)", 100_000)));
            string text = new('a', 100_000);
            Assert.IsTrue(r.IsMatch(text));
            Assert.AreEqual(100_001, r.Match(text).Groups.Count);
            Assert.AreEqual(100_001, r.Matches(text)[0].Groups.Count);
            Assert.AreEqual(text.Length + 2, r.Replace(text, "<$&>").Length);
            Assert.AreEqual(100_002, r.Split(text).Length);
        });
    }

    [TestMethod]
    public void ManyClassesWithoutTheVFlagAreReadWhereTheyAreAsked()
    {
        // Without the v flag a class holds no class, so a pattern of many classes nests no deeper than one of none: it
        // is read on the stack of the thread that asks, not on a thread of its own, and it compiles and matches on a
        // small stack all the same.
        string classes = string.Concat(Enumerable.Range(0, 400).Select(i => "[a-" + (char)('b' + (i % 20)) + "]"));
        Assert.IsTrue(DeepStack.IsShallow(classes.AsSpan(), unicodeSets: false));
        Assert.IsFalse(DeepStack.IsShallow(classes.AsSpan(), unicodeSets: true));
        OnASmallStack(() =>
        {
            foreach (string flags in new[] { "", "u", "i", "iu" })
            {
                Regex r = new(classes, flags);
                Assert.IsTrue(r.IsMatch(new string('a', 400)), flags);
                Assert.IsFalse(r.IsMatch(new string('a', 399)), flags);
            }
        });
    }

    [TestMethod]
    [DataRow(@"(?<a>x)|(?<a>y)", true, false)]
    [DataRow(@"(?<a>x)|(?<a>y)|(?<a>z)", true, false)]
    [DataRow(@"(?:(?<a>x)|(?<a>y))|(?<a>z)", true, false)]
    [DataRow(@"((?<a>x)|(?<a>y))(?<a>z)", false, false)]
    [DataRow(@"(?:(?<a>x)|y)(?:(?<a>z)|w)", false, false)]
    [DataRow(@"(?:(?<a>x)|(?<b>y))(?<b>z)", false, false)]
    [DataRow(@"(?<a>x)(?:(?<b>y)|(?<a>z))", false, false)]
    [DataRow(@"(?<\u0061>x)\k<a>", true, true)]
    [DataRow(@"(?<\u0061>x)(?<a>y)", false, false)]
    [DataRow(@"\k<a>(?<a>x)", true, true)]
    [DataRow(@"(?<a>x)\k<b>", false, false)]
    public void AGroupNameIsUsedAgainOnlyInAnotherAlternative(string pattern, bool valid, bool validInEs2020)
    {
        Assert.AreEqual(valid, Regex.IsValidPattern(pattern, RegexFlags.Unicode), pattern);
        Assert.AreEqual(validInEs2020, Regex.IsValidPattern(pattern, RegexFlags.Unicode, RegexEdition.Es2020, RegexUnicodeVersion.Default), pattern);
        Assert.AreEqual(valid, Regex.IsValidPattern(System.Text.Encoding.UTF8.GetBytes(pattern), RegexFlags.Unicode), pattern);
    }

    [TestMethod]
    [DataRow("names")]
    [DataRow("names and references")]
    [DataRow("one name in many alternatives")]
    public void ManyGroupNamesAreReadInTimeInProportionToThePattern(string shape)
    {
        // Each name was compared with every name before it, so 40,000 names took seconds where 4,000 took
        // milliseconds. The bound is far above what a linear check takes on a busy machine.
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < 40_000; i++)
        {
            builder.Append(shape switch
            {
                "names" => $"(?<n{i}>a)",
                "names and references" => $"(?<n{i}>a)\\k<n{i}>",
                _ => i == 0 ? "(?<n>a)" : "|(?<n>a)",
            });
        }

        string pattern = builder.ToString();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool valid = Regex.IsValidPattern(pattern, RegexFlags.Unicode);
        stopwatch.Stop();
        Assert.IsTrue(valid, shape);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"{shape}: {pattern.Length:N0} characters took {stopwatch.Elapsed.TotalSeconds:F2} s.");
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
