// <copyright file="SpanMembersTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.Text;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The members that match, replace and split without allocating give the answers of the allocating members,
/// over both encodings, and allocate nothing once warm.
/// </summary>
[TestClass]
public class SpanMembersTests
{
    private const string Lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer posuere eget magna a consectetur. Integer a libero volutpat, tempus lectus ac, aliquam enim. Ut orci felis, efficitur molestie posuere id, eleifend vel urna. Vestibulum nec mi suscipit, tempor purus at, convallis ligula. Sed auctor lobortis semper. Nulla facilisi, ſemper.";

    [TestMethod]
    [DataRow(@"(\w+)\s(\w+)", "gu", "the quick brown fox jumps over")]
    [DataRow(@"(?<first>\w)(\w*)", "giu", "Lorem ipsum ſit")]
    [DataRow(@"(a)|(b)", "gu", "xaxbx")]
    [DataRow(@"", "gu", "ab")]
    [DataRow(@"x*", "gu", "axxb")]
    [DataRow(@"(\d+)-(\d+)", "u", "1-2 3-4")]
    public void EnumerateMatchesWithCapturesAgreesWithMatches(string pattern, string flags, string text)
    {
        Regex r = new(pattern, flags);
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        List<(int, int, string?[])> expected = [];
        foreach (Match m in r.Matches(text))
        {
            string?[] groups = new string?[r.GroupCount + 1];
            for (int g = 0; g <= r.GroupCount; g++)
            {
                groups[g] = m.Groups[g].Success ? m.Groups[g].Value : null;
            }

            expected.Add((m.Index, m.Length, groups));
        }

        List<(int, int, string?[])> got = [];
        Span<ValueCapture> captures = stackalloc ValueCapture[r.GroupCount + 1];
        foreach (ValueMatch m in r.EnumerateMatches(text.AsSpan(), captures))
        {
            string?[] groups = new string?[r.GroupCount + 1];
            for (int g = 0; g <= r.GroupCount; g++)
            {
                groups[g] = captures[g].Success ? text.Substring(captures[g].Index, captures[g].Length) : null;
            }

            got.Add((m.Index, m.Length, groups));
        }

        CollectionAssert.AreEqual(expected.Select(e => $"{e.Item1}:{e.Item2}:{string.Join("|", e.Item3)}").ToList(), got.Select(e => $"{e.Item1}:{e.Item2}:{string.Join("|", e.Item3)}").ToList(), "UTF-16");

        List<string> got8 = [];
        foreach (ValueMatch m in r.EnumerateMatches(utf8.AsSpan(), captures))
        {
            string?[] groups = new string?[r.GroupCount + 1];
            for (int g = 0; g <= r.GroupCount; g++)
            {
                groups[g] = captures[g].Success ? Encoding.UTF8.GetString(utf8, captures[g].Index, captures[g].Length) : null;
            }

            got8.Add($"{Encoding.UTF8.GetString(utf8, m.Index, m.Length)}:{string.Join("|", groups)}");
        }

        CollectionAssert.AreEqual(expected.Select(e => $"{text.Substring(e.Item1, e.Item2)}:{string.Join("|", e.Item3)}").ToList(), got8, "UTF-8");
    }

    [TestMethod]
    [DataRow(@"tempus|magna|semper", "gu", "amoveatur")]
    [DataRow(@"(\w+)\s(\w+)", "gu", "$2 $1")]
    [DataRow(@"(?<first>\w)(?<rest>\w*)", "gu", "<$<rest>|$<first>|$<none>>")]
    [DataRow(@"a", "gu", "[$&][$`][$']")]
    [DataRow(@"(a)|(b)", "gu", "$2$1$$$3$0$99")]
    [DataRow(@"(\w)(\w)(\w)(\w)(\w)(\w)(\w)(\w)(\w)(\w)(\w)", "gu", "$11-$1$10")]
    [DataRow(@"", "gu", "-")]
    [DataRow(@"x*", "gu", "-")]
    [DataRow(@"ipsum", "u", "IPSUM")]
    [DataRow(@"nothing here", "gu", "x")]
    [DataRow(@"ſemper", "giu", "s")]
    public void TryReplaceAgreesWithReplace(string pattern, string flags, string replacement)
    {
        Regex r = new(pattern, flags);
        string expected = r.Replace(Lorem, replacement);
        char[] buffer = new char[expected.Length + 64];
        Assert.IsTrue(r.TryReplace(Lorem.AsSpan(), replacement.AsSpan(), buffer, out int written));
        Assert.AreEqual(expected, new string(buffer, 0, written), "UTF-16 span");

        ArrayBufferWriter<char> writer = new();
        r.Replace(Lorem.AsSpan(), replacement.AsSpan(), writer);
        Assert.AreEqual(expected, writer.WrittenSpan.ToString(), "UTF-16 writer");

        byte[] utf8 = Encoding.UTF8.GetBytes(Lorem);
        byte[] out8 = new byte[Encoding.UTF8.GetByteCount(expected) + 64];
        Assert.IsTrue(r.TryReplace(utf8, Encoding.UTF8.GetBytes(replacement), out8, out int written8));
        Assert.AreEqual(expected, Encoding.UTF8.GetString(out8, 0, written8), "UTF-8 span");

        ArrayBufferWriter<byte> writer8 = new();
        r.Replace(utf8, Encoding.UTF8.GetBytes(replacement), writer8);
        Assert.AreEqual(expected, Encoding.UTF8.GetString(writer8.WrittenSpan), "UTF-8 writer");

        Assert.IsFalse(r.TryReplace(Lorem.AsSpan(), replacement.AsSpan(), buffer.AsSpan(0, 3), out _), "too short");
    }

    [TestMethod]
    public void ReplaceWithASpanEvaluatorWritesWhatTheEvaluatorGives()
    {
        Regex r = new(@"(\w+)\s(\w+)", "gu");
        string expected = r.Replace("the quick brown fox jumps", m => m.Groups[2].Value.ToUpperInvariant() + "/" + m.Groups[1].Value);
        ArrayBufferWriter<char> writer = new();
        r.Replace("the quick brown fox jumps".AsSpan(), (ReadOnlySpan<char> input, ValueMatch match, ReadOnlySpan<ValueCapture> captures, IBufferWriter<char> destination) =>
        {
            ReadOnlySpan<char> second = input.Slice(captures[2].Index, captures[2].Length);
            Span<char> span = destination.GetSpan(second.Length + 1 + captures[1].Length);
            second.ToUpperInvariant(span);
            span[second.Length] = '/';
            input.Slice(captures[1].Index, captures[1].Length).CopyTo(span.Slice(second.Length + 1));
            destination.Advance(second.Length + 1 + captures[1].Length);
        }, writer);
        Assert.AreEqual(expected, writer.WrittenSpan.ToString());

        ArrayBufferWriter<byte> writer8 = new();
        r.Replace(Encoding.UTF8.GetBytes("the quick brown fox jumps"), (ReadOnlySpan<byte> input, ValueMatch match, ReadOnlySpan<ValueCapture> captures, IBufferWriter<byte> destination) =>
        {
            ReadOnlySpan<byte> second = input.Slice(captures[2].Index, captures[2].Length);
            Span<byte> span = destination.GetSpan(second.Length + 1 + captures[1].Length);
            for (int k = 0; k < second.Length; k++)
            {
                span[k] = (byte)char.ToUpperInvariant((char)second[k]);
            }

            span[second.Length] = (byte)'/';
            input.Slice(captures[1].Index, captures[1].Length).CopyTo(span.Slice(second.Length + 1));
            destination.Advance(second.Length + 1 + captures[1].Length);
        }, writer8);
        Assert.AreEqual(expected, Encoding.UTF8.GetString(writer8.WrittenSpan));
    }

    [TestMethod]
    [DataRow(@"tempus|magna|semper", "gu", "Lorem tempus ipsum magna dolor semper", int.MaxValue)]
    [DataRow(@"\s+", "u", "  a b  c ", int.MaxValue)]
    [DataRow(@"", "u", "abc", int.MaxValue)]
    [DataRow(@"x*", "u", "axxbx", int.MaxValue)]
    [DataRow(@"(,)", "u", "a,b,c", int.MaxValue)]
    [DataRow(@",", "u", "a,b,c", 2)]
    [DataRow(@",", "u", "a,b,c", 0)]
    [DataRow(@",", "u", "", int.MaxValue)]
    [DataRow(@".*", "u", "", int.MaxValue)]
    [DataRow(@"b", "yu", "abc", int.MaxValue)]
    [DataRow(@"\u{1F600}", "u", "a😀b😀", int.MaxValue)]
    [DataRow(@"(?:)", "", "😀", int.MaxValue)]
    [DataRow(@"(?:)", "u", "😀", int.MaxValue)]
    public void EnumerateSplitsGivesThePiecesOfSplitWithoutTheCaptures(string pattern, string flags, string text, int count)
    {
        Regex r = new(pattern, flags);
        Regex plain = new(pattern == "(,)" ? "(?:,)" : pattern, flags);
        string?[] pieces = plain.Split(text, count);
        List<string> got = [];
        foreach (ValueMatch piece in r.EnumerateSplits(text.AsSpan(), count))
        {
            got.Add(text.Substring(piece.Index, piece.Length));
        }

        CollectionAssert.AreEqual(pieces, got, "UTF-16");

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        List<string> got8 = [];
        foreach (ValueMatch piece in r.EnumerateSplits(utf8.AsSpan(), count))
        {
            got8.Add(Encoding.UTF8.GetString(utf8, piece.Index, piece.Length));
        }

        if (text.IndexOf('\ud83d') < 0 || flags.Contains('u'))
        {
            CollectionAssert.AreEqual(pieces, got8, "UTF-8");
        }
    }

    [TestMethod]
    public void SplitStillPutsTheCapturesBetweenThePieces()
    {
        Regex r = new(@"(,)|(;)", "u");
        CollectionAssert.AreEqual(new string?[] { "a", ",", null, "b", null, ";", "c" }, r.Split("a,b;c"));
        CollectionAssert.AreEqual(new string?[] { "a", ",", null, "b" }, r.Split("a,b;c", 4));
    }

    [TestMethod]
    public void TheSpanMembersAllocateNothingOnceWarm()
    {
        Regex words = new(@"tempus|magna|semper", "gu");
        Regex groups = new(@"(\w+)\s(\w+)", "gu");
        byte[] utf8 = Encoding.UTF8.GetBytes(Lorem);
        char[] buffer = new char[Lorem.Length * 2];
        byte[] buffer8 = new byte[utf8.Length * 2];
        FixedWriter<char> writer = new(buffer);
        FixedWriter<byte> writer8 = new(buffer8);
        Span<ValueCapture> captures = stackalloc ValueCapture[groups.GroupCount + 1];
        for (int k = 0; k < 50; k++)
        {
            Work(words, groups, utf8, buffer, buffer8, writer, writer8, captures);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 200; k++)
        {
            Work(words, groups, utf8, buffer, buffer8, writer, writer8, captures);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0, allocated, $"allocated {allocated} bytes");

        static void Work(Regex words, Regex groups, byte[] utf8, char[] buffer, byte[] buffer8, FixedWriter<char> writer, FixedWriter<byte> writer8, Span<ValueCapture> captures)
        {
            int n = 0;
            foreach (ValueMatch m in groups.EnumerateMatches(Lorem.AsSpan(), captures))
            {
                n += captures[1].Length;
            }

            foreach (ValueMatch m in groups.EnumerateMatches(utf8.AsSpan(), captures))
            {
                n += captures[2].Length;
            }

            if (!words.TryReplace(Lorem.AsSpan(), "amoveatur".AsSpan(), buffer, out int written) || !words.TryReplace(utf8, "amoveatur"u8, buffer8, out int written8))
            {
                throw new InvalidOperationException("did not fit");
            }

            writer.Reset();
            words.Replace(Lorem.AsSpan(), "amoveatur".AsSpan(), writer);
            writer8.Reset();
            words.Replace(utf8, "amoveatur"u8, writer8);
            foreach (ValueMatch piece in words.EnumerateSplits(Lorem.AsSpan()))
            {
                n += piece.Length;
            }

            foreach (ValueMatch piece in words.EnumerateSplits(utf8.AsSpan()))
            {
                n += piece.Length;
            }

            if (n == 0 || written == 0 || written8 == 0)
            {
                throw new InvalidOperationException("nothing happened");
            }
        }
    }

    /// <summary>A writer over a fixed array, for the allocation test.</summary>
    private sealed class FixedWriter<T>(T[] buffer) : IBufferWriter<T>
    {
        private int written;

        public void Reset() => this.written = 0;

        public void Advance(int count) => this.written += count;

        public Memory<T> GetMemory(int sizeHint = 0) => buffer.AsMemory(this.written);

        public Span<T> GetSpan(int sizeHint = 0) => buffer.AsSpan(this.written);
    }
}
