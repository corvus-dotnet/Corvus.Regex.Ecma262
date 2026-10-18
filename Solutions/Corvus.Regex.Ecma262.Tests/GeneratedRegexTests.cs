// <copyright file="GeneratedRegexTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>The generator, used as a consumer uses it: the attribute on partial members of this test class.</summary>
[TestClass]
public partial class GeneratedRegexTests
{
    [GeneratedRegex(@"\b\d{1,2}\/\d{1,2}\/\d{2,4}\b", "gu")]
    private static partial Regex Dates();

    [GeneratedRegex(@"(?<word>\w+)", RegexFlags.Global | RegexFlags.Unicode | RegexFlags.IgnoreCase, 100)]
    private static partial Regex Words { get; }

    [TestMethod]
    public void AGeneratedMethodReturnsOneRegexWithThePatternAndFlags()
    {
        Regex r = Dates();
        Assert.AreSame(r, Dates(), "the same instance each time");
        Assert.AreEqual(@"\b\d{1,2}\/\d{1,2}\/\d{2,4}\b", r.Pattern);
        Assert.AreEqual(RegexFlags.Global | RegexFlags.Unicode, r.Flags);
        Assert.AreEqual(Regex.InfiniteMatchTimeout, r.MatchTimeout);
        Assert.AreEqual(2, r.Count("on 11/18/2019 and 1/2/03"));
        Assert.AreEqual(2, r.Count(Encoding.UTF8.GetBytes("on 11/18/2019 and 1/2/03")));
    }

    [TestMethod]
    public void AGeneratedPropertyReturnsOneRegexWithThePatternFlagsAndTimeout()
    {
        Regex r = Words;
        Assert.AreSame(r, Words);
        Assert.AreEqual(RegexFlags.Global | RegexFlags.Unicode | RegexFlags.IgnoreCase, r.Flags);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), r.MatchTimeout);
        Assert.AreEqual("word", r.GetGroupNames()[0]);
        Assert.AreEqual(3, r.Count("Sherlock Holmes lives"));
    }
}
