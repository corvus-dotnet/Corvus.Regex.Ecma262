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
/// The search for any of several literals by fingerprints (Teddy, as in Hyperscan and the aho-corasick crate): the
/// literals are put in up to eight buckets, and for each of up to three positions two tables of sixteen entries
/// map the low and the high nibble of a byte to the buckets that have a literal with that byte there, in any of
/// its forms. A vector of starts is looked up with two shuffles per position, and the starts whose bytes agree
/// with some bucket at every position are compared against its literals. The positions are the rarest the
/// literals share, so that beyond ASCII (where the lead bytes of a script are alike) the bytes that tell the
/// literals apart are the ones looked at. Over UTF-16 the byte looked at is the low byte of the unit, and only
/// the lanes at units are starts; every form of a letter is in the tables, so nothing is searched for apart.
/// </summary>
/// <remarks>
/// <para>
/// Teddy is Geoff Langdale's, from Intel's Hyperscan (<c>src/fdr/teddy.c</c> in
/// <see href="https://github.com/intel/hyperscan"/>), which the Rust crate aho-corasick ports. The README of that port,
/// <see href="https://github.com/BurntSushi/aho-corasick/blob/master/src/packed/teddy/README.md"/>, describes it in
/// full, with the masks worked through and the papers it rests on.
/// </para>
/// <para>
/// The loop over the vectors with no start in them is its own method for each number of positions, with no call
/// in it: the tables then stay in registers, where a loop that also handles the starts reloads them at every
/// vector (the three-name search measured 61 us against 75 with one loop). A start is compared against a
/// literal's forms with one vector compare per form where the literal fits a vector and every character of it
/// is one unit in every form.
/// </para>
/// </remarks>
/// <typeparam name="T">The unit: byte for UTF-8, ushort for UTF-16.</typeparam>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class Teddy<T>
    where T : unmanaged, IEquatable<T>
{
    /// <summary>The most literals a search takes.</summary>
    public const int MostLiterals = 32;

    private const int Buckets = 8;
    private const int MostPositions = 3;

    /// <summary>The cost of a position, per byte of text, in the units of a vector operation: two shuffles, a shift and two ands over thirty-two bytes.</summary>
    private const double PositionCost = 6.0 / 32;

    /// <summary>
    /// The cost of a start the fingerprints let through, in the same units: the lanes, the buckets and the literals
    /// compared, measured at about five nanoseconds against five thousandths per byte and position (seven names
    /// searched by one position took twice the time of three; five Russian names by two, two and a half times).
    /// </summary>
    private const double StartCost = 200;

    private readonly Needle<T>[] needles;
    private readonly int[][] buckets;
    private readonly int[] positions;
    private readonly Vector128<byte>[] lo;
    private readonly Vector128<byte>[] hi;
    private readonly Vector256<byte> lo0;
    private readonly Vector256<byte> hi0;
    private readonly Vector256<byte> lo1;
    private readonly Vector256<byte> hi1;
    private readonly Vector256<byte> lo2;
    private readonly Vector256<byte> hi2;
    private readonly int p0;
    private readonly int p1;
    private readonly int p2;
    private readonly int count;
    private readonly int minLength;
    private readonly int reach;
    private readonly bool packed;

    /// <summary>For each literal, each of its forms as the bytes of a vector (the unit bytes in order, zero beyond the literal), or null where the literal is compared by its needle.</summary>
    private readonly Vector256<byte>[]?[] forms;

    /// <summary>For each literal, the bits of the bytes of a vector it covers.</summary>
    private readonly uint[] formMask;

    /// <summary>Initializes a new instance of the <see cref="Teddy{T}"/> class.</summary>
    /// <param name="needles">The literals, at most <see cref="MostLiterals"/>, each at least two units long.</param>
    /// <param name="packed">Over UTF-16, whether to narrow thirty-two units to bytes at each step (a unit beyond 255 to 255, which is then a bucket of its own) rather than look at the low byte of sixteen: for literals that are ASCII in their own form, with any forms beyond it. Needs AVX2.</param>
    public Teddy(Needle<T>[] needles, bool packed = false)
    {
        this.needles = needles;
        this.packed = packed && typeof(T) == typeof(ushort);
        int n = needles.Length;
        this.minLength = int.MaxValue;
        foreach (Needle<T> needle in needles)
        {
            this.minLength = Math.Min(this.minLength, needle.Length);
        }

        // The share of text each position the literals share lets through, and the rarest three of them.
        double[] share = new double[this.minLength];
        Span<T> units = stackalloc T[Needle<T>.MaxRows];
        for (int d = 0; d < this.minLength; d++)
        {
            List<T> seen = [];
            foreach (Needle<T> needle in needles)
            {
                int c = needle.UnitsAt(d, units);
                for (int f = 0; f < c; f++)
                {
                    if (!seen.Contains(units[f]))
                    {
                        seen.Add(units[f]);
                        share[d] += (255 - UnitRarity.Of(units[f])) / 1000.0;
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

        // As many of them as pay: each costs its shuffles at every vector, and saves the starts it turns away.
        int take = 1;
        double best = double.MaxValue;
        double through = 1;
        for (int k = 1; k <= Math.Min(MostPositions, order.Length); k++)
        {
            through *= share[order[k - 1]];
            double c = (k * PositionCost) + (through * StartCost);
            if (c < best)
            {
                best = c;
                take = k;
            }
        }

        this.positions = order.AsSpan(0, take).ToArray();
        Array.Sort(this.positions);
        this.count = this.positions.Length;
        this.reach = this.positions[this.count - 1];
        this.p0 = this.positions[0];
        this.p1 = this.count > 1 ? this.positions[1] : 0;
        this.p2 = this.count > 2 ? this.positions[2] : 0;

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

        this.lo = new Vector128<byte>[this.count];
        this.hi = new Vector128<byte>[this.count];
        Span<byte> loTable = stackalloc byte[16];
        Span<byte> hiTable = stackalloc byte[16];
        for (int j = 0; j < this.count; j++)
        {
            loTable.Clear();
            hiTable.Clear();
            for (int b = 0; b < this.buckets.Length; b++)
            {
                foreach (int k in this.buckets[b])
                {
                    int c = needles[k].UnitsAt(this.positions[j], units);
                    for (int f = 0; f < c; f++)
                    {
                        int low = this.packed ? Math.Min(Unit(units[f]), 0xFF) : Unit(units[f]) & 0xFF;
                        loTable[low & 0xF] |= (byte)(1 << b);
                        hiTable[low >> 4] |= (byte)(1 << b);
                    }
                }
            }

            this.lo[j] = Vector128.Create(loTable);
            this.hi[j] = Vector128.Create(hiTable);
        }

        this.lo0 = Vector256.Create(this.lo[0], this.lo[0]);
        this.hi0 = Vector256.Create(this.hi[0], this.hi[0]);
        this.lo1 = this.count > 1 ? Vector256.Create(this.lo[1], this.lo[1]) : default;
        this.hi1 = this.count > 1 ? Vector256.Create(this.hi[1], this.hi[1]) : default;
        this.lo2 = this.count > 2 ? Vector256.Create(this.lo[2], this.lo[2]) : default;
        this.hi2 = this.count > 2 ? Vector256.Create(this.hi[2], this.hi[2]) : default;

        // Each literal's forms as vectors, where every character is one unit in every form (so that a unit
        // being one of the forms' units at its position is the character being one of its forms) and the
        // literal fits a vector.
        this.forms = new Vector256<byte>[]?[n];
        this.formMask = new uint[n];
        Span<byte> bytes = stackalloc byte[Vector256<byte>.Count];
        for (int k = 0; k < n; k++)
        {
            Needle<T> needle = needles[k];
            int width = needle.Length * Stride;
            if (!Vector256.IsHardwareAccelerated || !needle.Positionwise || width > Vector256<byte>.Count)
            {
                continue;
            }

            T[][] rows = needle.Rows;
            Vector256<byte>[] vectors = new Vector256<byte>[rows.Length];
            for (int f = 0; f < rows.Length; f++)
            {
                bytes.Clear();
                MemoryMarshal.AsBytes(rows[f].AsSpan()).CopyTo(bytes);
                vectors[f] = Vector256.Create(bytes);
            }

            this.forms[k] = vectors;
            this.formMask[k] = width == Vector256<byte>.Count ? uint.MaxValue : (1u << width) - 1;
        }
    }

    /// <summary>The bytes of a unit.</summary>
    private static int Stride => typeof(T) == typeof(byte) ? 1 : 2;

    /// <summary>The lanes at units: every lane over UTF-8, the even ones over UTF-16 (the low byte comes first).</summary>
    private static uint LaneMask => typeof(T) == typeof(byte) ? 0xFFFFFFFFu : 0x55555555u;

    /// <summary>Gets a value indicating whether the search is vectorised on this machine.</summary>
    public static bool Supported => Vector128.IsHardwareAccelerated;

    /// <summary>Gets a value indicating whether the packed search over UTF-16 is available on this machine.</summary>
    public static bool PackedSupported => Avx2.IsSupported && Vector256.IsHardwareAccelerated;

    private static int Unit(T u) => typeof(T) == typeof(byte) ? Unsafe.As<T, byte>(ref u) : Unsafe.As<T, ushort>(ref u);

    /// <summary>The earliest position at or after a position where one of the literals is, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <returns>The position, or -1.</returns>
    public int IndexOf(ReadOnlySpan<T> input, int from) => this.IndexOf(input, from, out _);

    /// <summary>The earliest position at or after a position where one of the literals is, or -1, and that literal's length.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="length">The length of the literal found (the first in the pattern's order at that position), or -1.</param>
    /// <returns>The position, or -1.</returns>
    public int IndexOf(ReadOnlySpan<T> input, int from, out int length)
    {
        length = -1;
        int last = input.Length - this.minLength;
        if (from > last)
        {
            return -1;
        }

        int i = from;
        if (this.packed)
        {
            int found = this.IndexOfPacked(input, ref i, last, out length);
            if (found != -1)
            {
                return found < 0 ? -1 : found;
            }
        }
        else if (Avx2.IsSupported && Vector256.IsHardwareAccelerated)
        {
            int found = this.IndexOf256(input, ref i, last, out length);
            if (found != -1)
            {
                return found < 0 ? -1 : found;
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            int found = this.IndexOf128(input, ref i, last, out length);
            if (found != -1)
            {
                return found < 0 ? -1 : found;
            }
        }

        for (; i <= last; i++)
        {
            length = this.VerifyAny(input, i);
            if (length >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Over UTF-16: thirty-two units narrowed to bytes at each step, then the lookups of the byte search; a unit beyond 255 narrows to 255. Returns the start, -1 with the position the vectors reached, or -2 past the last start.</summary>
    private int IndexOfPacked(ReadOnlySpan<T> input, ref int i, int last, out int length)
    {
        length = -1;
        ReadOnlySpan<ushort> units = MemoryMarshal.Cast<T, ushort>(input);
        int lastVector = units.Length - Vector256<byte>.Count - this.reach;
        ref ushort s = ref MemoryMarshal.GetReference(units);
        while (i <= lastVector)
        {
            Vector256<byte> r = this.count == 3 ? this.Packed3(ref s, ref i, lastVector) : this.count == 2 ? this.Packed2(ref s, ref i, lastVector) : this.Packed1(ref s, ref i, lastVector);
            if (i > lastVector)
            {
                break;
            }

            uint lanes = (~Vector256.Equals(r, Vector256<byte>.Zero)).ExtractMostSignificantBits();
            int found = this.Candidates(input, i, lanes, r, 1, last, out length);
            if (found != -1)
            {
                return found;
            }

            i += Vector256<byte>.Count;
        }

        return -1;
    }

    /// <summary>The scan by bytes with vectors of thirty-two: a start is a lane at a unit. Returns the start, -1 with the position the vectors reached (in units), or -2 past the last start.</summary>
    private int IndexOf256(ReadOnlySpan<T> input, ref int i, int last, out int length)
    {
        length = -1;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(input);
        int lastVector = bytes.Length - Vector256<byte>.Count - (this.reach * Stride);
        ref byte s = ref MemoryMarshal.GetReference(bytes);
        int at = i * Stride;
        while (at <= lastVector)
        {
            Vector256<byte> r = this.count == 3 ? this.Bytes3(ref s, ref at, lastVector) : this.count == 2 ? this.Bytes2(ref s, ref at, lastVector) : this.Bytes1(ref s, ref at, lastVector);
            if (at > lastVector)
            {
                break;
            }

            uint lanes = (~Vector256.Equals(r, Vector256<byte>.Zero)).ExtractMostSignificantBits() & LaneMask;
            if (lanes != 0)
            {
                int found = this.Candidates(input, at, lanes, r, Stride, last, out length);
                if (found != -1)
                {
                    return found;
                }
            }

            at += Vector256<byte>.Count;
        }

        i = at / Stride;
        return -1;
    }

    /// <summary>The scan by bytes with vectors of sixteen. Returns the start, -1 with the position the vectors reached (in units), or -2 past the last start.</summary>
    private int IndexOf128(ReadOnlySpan<T> input, ref int i, int last, out int length)
    {
        length = -1;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(input);
        int lastVector = bytes.Length - Vector128<byte>.Count - (this.reach * Stride);
        ref byte s = ref MemoryMarshal.GetReference(bytes);
        int at = i * Stride;
        while (at <= lastVector)
        {
            Vector128<byte> r = this.count == 3 ? this.Half3(ref s, ref at, lastVector) : this.count == 2 ? this.Half2(ref s, ref at, lastVector) : this.Half1(ref s, ref at, lastVector);
            if (at > lastVector)
            {
                break;
            }

            uint lanes = (~Vector128.Equals(r, Vector128<byte>.Zero)).ExtractMostSignificantBits() & LaneMask;
            if (lanes != 0)
            {
                int found = this.Candidates(input, at, lanes, Vector256.Create(r, Vector128<byte>.Zero), Stride, last, out length);
                if (found != -1)
                {
                    return found;
                }
            }

            at += Vector128<byte>.Count;
        }

        i = at / Stride;
        return -1;
    }

    /// <summary>Thirty-two UTF-16 units from a position as bytes, in order, a unit beyond 255 as 255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<byte> Narrow(ref ushort s, int at)
    {
        Vector256<ushort> a = Vector256.LoadUnsafe(ref s, (nuint)at);
        Vector256<ushort> b = Vector256.LoadUnsafe(ref s, (nuint)(at + Vector256<ushort>.Count));
        Vector256<byte> packed = Avx2.PackUnsignedSaturate(a.AsInt16(), b.AsInt16());
        return Avx2.Permute4x64(packed.AsInt64(), 0xD8).AsByte();
    }

    /// <summary>The buckets whose literals have each byte of a vector at a position: the two nibble lookups.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<byte> Look(Vector256<byte> lo, Vector256<byte> hi, Vector256<byte> c, Vector256<byte> nibble)
        => Avx2.Shuffle(lo, c & nibble) & Avx2.Shuffle(hi, Vector256.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Look(Vector128<byte> lo, Vector128<byte> hi, Vector128<byte> c, Vector128<byte> nibble)
        => Vector128.ShuffleNative(lo, c & nibble) & Vector128.ShuffleNative(hi, Vector128.ShiftRightLogical(c.AsUInt16(), 4).AsByte() & nibble);

    // The loops over the vectors with no start in them: each leaves the position at the first vector with one
    // (its lookups returned), or past the last vector.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector256<byte> Packed3(ref ushort s, ref int i, int lastVector)
    {
        Vector256<byte> nibble = Vector256.Create((byte)0x0F);
        Vector256<byte> lo0 = this.lo0, hi0 = this.hi0, lo1 = this.lo1, hi1 = this.hi1, lo2 = this.lo2, hi2 = this.hi2;
        int p0 = this.p0, p1 = this.p1, p2 = this.p2;
        int at = i;
        Vector256<byte> r = default;
        for (; at <= lastVector; at += Vector256<byte>.Count)
        {
            r = Look(lo0, hi0, Narrow(ref s, at + p0), nibble) & Look(lo1, hi1, Narrow(ref s, at + p1), nibble) & Look(lo2, hi2, Narrow(ref s, at + p2), nibble);
            if (r != Vector256<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector256<byte> Packed2(ref ushort s, ref int i, int lastVector)
    {
        Vector256<byte> nibble = Vector256.Create((byte)0x0F);
        Vector256<byte> lo0 = this.lo0, hi0 = this.hi0, lo1 = this.lo1, hi1 = this.hi1;
        int p0 = this.p0, p1 = this.p1;
        int at = i;
        Vector256<byte> r = default;
        for (; at <= lastVector; at += Vector256<byte>.Count)
        {
            r = Look(lo0, hi0, Narrow(ref s, at + p0), nibble) & Look(lo1, hi1, Narrow(ref s, at + p1), nibble);
            if (r != Vector256<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector256<byte> Packed1(ref ushort s, ref int i, int lastVector)
    {
        Vector256<byte> nibble = Vector256.Create((byte)0x0F);
        Vector256<byte> lo0 = this.lo0, hi0 = this.hi0;
        int p0 = this.p0;
        int at = i;
        Vector256<byte> r = default;
        for (; at <= lastVector; at += Vector256<byte>.Count)
        {
            r = Look(lo0, hi0, Narrow(ref s, at + p0), nibble);
            if (r != Vector256<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector256<byte> Bytes3(ref byte s, ref int i, int lastVector)
    {
        Vector256<byte> nibble = Vector256.Create((byte)0x0F);
        Vector256<byte> lo0 = this.lo0, hi0 = this.hi0, lo1 = this.lo1, hi1 = this.hi1, lo2 = this.lo2, hi2 = this.hi2;
        int p0 = this.p0 * Stride, p1 = this.p1 * Stride, p2 = this.p2 * Stride;
        int at = i;
        Vector256<byte> r = default;
        for (; at <= lastVector; at += Vector256<byte>.Count)
        {
            r = Look(lo0, hi0, Vector256.LoadUnsafe(ref s, (nuint)(at + p0)), nibble) & Look(lo1, hi1, Vector256.LoadUnsafe(ref s, (nuint)(at + p1)), nibble) & Look(lo2, hi2, Vector256.LoadUnsafe(ref s, (nuint)(at + p2)), nibble);
            if (r != Vector256<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector256<byte> Bytes2(ref byte s, ref int i, int lastVector)
    {
        Vector256<byte> nibble = Vector256.Create((byte)0x0F);
        Vector256<byte> lo0 = this.lo0, hi0 = this.hi0, lo1 = this.lo1, hi1 = this.hi1;
        int p0 = this.p0 * Stride, p1 = this.p1 * Stride;
        int at = i;
        Vector256<byte> r = default;
        for (; at <= lastVector; at += Vector256<byte>.Count)
        {
            r = Look(lo0, hi0, Vector256.LoadUnsafe(ref s, (nuint)(at + p0)), nibble) & Look(lo1, hi1, Vector256.LoadUnsafe(ref s, (nuint)(at + p1)), nibble);
            if (r != Vector256<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector256<byte> Bytes1(ref byte s, ref int i, int lastVector)
    {
        Vector256<byte> nibble = Vector256.Create((byte)0x0F);
        Vector256<byte> lo0 = this.lo0, hi0 = this.hi0;
        int p0 = this.p0 * Stride;
        int at = i;
        Vector256<byte> r = default;
        for (; at <= lastVector; at += Vector256<byte>.Count)
        {
            r = Look(lo0, hi0, Vector256.LoadUnsafe(ref s, (nuint)(at + p0)), nibble);
            if (r != Vector256<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector128<byte> Half3(ref byte s, ref int i, int lastVector)
    {
        Vector128<byte> nibble = Vector128.Create((byte)0x0F);
        Vector128<byte> lo0 = this.lo[0], hi0 = this.hi[0], lo1 = this.lo[1], hi1 = this.hi[1], lo2 = this.lo[2], hi2 = this.hi[2];
        int p0 = this.p0 * Stride, p1 = this.p1 * Stride, p2 = this.p2 * Stride;
        int at = i;
        Vector128<byte> r = default;
        for (; at <= lastVector; at += Vector128<byte>.Count)
        {
            r = Look(lo0, hi0, Vector128.LoadUnsafe(ref s, (nuint)(at + p0)), nibble) & Look(lo1, hi1, Vector128.LoadUnsafe(ref s, (nuint)(at + p1)), nibble) & Look(lo2, hi2, Vector128.LoadUnsafe(ref s, (nuint)(at + p2)), nibble);
            if (r != Vector128<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector128<byte> Half2(ref byte s, ref int i, int lastVector)
    {
        Vector128<byte> nibble = Vector128.Create((byte)0x0F);
        Vector128<byte> lo0 = this.lo[0], hi0 = this.hi[0], lo1 = this.lo[1], hi1 = this.hi[1];
        int p0 = this.p0 * Stride, p1 = this.p1 * Stride;
        int at = i;
        Vector128<byte> r = default;
        for (; at <= lastVector; at += Vector128<byte>.Count)
        {
            r = Look(lo0, hi0, Vector128.LoadUnsafe(ref s, (nuint)(at + p0)), nibble) & Look(lo1, hi1, Vector128.LoadUnsafe(ref s, (nuint)(at + p1)), nibble);
            if (r != Vector128<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector128<byte> Half1(ref byte s, ref int i, int lastVector)
    {
        Vector128<byte> nibble = Vector128.Create((byte)0x0F);
        Vector128<byte> lo0 = this.lo[0], hi0 = this.hi[0];
        int p0 = this.p0 * Stride;
        int at = i;
        Vector128<byte> r = default;
        for (; at <= lastVector; at += Vector128<byte>.Count)
        {
            r = Look(lo0, hi0, Vector128.LoadUnsafe(ref s, (nuint)(at + p0)), nibble);
            if (r != Vector128<byte>.Zero)
            {
                break;
            }
        }

        i = at;
        return r;
    }

    /// <summary>The earliest start among a vector's candidates at which one of the literals of a bucket its bytes agree with is: the start, or -1, or -2 where a candidate is past the last start.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private int Candidates(ReadOnlySpan<T> input, int i, uint lanes, Vector256<byte> r, int stride, int last, out int length)
    {
        length = -1;
        Span<byte> hits = stackalloc byte[Vector256<byte>.Count];
        r.CopyTo(hits);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(input);
        ref byte s = ref MemoryMarshal.GetReference(bytes);
        while (lanes != 0)
        {
            int lane = BitOperations.TrailingZeroCount(lanes);
            lanes &= lanes - 1;
            int start = (i + lane) / stride;
            if (start > last)
            {
                return -2;
            }

            int bits = hits[lane];
            bool whole = bytes.Length - (start * Stride) >= Vector256<byte>.Count;
            while (bits != 0)
            {
                int b = BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                foreach (int k in this.buckets[b])
                {
                    Vector256<byte>[]? vectors = this.forms[k];
                    if (vectors is not null && whole)
                    {
                        Vector256<byte> x = Vector256.LoadUnsafe(ref s, (nuint)(start * Stride));
                        Vector256<byte> ok = Vector256.Equals(x, vectors[0]);
                        for (int f = 1; f < vectors.Length; f++)
                        {
                            ok |= Vector256.Equals(x, vectors[f]);
                        }

                        uint mask = this.formMask[k];
                        if ((ok.ExtractMostSignificantBits() & mask) == mask)
                        {
                            length = this.needles[k].Length;
                            return start;
                        }

                        continue;
                    }

                    Needle<T> needle = this.needles[k];
                    if (input.Length - start >= needle.Length && needle.Verify(input, start))
                    {
                        length = needle.Length;
                        return start;
                    }
                }
            }
        }

        return -1;
    }

    private int VerifyAny(ReadOnlySpan<T> input, int start)
    {
        foreach (Needle<T> needle in this.needles)
        {
            if (input.Length - start >= needle.Length && needle.Verify(input, start))
            {
                return needle.Length;
            }
        }

        return -1;
    }
}
#endif
