// <copyright file="MatchingTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>Answers of IsMatch over both encodings, from the Go engine's TestBacktrackingSemantics, TestModifiers and the hand-written cases.</summary>
[TestClass]
public class MatchingTests
{
    [TestMethod]
    [DataRow("", "", "", true)]
    [DataRow("a", "", "a", true)]
    [DataRow("a", "", "b", false)]
    [DataRow("^a$", "u", "a", true)]
    [DataRow("^a$", "u", "ab", false)]
    [DataRow("^a$", "u", "a\n", false)]
    [DataRow("^a$", "m", "b\na", true)]
    [DataRow("a.c", "", "a\nc", false)]
    [DataRow("a.c", "s", "a\nc", true)]
    [DataRow("a.c", "", "a\u2028c", false)]
    [DataRow("a.c", "u", "a😀c", true)]
    [DataRow("^a.c$", "", "a😀c", false)]
    [DataRow("^a..c$", "", "a😀c", true)]
    [DataRow("^.$", "", "😀", false)]
    [DataRow("^.$", "u", "😀", true)]
    [DataRow("^[^a]$", "u", "😀", true)]
    [DataRow("^[^a]$", "", "😀", false)]
    [DataRow(@"^\u{1F600}$", "u", "😀", true)]
    [DataRow(@"^\ud83d\ude00$", "", "😀", true)]
    [DataRow(@"^\ud83d$", "", "😀", false)]
    [DataRow(@"\ud83d", "", "😀", true)]
    [DataRow(@"^(a|ab)(c|bcd)(d*)$", "", "abcd", true)]
    [DataRow(@"^(?:a|ab)(?:c|bcd)(?:d*)$", "", "abcd", true)]
    [DataRow(@"^(a+)+$", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab", false)]
    [DataRow(@"^(a|aa)+$", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab", false)]
    [DataRow(@"^(?:(?:(?:a*)*)*)*b$", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [DataRow(@"^(a)\1$", "", "aa", true)]
    [DataRow(@"^(a)\1$", "", "ab", false)]
    [DataRow(@"^(?:(a)|b)\1$", "", "b", true)]
    [DataRow(@"^(?:(a)|b)*\1$", "", "ab", true)]
    [DataRow(@"^(?:(a)|b)*\1$", "", "aba", false)]
    [DataRow(@"^(?<x>a)\k<x>$", "u", "aa", true)]
    [DataRow(@"^(?:(?<x>a)|(?<x>b))\k<x>$", "u", "bb", true)]
    [DataRow(@"^(?:(?<x>a)|(?<x>b))\k<x>$", "u", "ba", false)]
    [DataRow(@"(?<=a)b", "", "ab", true)]
    [DataRow(@"(?<=a)b", "", "cb", false)]
    [DataRow(@"(?<!a)b", "", "cb", true)]
    [DataRow(@"(?<=(\w+))\1", "", "abab", true)]
    [DataRow(@"^(?<=(x*?)a*)\1", "", "", true)]
    [DataRow(@"(?=a)a", "", "a", true)]
    [DataRow(@"(?!a)a", "", "a", false)]
    [DataRow(@"\bfoo\b", "", "a foo b", true)]
    [DataRow(@"\bfoo\b", "", "afoob", false)]
    [DataRow(@"\Bfoo\B", "", "afoob", true)]
    [DataRow(@"(?i:k)", "u", "\u212a", true)]
    [DataRow(@"(?i:k)", "", "\u212a", false)]
    [DataRow(@"^(?i:ß)$", "u", "ẞ", true)]
    [DataRow(@"^(?i:ß)$", "", "ẞ", false)]
    [DataRow(@"^(?i:(a)\1)$", "u", "aA", true)]
    [DataRow(@"^(a)(?i:\1)$", "u", "aA", true)]
    [DataRow(@"^(a)\1$", "i", "aA", true)]
    [DataRow(@"^(?i:\w)$", "u", "\u017f", true)]
    [DataRow(@"(?i:\b)", "u", "\u017f", true)]
    [DataRow(@"\p{Script=Greek}", "u", "α", true)]
    [DataRow(@"\p{Script=Greek}", "u", "a", false)]
    [DataRow(@"^\P{ASCII}$", "u", "é", true)]
    [DataRow(@"^(^|x){2}a", "", "xa", true)]
    [DataRow(@"^x(,a$)*$", "", "x,a,a", false)]
    [DataRow(@"^(?:a?){3}$", "", "aa", true)]
    [DataRow(@"^(?:a?){3}$", "", "aaaa", false)]
    [DataRow(@"^(?:|a)+$", "", "aaa", true)]
    [DataRow(@"^(a*?)*b$", "", "aab", true)]
    [DataRow(@"\d+", "", "x12", true)]
    [DataRow(@"^\d+$", "", "١٢", false)]
    [DataRow(@"^\s+$", "", "\u00a0\ufeff\u3000", true)]
    [DataRow(@"^\w+$", "", "abc_1", true)]
    [DataRow(@"^\w+$", "", "é", false)]
    [DataRow(@"^[a-z]{2,4}$", "", "abcde", false)]
    [DataRow(@"^[a-z]{2,4}?$", "", "abcd", true)]
    [DataRow(@"hello", "", "say hello there", true)]
    [DataRow(@"hello|world", "", "say world", true)]
    [DataRow(@"^a{0}$", "", "", true)]
    [DataRow(@"^a{0}$", "", "a", false)]
    [DataRow(@"[\u0000-\uffff]+", "", "😀", true)]
    [DataRow(@"^[\u0000-\uffff]$", "u", "😀", false)]
    [DataRow(@"^\c_$", "", "\\c_", true)]
    [DataRow(@"^[\c_]$", "", "\u001f", true)]
    [DataRow(@"^\8$", "", "8", true)]
    [DataRow(@"^\101$", "", "A", true)]
    public void IsMatchAgreesWithEcma262OverBothEncodings(string pattern, string flags, string text, bool expected)
    {
        Regex r = new(pattern, flags);
        Assert.AreEqual(expected, r.IsMatch(text), $"/{pattern}/{flags} on {Show(text)} (UTF-16)");
        Assert.AreEqual(expected, r.IsMatch(Encoding.UTF8.GetBytes(text)), $"/{pattern}/{flags} on {Show(text)} (UTF-8)");
        Assert.AreEqual(expected, r.IsMatch(text.AsSpan()), $"/{pattern}/{flags} on {Show(text)} (span)");
    }

    [TestMethod]
    public void TheMemoryOfFailedPlacesGivesTheSameAnswers()
    {
        MatchState.MemoFromTheStart = true;
        try
        {
            Regex r = new(@"^(?:(?:(?:a*)*)*)*b$");
            Assert.IsFalse(r.IsMatch(new string('a', 64)));
            Assert.IsTrue(r.IsMatch(new string('a', 64) + "b"));
            Regex nested = new(@"^(a|aa)+$");
            Assert.IsTrue(nested.IsMatch(new string('a', 100)));
            Assert.IsFalse(nested.IsMatch(new string('a', 100) + "c"));
        }
        finally
        {
            MatchState.MemoFromTheStart = false;
        }
    }

    [TestMethod]
    public void StickyMatchesOnlyAtTheStartPosition()
    {
        Regex r = new("b", RegexFlags.Sticky);
        Assert.IsFalse(r.IsMatch("ab"));
        Assert.IsTrue(r.IsMatch("ab", 1));
        Assert.IsTrue(r.IsMatch("b"));
    }

    [TestMethod]
    public void AMatchIsAbandonedAtItsTimeout()
    {
        Regex r = new(@"^(?:a|a|a)*(?:(a)\1)*c$", RegexFlags.None, TimeSpan.FromMilliseconds(50));
        string text = new string('a', 40) + "b";
        RegexMatchTimeoutException e = Assert.ThrowsExactly<RegexMatchTimeoutException>(() => r.IsMatch(text));
        Assert.IsFalse(e.ExceededMemory);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), e.MatchTimeout);
    }

    [TestMethod]
    public void AMatchIsAbandonedAtItsMemoryLimit()
    {
        Regex r = new(@"^(?:a?){2147483647}(b)\1$", RegexFlags.None, TimeSpan.FromSeconds(30));
        RegexMatchTimeoutException e = Assert.ThrowsExactly<RegexMatchTimeoutException>(() => r.IsMatch("c"));
        Assert.IsTrue(e.ExceededMemory);
    }

    [TestMethod]
    public void TheDefaultMatchTimeoutIsTheAppContextDataSystemTextRegularExpressionsReads()
    {
        Assert.AreEqual(Regex.InfiniteMatchTimeout, Regex.ReadDefaultMatchTimeout(null));
        Assert.AreEqual(TimeSpan.FromSeconds(2), Regex.ReadDefaultMatchTimeout(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(Regex.InfiniteMatchTimeout, Regex.ReadDefaultMatchTimeout(Regex.InfiniteMatchTimeout));
        Assert.ThrowsExactly<InvalidCastException>(() => Regex.ReadDefaultMatchTimeout("00:00:01"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Regex.ReadDefaultMatchTimeout(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Regex.ReadDefaultMatchTimeout(TimeSpan.FromDays(30)));

        // The tests set none, so every constructor without a timeout gives no timeout.
        Assert.AreEqual(Regex.InfiniteMatchTimeout, Regex.DefaultMatchTimeout);
        Assert.AreEqual(Regex.DefaultMatchTimeout, new Regex("a").MatchTimeout);
        Assert.AreEqual(Regex.DefaultMatchTimeout, new Regex("a", RegexFlags.Global).MatchTimeout);
        Assert.AreEqual(Regex.DefaultMatchTimeout, new Regex("a", "g").MatchTimeout);
    }

    [TestMethod]
    public void AMatchOfAPatternWithoutABackreferenceIsAbandonedAtItsTimeout()
    {
        Regex r = new(@"^(?:a|a)*(?:aa)*c$", RegexFlags.None, TimeSpan.FromMilliseconds(1));
        string text = new string('a', 100000) + "b";

        // An answer, or the timeout; it is never a wrong answer.
        try
        {
            Assert.IsFalse(r.IsMatch(text));
        }
        catch (RegexMatchTimeoutException e)
        {
            Assert.IsFalse(e.ExceededMemory);
        }
    }

    [TestMethod]
    public void AMatchOfAPatternWithoutABackreferenceIsAbandonedAtItsMemoryLimit()
    {
        // No backreference, so the memory of failed places keeps the time in proportion to the pattern's size times
        // the text; but a count is not size, and the timeout and its cap on the memory still apply.
        Regex r = new(@"^(?:a?){2147483647}$", RegexFlags.None, TimeSpan.FromSeconds(30));
        RegexMatchTimeoutException e = Assert.ThrowsExactly<RegexMatchTimeoutException>(() => r.IsMatch(string.Empty));
        Assert.IsTrue(e.ExceededMemory);
        e = Assert.ThrowsExactly<RegexMatchTimeoutException>(() => r.IsMatch(ReadOnlySpan<byte>.Empty));
        Assert.IsTrue(e.ExceededMemory);
        e = Assert.ThrowsExactly<RegexMatchTimeoutException>(() => r.Split(string.Empty));
        Assert.IsTrue(e.ExceededMemory);
    }

    [TestMethod]
    public void ALoneSurrogateInUtf16IsMatchedAsEcma262Says()
    {
        Assert.IsTrue(new Regex(@"^\ud800$").IsMatch("\ud800"));
        Assert.IsTrue(new Regex(@"^\ud800$", RegexFlags.Unicode).IsMatch("\ud800"));
        Assert.IsTrue(new Regex(@"^.$", RegexFlags.Unicode).IsMatch("\ud800"));
        Assert.IsTrue(new Regex(@"^[^a]$").IsMatch("\udc00"));
        Assert.IsFalse(new Regex(@"^\ud800\udc00$", RegexFlags.Unicode).IsMatch("\ud800"));
    }

    private static string Show(string s)
    {
        StringBuilder sb = new();
        foreach (char c in s)
        {
            if (c < 0x20 || c > 0x7e)
            {
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
