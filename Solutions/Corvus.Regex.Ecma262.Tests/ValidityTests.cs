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
        // Where no thread can be started the reader runs on the stack of the thread that asks, and refuses a pattern
        // too deep for that stack, rather than overflowing it.
        string deep = string.Concat(Enumerable.Repeat("(?:", 200)) + "a" + new string(')', 200);
        OnASmallStack(
            () => Assert.ThrowsExactly<RegexPatternTooLargeException>(() => DeepStack.RunHere(() => new EcmaRegexParser(deep.AsSpan(), unicode: true).Parse())),
            stack: 64 * 1024);
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
