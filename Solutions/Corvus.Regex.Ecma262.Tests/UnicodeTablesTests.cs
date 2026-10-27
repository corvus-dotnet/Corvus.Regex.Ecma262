// <copyright file="UnicodeTablesTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>The Unicode tables decode and hold what the property escapes and case folding need.</summary>
[TestClass]
public class UnicodeTablesTests
{
    [TestMethod]
    [DataRow("L", 'a', true)]
    [DataRow("L", '1', false)]
    [DataRow("Letter", 'Z', true)]
    [DataRow("Nd", '7', true)]
    [DataRow("Script=Greek", 'α', true)]
    [DataRow("sc=Grek", 'a', false)]
    [DataRow("ASCII", 'a', true)]
    [DataRow("ASCII", 'é', false)]
    public void APropertyEscapeNamesItsSet(string property, char c, bool member)
    {
        int[]? set = UnicodeTables.V17.Property(property.AsSpan(), out _);
        Assert.IsNotNull(set, property);
        Assert.AreEqual(member, EcmaRegexUnicode.Contains(set, c), $"\\p{{{property}}} and '{c}'");
    }

    [TestMethod]
    public void AnUnknownPropertyIsNoSet()
    {
        Assert.IsNull(UnicodeTables.V17.Property("Nope".AsSpan(), out _));
    }

    [TestMethod]
    public void SimpleCaseFoldingJoinsTheKelvinSignToK()
    {
        int[] closure = UnicodeTables.V17.FoldClosure([0x4B, 0x4B], unicode: true);
        Assert.IsTrue(EcmaRegexUnicode.Contains(closure, 0x6B), "k");
        Assert.IsTrue(EcmaRegexUnicode.Contains(closure, 0x212A), "Kelvin sign");
    }

    [TestMethod]
    public void TheCanonicalizeOfAPatternWithNoFlagLeavesTheKelvinSignAlone()
    {
        int[] closure = UnicodeTables.V17.FoldClosure([0x4B, 0x4B], unicode: false);
        Assert.IsTrue(EcmaRegexUnicode.Contains(closure, 0x6B), "k");
        Assert.IsFalse(EcmaRegexUnicode.Contains(closure, 0x212A), "Kelvin sign");
    }
}
