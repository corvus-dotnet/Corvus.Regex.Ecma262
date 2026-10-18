// <copyright file="CharSet.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of charset.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself a port of charset.go).

using System;
#if NET
using System.Buffers;
#endif
using System.Runtime.CompilerServices;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// An immutable set of code points for the matcher. The ranges are inclusive, sorted, and neither overlap nor
/// touch (the form <see cref="EcmaRegexUnicode"/> makes). The two words of ASCII bits repeat the membership of the
/// code points below 128, so that the common case is one bit test.
/// </summary>
internal sealed class CharSet
{
    private readonly int[] ranges;
    private readonly ulong ascii0;
    private readonly ulong ascii1;
    private readonly int coversAbove128To;
#if NET
    private SearchValues<byte>? memberBytes;
    private SearchValues<byte>? nonMemberBytes;
    private SearchValues<char>? memberChars;
    private SearchValues<char>? nonMemberChars;
#endif

    /// <summary>Initializes a new instance of the <see cref="CharSet"/> class.</summary>
    /// <param name="ranges">The ranges, in the normal form.</param>
    public CharSet(int[] ranges)
    {
        this.ranges = ranges;
        for (int k = 0; k < ranges.Length; k += 2)
        {
            int lo = ranges[k];
            if (lo >= 128)
            {
                break;
            }

            int hi = Math.Min(ranges[k + 1], 127);
            for (int c = lo; c <= hi; c++)
            {
                if (c < 64)
                {
                    this.ascii0 |= 1UL << c;
                }
                else
                {
                    this.ascii1 |= 1UL << (c - 64);
                }
            }
        }

        this.coversAbove128To = -1;
        for (int k = 0; k < ranges.Length; k += 2)
        {
            if (ranges[k] <= 128 && ranges[k + 1] >= 128)
            {
                this.coversAbove128To = ranges[k + 1];
                break;
            }
        }
    }

    /// <summary>Gets the ranges.</summary>
    public int[] Ranges => this.ranges;

    /// <summary>Gets the membership of the code points 0 to 63, one bit each.</summary>
    public ulong Ascii0 => this.ascii0;

    /// <summary>Gets the membership of the code points 64 to 127, one bit each.</summary>
    public ulong Ascii1 => this.ascii1;

    /// <summary>Gets a value indicating whether the set holds no code point.</summary>
    public bool IsEmpty => this.ranges.Length == 0;

    /// <summary>Gets a value indicating whether the set holds a code point of 128 or more.</summary>
    public bool HasNonAscii => this.ranges.Length > 0 && this.ranges[this.ranges.Length - 1] >= 128;

    /// <summary>
    /// Gets the last code point of the range that holds 128, when one does, or -1. A set whose range from 128
    /// reaches the last character of the grammar (U+10FFFF by code point, U+FFFF by code unit) holds every
    /// character that is not ASCII, so a scan for the end of a run of its members need only look at ASCII.
    /// </summary>
    public int CoversAbove128To => this.coversAbove128To;

    /// <summary>Whether the set holds every character of 128 and above that the mode can read.</summary>
    /// <param name="units">Whether the text is matched by UTF-16 code unit.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HoldsAllNonAscii(bool units) => this.coversAbove128To >= (units ? 0xFFFF : 0x10FFFF);

#if NET
    /// <summary>Gets the ASCII members, for a vectorised search over UTF-8.</summary>
    public SearchValues<byte> MemberBytes => this.memberBytes ??= SearchValues.Create(this.AsciiUnits<byte>(members: true));

    /// <summary>Gets the ASCII characters that are not members, for a vectorised search over UTF-8.</summary>
    public SearchValues<byte> NonMemberBytes => this.nonMemberBytes ??= SearchValues.Create(this.AsciiUnits<byte>(members: false));

    /// <summary>Gets the ASCII members, for a vectorised search over UTF-16.</summary>
    public SearchValues<char> MemberChars => this.memberChars ??= SearchValues.Create(this.AsciiUnits<char>(members: true));

    /// <summary>Gets the ASCII characters that are not members, for a vectorised search over UTF-16.</summary>
    public SearchValues<char> NonMemberChars => this.nonMemberChars ??= SearchValues.Create(this.AsciiUnits<char>(members: false));

    private TUnit[] AsciiUnits<TUnit>(bool members)
        where TUnit : unmanaged
    {
        int n = 0;
        for (int c = 0; c < 128; c++)
        {
            if (this.HasAscii(c) == members)
            {
                n++;
            }
        }

        TUnit[] units = new TUnit[n];
        int k = 0;
        for (int c = 0; c < 128; c++)
        {
            if (this.HasAscii(c) == members)
            {
                units[k++] = typeof(TUnit) == typeof(byte) ? (TUnit)(object)(byte)c : (TUnit)(object)(char)c;
            }
        }

        return units;
    }
#endif

    /// <summary>Whether the set holds a character below 128.</summary>
    /// <param name="c">The character, which must be below 128.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasAscii(int c)
    {
        return c < 64 ? ((this.ascii0 >> c) & 1) != 0 : ((this.ascii1 >> (c - 64)) & 1) != 0;
    }

    /// <summary>Whether the set holds the code point.</summary>
    /// <param name="r">The code point.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int r)
    {
        if ((uint)r < 128)
        {
            return this.HasAscii(r);
        }

        return this.ContainsSlow(r);
    }

    private bool ContainsSlow(int r)
    {
        int[] ranges = this.ranges;
        int lo = 0;
        int hi = ranges.Length / 2;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (r > ranges[(2 * mid) + 1])
            {
                lo = mid + 1;
            }
            else if (r < ranges[2 * mid])
            {
                hi = mid;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
