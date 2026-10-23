// <copyright file="Utf8CheckTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The members that take UTF-8 refuse bytes that are not UTF-8 with an <see cref="ArgumentException"/> before they read
/// any, and their <c>Unchecked</c> twins read such bytes as U+FFFD one at a time.
/// </summary>
[TestClass]
public class Utf8CheckTests
{
    // The ill-formed sequences of Table 3-7 of the Unicode Standard, after a byte that matches "a".
    private static readonly byte[][] IllFormed =
    [
        [0x61, 0x80],
        [0x61, 0xC0, 0x80],
        [0x61, 0xC1, 0xBF],
        [0x61, 0xE0, 0x9F, 0xBF],
        [0x61, 0xED, 0xA0, 0x80],
        [0x61, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80],
        [0x61, 0xF0, 0x8F, 0xBF, 0xBF],
        [0x61, 0xF4, 0x90, 0x80, 0x80],
        [0x61, 0xF5],
        [0x61, 0xFF],
        [0x61, 0xC3],
        [0x61, 0xE2, 0x82],
        [0x61, 0xF0, 0x9F, 0x98],
        [0x61, 0xF0, 0x9F, 0x98, 0x41],
    ];

    [TestMethod]
    public void EveryMemberThatTakesUtf8RefusesBytesThatAreNotUtf8()
    {
        Regex re = new("a", "g");
        Regex b = new("b");
        Span<ValueCapture> captures = new ValueCapture[re.GroupCount + 1];
        foreach (byte[] text in IllFormed)
        {
            string what = BitConverter.ToString(text);
            Assert.ThrowsExactly<ArgumentException>(() => re.IsMatch(text), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.IsMatch(text, 0), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.Count(text), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.EnumerateMatches(text), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.EnumerateMatches(text, 0), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.EnumerateMatches(text, new ValueCapture[1]), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.EnumerateMatches(text, 0, new ValueCapture[1]), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.TryMatch(text, 0, new ValueCapture[1], out _), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.TryReplace(text, "x"u8, new byte[64], out _), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.Replace(text, "x"u8, new ArrayBufferWriter<byte>()), what);
            Assert.ThrowsExactly<ArgumentException>(() => re.Replace(text, (_, _, _, d) => d.Write("x"u8), new ArrayBufferWriter<byte>()), what);
            Assert.ThrowsExactly<ArgumentException>(() => b.EnumerateSplits(text), what);
            Assert.ThrowsExactly<ArgumentException>(() => b.EnumerateSplits(text, 2), what);
        }

        // A replacement that is not UTF-8 is refused too.
        Assert.ThrowsExactly<ArgumentException>(() => re.TryReplace("a"u8, [0xFF], new byte[64], out _));
        Assert.ThrowsExactly<ArgumentException>(() => re.Replace("a"u8, [0xFF], new ArrayBufferWriter<byte>()));
    }

    [TestMethod]
    public void TheUncheckedMembersReadBytesThatAreNotUtf8AsReplacementCharacters()
    {
        byte[] text = [0x61, 0xFF, 0x62];
        Regex fffd = new("\\uFFFD", "gu");
        Assert.IsTrue(fffd.IsMatchUnchecked(text));
        Assert.IsTrue(fffd.IsMatchUnchecked(text, 0));
        Assert.AreEqual(1, fffd.CountUnchecked(text));
        Assert.IsTrue(fffd.TryMatchUnchecked(text, 0, new ValueCapture[1], out ValueMatch m));
        Assert.AreEqual(1, m.Index);
        Assert.AreEqual(1, m.Length);
        int n = 0;
        foreach (ValueMatch _ in fffd.EnumerateMatchesUnchecked(text))
        {
            n++;
        }

        Assert.AreEqual(1, n);
        byte[] destination = new byte[16];
        Assert.IsTrue(fffd.TryReplaceUnchecked(text, "-"u8, destination, out int written));
        CollectionAssert.AreEqual("a-b"u8.ToArray(), destination.AsSpan(0, written).ToArray());
        ArrayBufferWriter<byte> writer = new();
        fffd.ReplaceUnchecked(text, "-"u8, writer);
        CollectionAssert.AreEqual("a-b"u8.ToArray(), writer.WrittenSpan.ToArray());
        n = 0;
        foreach (ValueMatch _ in fffd.EnumerateSplitsUnchecked(text))
        {
            n++;
        }

        Assert.AreEqual(2, n);
    }

    [TestMethod]
    public void TheCheckOfTheOlderFrameworksIsTheStandardsTable()
    {
        // Every sequence of up to three bytes, and the four-byte ones around each boundary, judged as Encoding.UTF8
        // with exceptions judges them (which follows Table 3-7).
        UTF8Encoding strict = new(false, true);
        Random random = new(20261023);
        for (int t = 0; t < 200_000; t++)
        {
            byte[] s = new byte[random.Next(1, 6)];
            for (int k = 0; k < s.Length; k++)
            {
                s[k] = (byte)(random.Next(3) == 0 ? random.Next(256) : new[] { 0x41, 0x80, 0x8F, 0x90, 0x9F, 0xA0, 0xBF, 0xC0, 0xC2, 0xDF, 0xE0, 0xED, 0xEF, 0xF0, 0xF4, 0xF5 }[random.Next(16)]);
            }

            bool valid;
            try
            {
                strict.GetCharCount(s);
                valid = true;
            }
            catch (DecoderFallbackException)
            {
                valid = false;
            }

            Assert.AreEqual(valid, Utf8Check.FirstInvalid(s) < 0, BitConverter.ToString(s));
        }

        Assert.AreEqual(1, Utf8Check.FirstInvalid([0x61, 0xFF, 0x62]));
        Assert.AreEqual(0, Utf8Check.FirstInvalid([0xE2, 0x82]));
        Assert.AreEqual(-1, Utf8Check.FirstInvalid("aé€\U0001F600"u8));
    }
}