// <copyright file="AllocationTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// A match allocates nothing once the per-thread working memory has grown. The count is of the measuring thread
/// alone, so that the harness's own threads do not disturb it.
/// </summary>
[TestClass]
public class AllocationTests
{
    [TestMethod]
    [DataRow(@"^[a-z]+@[a-z]+\.[a-z]+$", "", "someone@example.com", true)]
    [DataRow(@"^[a-z]+@[a-z]+\.[a-z]+$", "", "someone@example.c0m", false)]
    [DataRow(@"^(a|aa)+$", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [DataRow(@"^(a|aa)+$", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab", false)]
    [DataRow(@"(?<=\d)([a-z]+)\1?", "u", "the 42nd of the month", true)]
    [DataRow(@"^(?<w>\w+)\s\k<w>$", "iu", "Hello hello", true)]
    [DataRow(@"\p{Script=Greek}+", "u", "the letters αβγ in the middle", true)]
    [DataRow(@"^(?:(?:(?:a*)*)*)*b$", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    public void AMatchAllocatesNothingOnceWarm(string pattern, string flags, string text, bool expected)
    {
        Regex r = new(pattern, flags);
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        for (int k = 0; k < 50; k++)
        {
            Assert.AreEqual(expected, r.IsMatch(text));
            Assert.AreEqual(expected, r.IsMatch(utf8));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 1000; k++)
        {
            if (r.IsMatch(text) != expected || r.IsMatch(utf8) != expected)
            {
                Assert.Fail("The answer changed.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0, allocated, $"/{pattern}/{flags} allocated {allocated} bytes over 2000 matches");
    }
}
