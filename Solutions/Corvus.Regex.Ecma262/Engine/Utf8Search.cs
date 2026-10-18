// <copyright file="Utf8Search.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Runtime.CompilerServices;
#if NET
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
#endif

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// A literal to search for in UTF-8 text by its rarest byte. The span search looks for a literal by its first and
/// last bytes, and in UTF-8 those are often the bytes every character of a script shares (every Cyrillic letter
/// starts with D0 or D1, every common CJK character with E4 to E9), so it compares at nearly every character. The
/// search here looks for the byte of the literal that English text and UTF-8 make rarest, at its offset, and
/// compares the literal from there.
/// </summary>
internal sealed class Utf8Needle
{
    private static readonly byte[] Rarity = MakeRarity();

    /// <summary>Initializes a new instance of the <see cref="Utf8Needle"/> class.</summary>
    /// <param name="bytes">The literal, which is not empty.</param>
    public Utf8Needle(byte[] bytes)
    {
        this.Bytes = bytes;
        int best = 0;
        for (int k = 1; k < bytes.Length; k++)
        {
            if (Rarity[bytes[k]] > Rarity[bytes[best]])
            {
                best = k;
            }
        }

        this.Offset = best;
        this.Rare = bytes[best];

        // A first character of three or four bytes (CJK, emoji) is searched for as a whole by the span search,
        // whose first-and-last-byte scan then needs its lead and its last continuation byte at a fixed distance,
        // which is rarer than any one byte.
        int lead = bytes[0];
        int firstWidth = lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : 1;
        this.FirstCharacter = firstWidth >= 3 && bytes.Length >= firstWidth ? bytes.AsSpan(0, firstWidth).ToArray() : null;
    }

    /// <summary>Gets the bytes of the first character when it is three or four bytes, or null.</summary>
    public byte[]? FirstCharacter { get; }

    /// <summary>Gets the literal.</summary>
    public byte[] Bytes { get; }

    /// <summary>Gets the rarest byte.</summary>
    public byte Rare { get; }

    /// <summary>Gets the offset of the rarest byte in the literal.</summary>
    public int Offset { get; }

    /// <summary>The first occurrence of the literal at or after a position, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <returns>The position of the occurrence, or -1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(ReadOnlySpan<byte> input, int from)
    {
        byte[] bytes = this.Bytes;
        int n = bytes.Length;
        byte[]? first = this.FirstCharacter;
        if (first is not null)
        {
            int at0 = from;
            while (at0 <= input.Length - n)
            {
                int j = input.Slice(at0, input.Length - at0 - n + first.Length).IndexOf(first);
                if (j < 0)
                {
                    return -1;
                }

                int start0 = at0 + j;
                if (input.Slice(start0, n).SequenceEqual(bytes))
                {
                    return start0;
                }

                at0 = start0 + 1;
            }

            return -1;
        }

        int at = from + this.Offset;
        while (at < input.Length)
        {
            int j = input.Slice(at).IndexOf(this.Rare);
            if (j < 0)
            {
                return -1;
            }

            int start = at + j - this.Offset;
            if (input.Length - start >= n && input.Slice(start, n).SequenceEqual(bytes))
            {
                return start;
            }

            at += j + 1;
        }

        return -1;
    }

    private static byte[] MakeRarity()
    {
        // Higher is rarer. Letters by their frequency in English, capitals rarer than small letters, digits and
        // punctuation rarer still; UTF-8 lead bytes of two and three bytes are the commonest bytes of their
        // scripts, continuation bytes spread over 64 values, four-byte leads are rare.
        byte[] r = new byte[256];
        for (int b = 0; b < 256; b++)
        {
            r[b] = 200;
        }

        const string letters = "etaoinshrdlcumwfgypbvkjxqz";
        for (int k = 0; k < letters.Length; k++)
        {
            r[letters[k]] = (byte)(20 + (k * 5));
            r[char.ToUpperInvariant(letters[k])] = (byte)(90 + (k * 4));
        }

        r[' '] = 10;
        r['\n'] = 60;
        r['\r'] = 60;
        r['.'] = 120;
        r[','] = 110;
        r['\''] = 140;
        for (int b = '0'; b <= '9'; b++)
        {
            r[b] = 150;
        }

        for (int b = 0xC0; b < 0xE0; b++)
        {
            r[b] = 15;
        }

        for (int b = 0xE0; b < 0xF0; b++)
        {
            r[b] = 25;
        }

        for (int b = 0x80; b < 0xC0; b++)
        {
            r[b] = 100;
        }

        for (int b = 0xF0; b < 256; b++)
        {
            r[b] = 220;
        }

        return r;
    }
}

#if NET
/// <summary>
/// A search for an ASCII literal ignoring case in UTF-8 text, vectorised: sixteen bytes at a time, each byte is
/// folded (a letter's case bit cleared) and compared with the literal's first byte, the byte after it with the
/// second, and the full comparison is made where both agree.
/// </summary>
internal static class Utf8CaseSearch
{
    /// <summary>The first occurrence at or after a position of a literal of two bytes or more, ignoring the case of ASCII letters, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="lower">The literal, in lower case.</param>
    /// <returns>The position, or -1.</returns>
    public static int IndexOf(ReadOnlySpan<byte> input, int from, byte[] lower)
    {
        int n = lower.Length;
        int last = input.Length - n;
        if (from > last)
        {
            return -1;
        }

        byte l0 = lower[0];
        byte l1 = lower[1];
        byte m0 = (byte)(l0 >= 'a' && l0 <= 'z' ? 0x20 : 0);
        byte m1 = (byte)(l1 >= 'a' && l1 <= 'z' ? 0x20 : 0);
        int i = from;
        if (Vector128.IsHardwareAccelerated)
        {
            Vector128<byte> v0 = Vector128.Create(l0);
            Vector128<byte> v1 = Vector128.Create(l1);
            Vector128<byte> fold0 = Vector128.Create(m0);
            Vector128<byte> fold1 = Vector128.Create(m1);
            ref byte start = ref MemoryMarshal.GetReference(input);
            for (; i + Vector128<byte>.Count <= last; i += Vector128<byte>.Count)
            {
                Vector128<byte> a = Vector128.LoadUnsafe(ref start, (nuint)i);
                Vector128<byte> b = Vector128.LoadUnsafe(ref start, (nuint)(i + 1));
                uint bits = (Vector128.Equals(a | fold0, v0) & Vector128.Equals(b | fold1, v1)).ExtractMostSignificantBits();
                while (bits != 0)
                {
                    int pos = i + System.Numerics.BitOperations.TrailingZeroCount(bits);
                    if (pos <= last && System.Text.Ascii.EqualsIgnoreCase(input.Slice(pos, n), lower))
                    {
                        return pos;
                    }

                    bits &= bits - 1;
                }
            }
        }

        for (; i <= last; i++)
        {
            if ((input[i] | m0) == l0 && (input[i + 1] | m1) == l1 && System.Text.Ascii.EqualsIgnoreCase(input.Slice(i, n), lower))
            {
                return i;
            }
        }

        return -1;
    }
}
#endif
