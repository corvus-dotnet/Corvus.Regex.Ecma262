// <copyright file="CharSet.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of charset.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself a port of charset.go).

using System;
#if NET10_0_OR_GREATER
using System.Buffers;
#endif
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// An immutable set of code points for the matcher. The ranges are inclusive, sorted, and neither overlap nor
/// touch (the form <see cref="EcmaRegexUnicode"/> makes). The two words of ASCII bits repeat the membership of the
/// code points below 128, so that the common case is one bit test.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class CharSet
{
    private readonly int[] ranges;
    private readonly ulong ascii0;
    private readonly ulong ascii1;
    private readonly int coversAbove128To;
#if NET10_0_OR_GREATER
    private SearchValues<byte>? memberBytes;
    private SearchValues<byte>? nonMemberBytes;
    private SearchValues<char>? memberChars;
    private SearchValues<char>? nonMemberChars;
    private SearchValues<byte>? leadBytes;
    private SearchValues<byte>? trailBytes;
    private SearchValues<char>? allChars;
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

    /// <summary>The most members a set may have for its search values over every member to be built: enumerating them must stay cheap.</summary>
    public const int SmallSet = 512;

    /// <summary>Gets the number of members.</summary>
    public long Count
    {
        get
        {
            long n = 0;
            for (int k = 0; k < this.ranges.Length; k += 2)
            {
                n += this.ranges[k + 1] - this.ranges[k] + 1;
            }

            return n;
        }
    }

    /// <summary>Whether two sets share a character.</summary>
    /// <param name="other">The other set.</param>
    /// <returns>Whether they do.</returns>
    public bool Intersects(CharSet other)
    {
        int[] a = this.ranges;
        int[] b = other.ranges;
        int i = 0;
        int j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i + 1] < b[j])
            {
                i += 2;
            }
            else if (b[j + 1] < a[i])
            {
                j += 2;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets a value indicating whether the set has at most <see cref="SmallSet"/> members.</summary>
    public bool IsSmall
    {
        get
        {
            long n = 0;
            for (int k = 0; k < this.ranges.Length; k += 2)
            {
                n += this.ranges[k + 1] - this.ranges[k] + 1;
                if (n > SmallSet)
                {
                    return false;
                }
            }

            return true;
        }
    }

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

    /// <summary>Gets a value indicating whether the set is every character but a line terminator (<c>.</c> without the s flag), whose runs the matcher scans with the cursor for the rare terminators.</summary>
    public bool IsLineRest => this.ranges.Length == 8 && this.ranges[0] == 0 && this.ranges[1] == 9 && this.ranges[2] == 11 && this.ranges[3] == 12 && this.ranges[4] == 14 && this.ranges[5] == 0x2027 && this.ranges[6] == 0x202A && (this.ranges[7] == 0xFFFF || this.ranges[7] == 0x10FFFF);

#if NET10_0_OR_GREATER
    /// <summary>Gets the ASCII members, for a vectorised search over UTF-8.</summary>
    public SearchValues<byte> MemberBytes => this.memberBytes ??= SearchValues.Create(this.AsciiUnits<byte>(members: true));

    /// <summary>Gets the ASCII characters that are not members, for a vectorised search over UTF-8.</summary>
    public SearchValues<byte> NonMemberBytes => this.nonMemberBytes ??= SearchValues.Create(this.AsciiUnits<byte>(members: false));

    /// <summary>Gets the ASCII members, for a vectorised search over UTF-16.</summary>
    public SearchValues<char> MemberChars => this.memberChars ??= SearchValues.Create(this.AsciiUnits<char>(members: true));

    /// <summary>Gets the ASCII characters that are not members, for a vectorised search over UTF-16.</summary>
    public SearchValues<char> NonMemberChars => this.nonMemberChars ??= SearchValues.Create(this.AsciiUnits<char>(members: false));

    /// <summary>Gets every member of a small set as UTF-16 code units, for a vectorised search over UTF-16 (a member beyond the Basic Multilingual Plane is its high surrogate). Only for a set that <see cref="IsSmall"/>.</summary>
    public SearchValues<char> AllChars
    {
        get
        {
            if (this.allChars is null)
            {
                List<char> chars = [];
                for (int k = 0; k < this.ranges.Length; k += 2)
                {
                    for (int c = this.ranges[k]; c <= this.ranges[k + 1]; c++)
                    {
                        chars.Add(c > 0xFFFF ? (char)(0xD800 + ((c - 0x10000) >> 10)) : (char)c);
                    }
                }

                this.allChars = SearchValues.Create(chars.ToArray());
            }

            return this.allChars;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a small set is searched for over UTF-8 by the last byte of each member's
    /// encoding (see <see cref="TrailBytes"/>) rather than the first: when no member is a surrogate code unit (which
    /// is read inside a four-byte character) or U+FFFD (which any byte that is not UTF-8 reads as).
    /// </summary>
    public bool ScansByTrailByte => !this.Contains(0xFFFD) && !this.IntersectsSurrogates();


    /// <summary>Gets the last UTF-8 byte of every member of a small set, for a vectorised search over UTF-8 that then decodes backwards. Only for a set that <see cref="IsSmall"/> and <see cref="ScansByTrailByte"/>.</summary>
    public SearchValues<byte> TrailBytes
    {
        get
        {
            if (this.trailBytes is null)
            {
                bool[] seen = new bool[256];
                for (int k = 0; k < this.ranges.Length; k += 2)
                {
                    for (int c = this.ranges[k]; c <= this.ranges[k + 1]; c++)
                    {
                        seen[c < 0x80 ? c : 0x80 | (c & 0x3F)] = true;
                    }
                }

                List<byte> bytes = [];
                for (int b = 0; b < 256; b++)
                {
                    if (seen[b])
                    {
                        bytes.Add((byte)b);
                    }
                }

                this.trailBytes = SearchValues.Create(bytes.ToArray());
            }

            return this.trailBytes;
        }
    }

    /// <summary>Gets the first UTF-8 byte of every member of a small set, for a vectorised search over UTF-8. Only for a set that <see cref="IsSmall"/>.</summary>
    public SearchValues<byte> LeadBytes
    {
        get
        {
            if (this.leadBytes is null)
            {
                bool[] seen = new bool[256];
                for (int k = 0; k < this.ranges.Length; k += 2)
                {
                    for (int c = this.ranges[k]; c <= this.ranges[k + 1]; c++)
                    {
                        if (c >= 0xD800 && c <= 0xDBFF)
                        {
                            // A high surrogate is a code unit of a four-byte character, read at its first byte.
                            for (int b = 0xF0; b <= 0xF4; b++)
                            {
                                seen[b] = true;
                            }
                        }
                        else if (c >= 0xDC00 && c <= 0xDFFF)
                        {
                            // A low surrogate is read at the third byte of a four-byte character, a continuation byte.
                            for (int b = 0x80; b < 0xC0; b++)
                            {
                                seen[b] = true;
                            }
                        }
                        else
                        {
                            int lead = c < 0x80 ? c : c < 0x800 ? 0xC0 | (c >> 6) : c < 0x10000 ? 0xE0 | (c >> 12) : 0xF0 | (c >> 18);
                            seen[lead] = true;
                        }
                    }
                }

                // U+FFFD, which a byte that is not UTF-8 decodes to, is a member through any such byte.
                if (this.Contains(0xFFFD))
                {
                    for (int b = 0x80; b < 256; b++)
                    {
                        seen[b] = true;
                    }
                }

                List<byte> bytes = [];
                for (int b = 0; b < 256; b++)
                {
                    if (seen[b])
                    {
                        bytes.Add((byte)b);
                    }
                }

                this.leadBytes = SearchValues.Create(bytes.ToArray());
            }

            return this.leadBytes;
        }
    }

    private bool IntersectsSurrogates()
    {
        for (int k = 0; k < this.ranges.Length; k += 2)
        {
            if (this.ranges[k] <= 0xDFFF && this.ranges[k + 1] >= 0xD800)
            {
                return true;
            }
        }

        return false;
    }

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
