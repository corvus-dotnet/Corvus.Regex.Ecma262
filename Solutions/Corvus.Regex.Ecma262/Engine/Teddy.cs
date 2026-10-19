// <copyright file="Teddy.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

#if NET
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The search for any of several literals over UTF-8 by fingerprints (Teddy, as in Hyperscan and the aho-corasick
/// crate): the literals are put in up to eight buckets, and for each of up to three positions two tables of sixteen
/// entries map the low and the high nibble of a byte to the buckets that have a literal with that byte there, in
/// any of its forms. A vector of starts is looked up with two shuffles per position, and the starts whose bytes
/// agree with some bucket at every position are compared against its literals. The positions are the rarest the
/// literals share, so that beyond ASCII (where the lead bytes of a script are alike) the bytes that tell the
/// literals apart are the ones looked at.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class Teddy
{
    /// <summary>The most literals a search takes.</summary>
    public const int MostLiterals = 32;

    private const int Buckets = 8;
    private const int MostPositions = 3;

    private readonly Needle<byte>[] needles;
    private readonly int[][] buckets;
    private readonly int[] positions;
    private readonly Vector128<byte>[] lo;
    private readonly Vector128<byte>[] hi;
    private readonly int minLength;
    private readonly int reach;

    /// <summary>Initializes a new instance of the <see cref="Teddy"/> class.</summary>
    /// <param name="needles">The literals, at most <see cref="MostLiterals"/>, each at least two bytes long.</param>
    public Teddy(Needle<byte>[] needles)
    {
        this.needles = needles;
        int n = needles.Length;
        this.minLength = int.MaxValue;
        foreach (Needle<byte> needle in needles)
        {
            this.minLength = Math.Min(this.minLength, needle.Length);
        }

        // The share of text each position the literals share lets through, and the rarest three of them.
        double[] share = new double[this.minLength];
        Span<byte> forms = stackalloc byte[Needle<byte>.MaxRows];
        for (int d = 0; d < this.minLength; d++)
        {
            List<byte> seen = [];
            foreach (Needle<byte> needle in needles)
            {
                int count = needle.UnitsAt(d, forms);
                for (int f = 0; f < count; f++)
                {
                    if (!seen.Contains(forms[f]))
                    {
                        seen.Add(forms[f]);
                        share[d] += (255 - UnitRarity.Of(forms[f])) / 1000.0;
                    }
                }
            }
        }

        int[] order = new int[this.minLength];
        for (int d = 0; d < order.Length; d++)
        {
            order[d] = d;
        }

        Array.Sort(order, (a, b) => share[a] != share[b] ? share[a].CompareTo(share[b]) : a.CompareTo(b));
        this.positions = order.AsSpan(0, Math.Min(MostPositions, order.Length)).ToArray();
        Array.Sort(this.positions);
        this.reach = this.positions[this.positions.Length - 1];

        // A literal's bucket, and the tables: the bit of the bucket at the nibbles of each of its forms' bytes.
        this.buckets = new int[Math.Min(Buckets, n)][];
        List<int>[] members = new List<int>[this.buckets.Length];
        for (int b = 0; b < members.Length; b++)
        {
            members[b] = [];
        }

        for (int k = 0; k < n; k++)
        {
            members[k % members.Length].Add(k);
        }

        for (int b = 0; b < members.Length; b++)
        {
            this.buckets[b] = [.. members[b]];
        }

        this.lo = new Vector128<byte>[this.positions.Length];
        this.hi = new Vector128<byte>[this.positions.Length];
        Span<byte> loTable = stackalloc byte[16];
        Span<byte> hiTable = stackalloc byte[16];
        for (int j = 0; j < this.positions.Length; j++)
        {
            loTable.Clear();
            hiTable.Clear();
            for (int b = 0; b < this.buckets.Length; b++)
            {
                foreach (int k in this.buckets[b])
                {
                    int count = needles[k].UnitsAt(this.positions[j], forms);
                    for (int f = 0; f < count; f++)
                    {
                        loTable[forms[f] & 0xF] |= (byte)(1 << b);
                        hiTable[forms[f] >> 4] |= (byte)(1 << b);
                    }
                }
            }

            this.lo[j] = Vector128.Create(loTable);
            this.hi[j] = Vector128.Create(hiTable);
        }
    }

    /// <summary>Gets a value indicating whether the search is vectorised on this machine.</summary>
    public static bool Supported => Vector128.IsHardwareAccelerated;

    /// <summary>The earliest position at or after a position where one of the literals is, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <returns>The position, or -1.</returns>
    public int IndexOf(ReadOnlySpan<byte> input, int from)
    {
        int last = input.Length - this.minLength;
        if (from > last)
        {
            return -1;
        }

        int i = from;
        if (Avx2.IsSupported && Vector256.IsHardwareAccelerated)
        {
            int found = this.Scan256(input, ref i);
            if (found >= 0)
            {
                return found;
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            int found = this.Scan128(input, ref i);
            if (found >= 0)
            {
                return found;
            }
        }

        for (; i <= last; i++)
        {
            if (this.VerifyAny(input, i))
            {
                return i;
            }
        }

        return -1;
    }

    private int Scan256(ReadOnlySpan<byte> input, ref int i)
    {
        int lastVector = input.Length - Vector256<byte>.Count - this.reach;
        if (i > lastVector)
        {
            return -1;
        }

        ref byte s = ref MemoryMarshal.GetReference(input);
        Vector256<byte> nibble = Vector256.Create((byte)0x0F);
        Vector256<byte> lo0 = Vector256.Create(this.lo[0], this.lo[0]);
        Vector256<byte> hi0 = Vector256.Create(this.hi[0], this.hi[0]);
        Vector256<byte> lo1 = this.positions.Length > 1 ? Vector256.Create(this.lo[1], this.lo[1]) : default;
        Vector256<byte> hi1 = this.positions.Length > 1 ? Vector256.Create(this.hi[1], this.hi[1]) : default;
        Vector256<byte> lo2 = this.positions.Length > 2 ? Vector256.Create(this.lo[2], this.lo[2]) : default;
        Vector256<byte> hi2 = this.positions.Length > 2 ? Vector256.Create(this.hi[2], this.hi[2]) : default;
        int p0 = this.positions[0];
        int p1 = this.positions.Length > 1 ? this.positions[1] : 0;
        int p2 = this.positions.Length > 2 ? this.positions[2] : 0;
        int count = this.positions.Length;
        Span<byte> hits = stackalloc byte[Vector256<byte>.Count];
        for (; i <= lastVector; i += Vector256<byte>.Count)
        {
            Vector256<byte> c = Vector256.LoadUnsafe(ref s, (nuint)(i + p0));
            Vector256<byte> r = Avx2.Shuffle(lo0, c & nibble) & Avx2.Shuffle(hi0, Vector256.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);
            if (count > 1)
            {
                c = Vector256.LoadUnsafe(ref s, (nuint)(i + p1));
                r &= Avx2.Shuffle(lo1, c & nibble) & Avx2.Shuffle(hi1, Vector256.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);
                if (count > 2)
                {
                    c = Vector256.LoadUnsafe(ref s, (nuint)(i + p2));
                    r &= Avx2.Shuffle(lo2, c & nibble) & Avx2.Shuffle(hi2, Vector256.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);
                }
            }

            if (r == Vector256<byte>.Zero)
            {
                continue;
            }

            r.CopyTo(hits);
            uint lanes = (~Vector256.Equals(r, Vector256<byte>.Zero)).ExtractMostSignificantBits();
            int found = this.Candidates(input, i, lanes, hits);
            if (found >= 0)
            {
                return found;
            }
        }

        return -1;
    }

    private int Scan128(ReadOnlySpan<byte> input, ref int i)
    {
        int lastVector = input.Length - Vector128<byte>.Count - this.reach;
        if (i > lastVector)
        {
            return -1;
        }

        ref byte s = ref MemoryMarshal.GetReference(input);
        Vector128<byte> nibble = Vector128.Create((byte)0x0F);
        Vector128<byte> lo0 = this.lo[0];
        Vector128<byte> hi0 = this.hi[0];
        Vector128<byte> lo1 = this.positions.Length > 1 ? this.lo[1] : default;
        Vector128<byte> hi1 = this.positions.Length > 1 ? this.hi[1] : default;
        Vector128<byte> lo2 = this.positions.Length > 2 ? this.lo[2] : default;
        Vector128<byte> hi2 = this.positions.Length > 2 ? this.hi[2] : default;
        int p0 = this.positions[0];
        int p1 = this.positions.Length > 1 ? this.positions[1] : 0;
        int p2 = this.positions.Length > 2 ? this.positions[2] : 0;
        int count = this.positions.Length;
        Span<byte> hits = stackalloc byte[Vector128<byte>.Count];
        for (; i <= lastVector; i += Vector128<byte>.Count)
        {
            Vector128<byte> c = Vector128.LoadUnsafe(ref s, (nuint)(i + p0));
            Vector128<byte> r = Vector128.ShuffleNative(lo0, c & nibble) & Vector128.ShuffleNative(hi0, Vector128.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);
            if (count > 1)
            {
                c = Vector128.LoadUnsafe(ref s, (nuint)(i + p1));
                r &= Vector128.ShuffleNative(lo1, c & nibble) & Vector128.ShuffleNative(hi1, Vector128.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);
                if (count > 2)
                {
                    c = Vector128.LoadUnsafe(ref s, (nuint)(i + p2));
                    r &= Vector128.ShuffleNative(lo2, c & nibble) & Vector128.ShuffleNative(hi2, Vector128.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);
                }
            }

            if (r == Vector128<byte>.Zero)
            {
                continue;
            }

            r.CopyTo(hits);
            uint lanes = (~Vector128.Equals(r, Vector128<byte>.Zero)).ExtractMostSignificantBits();
            int found = this.Candidates(input, i, lanes, hits);
            if (found >= 0)
            {
                return found;
            }
        }

        return -1;
    }

    /// <summary>The earliest start among a vector's candidates at which one of the literals of a bucket its bytes agree with is, or -1.</summary>
    private int Candidates(ReadOnlySpan<byte> input, int i, uint lanes, ReadOnlySpan<byte> hits)
    {
        int last = input.Length - this.minLength;
        while (lanes != 0)
        {
            int lane = BitOperations.TrailingZeroCount(lanes);
            lanes &= lanes - 1;
            int start = i + lane;
            if (start > last)
            {
                return -1;
            }

            int bits = hits[lane];
            while (bits != 0)
            {
                int b = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                foreach (int k in this.buckets[b])
                {
                    Needle<byte> needle = this.needles[k];
                    if (input.Length - start >= needle.Length && needle.Verify(input, start))
                    {
                        return start;
                    }
                }
            }
        }

        return -1;
    }

    private bool VerifyAny(ReadOnlySpan<byte> input, int start)
    {
        foreach (Needle<byte> needle in this.needles)
        {
            if (input.Length - start >= needle.Length && needle.Verify(input, start))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
