// <copyright file="TeddyTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

#if NET
using System.Runtime.InteropServices;
using Corvus.Ecma262.RegularExpressions;

namespace Corvus.Ecma262.RegularExpressions.Tests;

/// <summary>
/// The packed search of <c>Teddy</c> over UTF-16 finds what trying every position finds: the earliest start of one
/// of the literals, and the length of the first in their order there. It narrows thirty-two units at a step with
/// AVX2 and sixteen with AdvSimd or SSE2; CI runs these on x86-64 as it is, with <c>DOTNET_EnableAVX2=0</c>, and on
/// arm64, so each narrowing is tested.
/// </summary>
[TestClass]
public class TeddyTests
{
    // Units around every edge of the narrowing: ASCII, the byte boundary, the sign bit of a short, the forms of k
    // and s beyond ASCII, and the largest unit.
    private static readonly char[] Alphabet =
    [
        'a', 'k', 'K', 's', 'S', 't', '\u212A', '\u017F', '\0', '\u00FF', '\u0100', '\u7FFF', '\u8000',
        '\u8073', '\uD83D', '\uFFFF',
    ];

    [TestMethod]
    public void PackedFindsWhatEveryPositionFinds()
    {
        if (!Teddy<ushort>.PackedSupported)
        {
            Assert.Inconclusive("The packed search is not available on this machine.");
        }

        Needle<ushort>[][] sets =
        [
            [Exact("ask"), Exact("tas")],
            [Exact("at"), Exact("sa"), Exact("kt"), Exact("aaa")],
            [Folded("sk"), Exact("ta")],
            [Folded("kas"), Folded("st"), Exact("aks")],
            [Exact("\0a"), Folded("ss")],
        ];
        Random random = new(20261028);
        int checks = 0;
        foreach (Needle<ushort>[] needles in sets)
        {
            Teddy<ushort> teddy = new(needles, packed: true);
            for (int t = 0; t < 400; t++)
            {
                char[] text = new char[random.Next(0, 140)];
                for (int k = 0; k < text.Length; k++)
                {
                    text[k] = Alphabet[random.Next(Alphabet.Length)];
                }

                ReadOnlySpan<ushort> input = MemoryMarshal.Cast<char, ushort>(text);
                for (int from = 0; from <= text.Length; from += 1 + random.Next(9))
                {
                    int want = Earliest(needles, input, from, out int wantLength);
                    int got = teddy.IndexOf(input, from, out int gotLength);
                    Assert.AreEqual(want, got, $"start in \"{Show(text)}\" from {from}");
                    Assert.AreEqual(wantLength, gotLength, $"length in \"{Show(text)}\" from {from}");
                    checks++;
                }
            }
        }

        Assert.IsGreaterThan(10_000, checks);
    }

    private static Needle<ushort> Exact(string s) => new(s.Select(c => (ushort)c).ToArray());

    // A literal under the i and u flags: k has the forms k, K and the Kelvin sign; s has s, S and the long s.
    private static Needle<ushort> Folded(string s)
    {
        static ushort[] Forms(char c) => c switch
        {
            'k' => ['k', 'K', '\u212A'],
            's' => ['s', 'S', '\u017F'],
            _ => [char.ToLowerInvariant(c), char.ToUpperInvariant(c)],
        };

        ushort[][] forms = s.Select(Forms).ToArray();
        int rows = forms.Max(f => f.Length);
        ushort[][] table = new ushort[rows][];
        for (int r = 0; r < rows; r++)
        {
            table[r] = forms.Select(f => f[Math.Min(r, f.Length - 1)]).ToArray();
        }

        return new(table, Enumerable.Range(0, s.Length + 1).ToArray());
    }

    private static int Earliest(Needle<ushort>[] needles, ReadOnlySpan<ushort> input, int from, out int length)
    {
        int min = needles.Min(n => n.Length);
        for (int p = from; p <= input.Length - min; p++)
        {
            foreach (Needle<ushort> needle in needles)
            {
                if (input.Length - p >= needle.Length && needle.Verify(input, p))
                {
                    length = needle.Length;
                    return p;
                }
            }
        }

        length = -1;
        return -1;
    }

    private static string Show(char[] text) => string.Concat(text.Select(c => c < 0x80 && c >= 0x20 ? c.ToString() : $"\\u{(int)c:X4}"));
}
#endif