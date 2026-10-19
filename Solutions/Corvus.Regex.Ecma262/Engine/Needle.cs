// <copyright file="Needle.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET
using System.Buffers;
using System.Runtime.Intrinsics;
#endif

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// A literal to search for in text of one encoding, by anchors. Two (or three) unit positions of the literal are
/// compared at a whole vector of text positions at once, and the literal is compared in full only where every
/// anchor agrees. The anchors are the positions text makes rarest, by the frequency of English letters and of
/// what UTF-8 makes of other scripts, so the full comparison is rare: the span search's first-and-last-unit
/// anchors compare at nearly every character of a Cyrillic or CJK text over UTF-8, where every character of a
/// script starts with the same byte or two. A position allows one value, or two or three where the literal is
/// matched ignoring case (a letter's cases), and then the full comparison is of each character against each of
/// its forms. A literal of one unit, or one with a unit rare enough on its own, is searched for by the vectorised
/// search for a value.
/// </summary>
/// <typeparam name="T">The unit: byte for UTF-8, ushort for UTF-16.</typeparam>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class Needle<T>
    where T : unmanaged, IEquatable<T>
{
    /// <summary>The most forms a character has.</summary>
    public const int MaxRows = 4;

    private readonly T[][] rows;
#if NET
    private readonly SearchValues<string>? spanString;
#endif
    private readonly int[] starts;
    private readonly bool exact;
    private readonly T[]? foldLower;
    private readonly T[]? foldMask;
    private readonly T[]? unitA;
    private readonly T[]? unitB;
    private readonly T[]? unitC;
    private readonly Mode mode;
    private readonly int o1;
    private readonly int o2;
    private readonly int o3;
    private readonly T a1;
    private readonly T b1;
    private readonly T c1;
    private readonly T a2;
    private readonly T b2;
    private readonly T c2;
    private readonly T a3;
    private readonly T b3;
    private readonly T c3;
    private readonly T m1;
    private readonly T m2;
    private readonly T m3;
    private readonly bool masked;
    private readonly bool packable;

    /// <summary>Initializes a new instance of the <see cref="Needle{T}"/> class for a literal matched exactly.</summary>
    /// <param name="units">The literal, which is not empty.</param>
    public Needle(T[] units)
        : this([units], [0, units.Length])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Needle{T}"/> class for a literal whose characters each have
    /// one or more forms of the same length.
    /// </summary>
    /// <param name="rows">The literal in each of its forms, all of one length: row r holds each character's form r, or its last form when it has fewer.</param>
    /// <param name="starts">The unit position each character starts at, then the length.</param>
    public Needle(T[][] rows, int[] starts)
    {
        this.rows = rows;
        this.starts = starts;
        this.Length = rows[0].Length;
        this.exact = rows.Length == 1;
        int n = this.Length;
        if (!this.exact)
        {
            this.MakeFold(out this.foldLower, out this.foldMask);
            bool few = true;
            Span<T> seen = stackalloc T[MaxRows];
            for (int j = 0; j < n && few; j++)
            {
                int distinctCount = 0;
                foreach (T[] row in rows)
                {
                    bool dup = false;
                    for (int k = 0; k < distinctCount; k++)
                    {
                        dup |= seen[k].Equals(row[j]);
                    }

                    if (!dup)
                    {
                        seen[distinctCount++] = row[j];
                    }
                }

                few &= distinctCount <= 3;
            }

            if (this.foldLower is null && starts.Length - 1 == n && few)
            {
                // Every character is one unit: each position allows up to three values, compared in place.
                this.unitA = new T[n];
                this.unitB = new T[n];
                this.unitC = new T[n];
                for (int j = 0; j < n; j++)
                {
                    this.ValuesAt(j, out this.unitA[j], out this.unitB[j], out this.unitC[j]);
                }
            }
        }

        // The values each position allows, and how common they are together.
        int[] shares = new int[n];
        int[] counts = new int[n];
        Span<T> values = stackalloc T[MaxRows];
        for (int j = 0; j < n; j++)
        {
            int count = 0;
            int share = 0;
            foreach (T[] row in rows)
            {
                T v = row[j];
                bool seen = false;
                for (int k = 0; k < count; k++)
                {
                    seen |= values[k].Equals(v);
                }

                if (!seen)
                {
                    values[count++] = v;
                    share += 255 - UnitRarity.Of(v);
                }
            }

            counts[j] = count;

            // A position of three values costs three comparisons and two ors at every vector, and one of two
            // values that no mask joins costs two and one, where one value (or a masked pair) costs one: the
            // extra work is charged as if the position let more starts through.
            T first = rows[0][j];
            bool maskable = count == 1 || (count == 2 && Mask(first, values[1], values[1], out _));
            shares[j] = share + (count >= 3 ? 24 : maskable ? 0 : 8);
        }

        int p1 = Rarest(shares, counts, -1, -1);
        if (p1 < 0)
        {
            this.mode = Mode.None;
            return;
        }

        this.o1 = p1;
        this.o2 = p1;
        this.ValuesAt(p1, out this.a1, out this.b1, out this.c1);
        this.a2 = this.a1;
        this.b2 = this.b1;
        this.c2 = this.c1;
        if (n == 1 || shares[p1] <= SingleShare)
        {
            // A value rare enough is searched for alone: the search for one value (or two) is the fastest there
            // is, and the full comparison at each occurrence costs less than testing a second anchor everywhere.
            this.mode = counts[p1] == 1 ? Mode.One : counts[p1] == 2 ? Mode.OneOfTwo : Mode.OneOfThree;
            return;
        }

        int p2 = Rarest(shares, counts, p1, -1);
        if (p2 < 0)
        {
            this.mode = Mode.None;
            return;
        }

        this.o2 = p2;
        this.ValuesAt(p2, out this.a2, out this.b2, out this.c2);

        // A third anchor pays where the two rarest are still common letters (the, ing): the full comparison
        // would otherwise be made at more than one position in two thousand.
        int p3 = (long)shares[p1] * shares[p2] > 500 ? Rarest(shares, counts, p1, p2) : -1;
        if (this.exact)
        {
            if (typeof(T) == typeof(ushort))
            {
                // Over UTF-16 the span search, anchored on the first and last units, is as fast as any: it is
                // kept for a literal that is matched exactly.
                this.mode = Mode.Span;
#if NET
                if (typeof(T) == typeof(ushort) && n >= 2 && n <= 8 && (long)shares[0] * shares[n - 1] > 500)
                {
                    // Both ends common letters: the span search's first-and-last comparison lets many starts
                    // through, where the runtime's fingerprint search does not (aei over Sherlock: 23 us to 28;
                    // a literal with a rare end, such as ://, keeps the span search).
                    this.spanString = SearchValues.Create([new string(MemoryMarshal.Cast<T, char>(rows[0]))], StringComparison.Ordinal);
                }
#endif
                return;
            }

            if (p3 >= 0)
            {
                this.o3 = p3;
                this.a3 = rows[0][p3];
                this.b3 = this.a3;
                this.c3 = this.a3;
                this.mode = Mode.Three;
                return;
            }

            if (p2 == p1 + 1 || p1 == p2 + 1)
            {
                // Adjacent bytes are one comparison of sixteen-bit lanes, over the text at both alignments.
                this.mode = Mode.Pair;
                return;
            }

            this.mode = Mode.Two;
            return;
        }

        // Two forms that differ in the bits of a mask (a letter's cases differ in 0x20, in ASCII, in UTF-16 for
        // Latin, Greek and Cyrillic, and in the last byte of UTF-8) are tested by one comparison of the unit with
        // those bits set.
        this.masked = Mask(this.a1, this.b1, this.c1, out this.m1) & Mask(this.a2, this.b2, this.c2, out this.m2);
        if (p3 >= 0)
        {
            // A third anchor that no mask joins would cost the mask (and the packing) on all three.
            this.ValuesAt(p3, out T a3, out T b3, out T c3);
            if (this.masked && !Mask(a3, b3, c3, out _))
            {
                p3 = -1;
            }
        }

        if (p3 >= 0)
        {
            this.o3 = p3;
            this.ValuesAt(p3, out this.a3, out this.b3, out this.c3);
            this.masked &= Mask(this.a3, this.b3, this.c3, out this.m3);
            this.mode = Mode.ThreeFolded;
        }
        else
        {
            this.m3 = this.m2;
            this.a3 = this.a2;
            this.mode = Mode.TwoFolded;
        }

        // Over UTF-16, anchors below 0xFF (ASCII, Latin-1) are compared as bytes: two vectors of sixteen units
        // are packed into one of thirty-two bytes (a unit above the byte range packs to 0xFF or to 0, which no
        // such anchor is), so each iteration takes twice the text.
        this.packable = typeof(T) == typeof(ushort) && this.masked
            && Unit(this.a1) < 0xFF && Unit(this.a2) < 0xFF && Unit(this.a3) < 0xFF && Unit(this.m1) < 0xFF && Unit(this.m2) < 0xFF && Unit(this.m3) < 0xFF;
    }

    /// <summary>The share of text (in parts per thousand) at or below which one anchor's values are searched for alone.</summary>
    private const int SingleShare = 1;

    /// <summary>Whether two values (or one) differ only in bits the first lacks, so a unit with those bits set equals the second exactly when it is one of them; the mask, or zero for one value.</summary>
    private static bool Mask(T a, T b, T c, out T mask)
    {
        int x = Unit(a);
        int y = Unit(b);
        int z = Unit(c);
        mask = FromUnit(0);
        if (x == y && y == z)
        {
            return true;
        }

        if (z != y)
        {
            return false;
        }

        int bits = x ^ y;
        if ((x & bits) != 0)
        {
            // The first form has some of the bits: the second is the one that lacks them.
            (x, y) = (y, x);
            if ((x & bits) != 0)
            {
                return false;
            }
        }

        mask = FromUnit(bits);
        return true;
    }

    private enum Mode
    {
        /// <summary>No position allows few enough values to anchor on: the needle cannot search.</summary>
        None,

        /// <summary>One value at one position, rare enough to search for alone.</summary>
        One,

        /// <summary>One of two values at one position.</summary>
        OneOfTwo,

        /// <summary>One of three values at one position.</summary>
        OneOfThree,

        /// <summary>Two anchors of one value each.</summary>
        Two,

        /// <summary>Three anchors of one value each.</summary>
        Three,

        /// <summary>Two anchors of up to three values each.</summary>
        TwoFolded,

        /// <summary>Three anchors of up to three values each.</summary>
        ThreeFolded,

        /// <summary>The span search for the literal, which is matched exactly.</summary>
        Span,

        /// <summary>Two adjacent anchors of one value each over UTF-8, compared as one unit of sixteen bits.</summary>
        Pair,
    }

    /// <summary>Gets the length of the literal in units.</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the needle can search (some position allows three values or fewer).</summary>
    public bool CanSearch => this.mode != Mode.None;

    /// <summary>Gets a value indicating whether the literal is matched exactly (every position allows one value).</summary>
    public bool Exact => this.exact;

    /// <summary>Gets the offset of the first anchor (the rarest position), for a search for several needles at once.</summary>
    public int Offset1 => this.o1;

    /// <summary>Gets the value at the first anchor, when the literal is matched exactly.</summary>
    public T Value1 => this.a1;

    /// <summary>Gets the offset of the second anchor (the next rarest position, or the first again for a literal of one unit).</summary>
    public int Offset2 => this.o2;

    /// <summary>Gets the value at the second anchor, when the literal is matched exactly.</summary>
    public T Value2 => this.a2;

    /// <summary>The unit at a position of the literal's first form.</summary>
    /// <param name="offset">The position.</param>
    /// <returns>The unit.</returns>
    public T UnitAt(int offset) => this.rows[0][offset];

    /// <summary>The distinct units a position allows, over the literal's forms.</summary>
    /// <param name="offset">The position.</param>
    /// <param name="values">Receives the units; at least <see cref="MaxRows"/> long.</param>
    /// <returns>How many.</returns>
    public int UnitsAt(int offset, Span<T> values)
    {
        int count = 0;
        foreach (T[] row in this.rows)
        {
            T v = row[offset];
            bool seen = false;
            for (int k = 0; k < count; k++)
            {
                seen |= values[k].Equals(v);
            }

            if (!seen)
            {
                values[count++] = v;
            }
        }

        return count;
    }

    /// <summary>Whether two units differ only in bits the first lacks (a letter's cases), so that a unit with those bits set equals the second exactly when it is one of them.</summary>
    /// <param name="a">One unit.</param>
    /// <param name="b">The other.</param>
    /// <param name="mask">The bits.</param>
    /// <returns>Whether they do.</returns>
    public static bool TryMask(T a, T b, out T mask) => Mask(a, b, b, out mask);

    /// <summary>The first occurrence of the literal at or after a position, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <returns>The position of the occurrence, or -1.</returns>
    public int IndexOf(ReadOnlySpan<T> input, int from)
    {
        int n = this.Length;
        int last = input.Length - n;
        if (from > last)
        {
            return -1;
        }

        switch (this.mode)
        {
            case Mode.One:
            case Mode.OneOfTwo:
            case Mode.OneOfThree:
            {
                int o = this.o1;
                int at = from + o;
                while (true)
                {
                    ReadOnlySpan<T> rest = input.Slice(at, input.Length - at - (n - 1 - o));
                    int j = this.mode == Mode.One ? rest.IndexOf(this.a1) : this.mode == Mode.OneOfTwo ? rest.IndexOfAny(this.a1, this.b1) : rest.IndexOfAny(this.a1, this.b1, this.c1);
                    if (j < 0)
                    {
                        return -1;
                    }

                    int start = at + j - o;
                    if (n == 1 || this.Verify(input, start))
                    {
                        return start;
                    }

                    at += j + 1;
                }
            }

            case Mode.Two:
            case Mode.Three:
            case Mode.TwoFolded:
            case Mode.ThreeFolded:
#if NET
                return this.Scan(input, from, last);
#else
                break;
#endif
            case Mode.Pair:
#if NET
                return this.ScanPair(input, from, last);
#else
                break;
#endif
            case Mode.Span:
            {
#if NET
                if (this.spanString is not null)
                {
                    // The runtime's search for a string by its fingerprints (Teddy): for a short literal of common
                    // letters it beats the span search's first-and-last comparison (aei over Sherlock: 23 us to 28).
                    int s = MemoryMarshal.Cast<T, char>(input.Slice(from)).IndexOfAny(this.spanString);
                    return s < 0 ? -1 : from + s;
                }
#endif
                int k = input.Slice(from).IndexOf(this.rows[0]);
                return k < 0 ? -1 : from + k;
            }

            default:
                throw new InvalidOperationException("The needle cannot search.");
        }

#if !NET
        for (int i = from; i <= last; i++)
        {
            if (this.Verify(input, i))
            {
                return i;
            }
        }

        return -1;
#endif
    }

    /// <summary>Gets a value indicating whether the literal is best searched for by the runtime's string search (common letters at both ends, over UTF-16).</summary>
    public bool PrefersStringSearch
    {
        get
        {
#if NET
            return this.spanString is not null;
#else
            return false;
#endif
        }
    }

    /// <summary>Whether the literal is at a position (in one of its forms).</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, with at least the literal's length after it.</param>
    /// <returns>Whether it is.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Verify(ReadOnlySpan<T> input, int pos)
    {
        int n = this.Length;
        if (this.exact)
        {
            return input.Slice(pos, n).SequenceEqual(this.rows[0]);
        }

        return this.VerifyForms(input, pos);
    }

    private bool VerifyForms(ReadOnlySpan<T> input, int pos)
    {
        T[]? lower = this.foldLower;
        if (lower is not null)
        {
            // Every character is ASCII with its two cases, or one form: a letter's case bit is set and the lower
            // case compared.
            T[] mask = this.foldMask!;
            ReadOnlySpan<T> at = input.Slice(pos, lower.Length);
            for (int k = 0; k < lower.Length; k++)
            {
                if ((Unit(at[k]) | Unit(mask[k])) != Unit(lower[k]))
                {
                    return false;
                }
            }

            return true;
        }

        T[]? ua = this.unitA;
        if (ua is not null)
        {
            T[] ub = this.unitB!;
            T[] uc = this.unitC!;
            ReadOnlySpan<T> at = input.Slice(pos, ua.Length);
            for (int k = 0; k < ua.Length; k++)
            {
                T x = at[k];
                if (!x.Equals(ua[k]) && !x.Equals(ub[k]) && !x.Equals(uc[k]))
                {
                    return false;
                }
            }

            return true;
        }

        T[][] rows = this.rows;
        int[] starts = this.starts;
        for (int k = 0; k < starts.Length - 1; k++)
        {
            int s = starts[k];
            int w = starts[k + 1] - s;
            ReadOnlySpan<T> at = input.Slice(pos + s, w);
            bool any = false;
            foreach (T[] row in rows)
            {
                if (at.SequenceEqual(row.AsSpan(s, w)))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Unit(T u) => typeof(T) == typeof(byte) ? Unsafe.As<T, byte>(ref u) : Unsafe.As<T, ushort>(ref u);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T Or(T u, T mask) => FromUnit(Unit(u) | Unit(mask));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T FromUnit(int v)
    {
        if (typeof(T) == typeof(byte))
        {
            byte b = (byte)v;
            return Unsafe.As<byte, T>(ref b);
        }

        ushort c = (ushort)v;
        return Unsafe.As<ushort, T>(ref c);
    }

    /// <summary>
    /// When every character is one ASCII unit with one form, or an ASCII letter with exactly its two cases, the
    /// lower-case form and, for each position, the bit that sets the lower case (0x20 for a letter, 0 for another
    /// character); otherwise null.
    /// </summary>
    private void MakeFold(out T[]? lower, out T[]? mask)
    {
        lower = null;
        mask = null;
        T[][] rows = this.rows;
        int n = this.Length;
        T[] lo = new T[n];
        T[] bits = new T[n];
        for (int j = 0; j < n; j++)
        {
            int first = Unit(rows[0][j]);
            if (first >= 0x80)
            {
                return;
            }

            int low = first;
            int bit = 0;
            foreach (T[] row in rows)
            {
                int v = Unit(row[j]);
                if (v == first)
                {
                    continue;
                }

                bool letters = (first >= 'a' && first <= 'z' && v == first - 0x20) || (first >= 'A' && first <= 'Z' && v == first + 0x20);
                if (!letters)
                {
                    return;
                }

                low = first | 0x20;
                bit = 0x20;
            }

            lo[j] = FromUnit(low);
            bits[j] = FromUnit(bit);
        }

        lower = lo;
        mask = bits;
    }

    private static int Rarest(int[] shares, int[] counts, int not1, int not2)
    {
        int best = -1;
        for (int j = 0; j < shares.Length; j++)
        {
            if (j != not1 && j != not2 && counts[j] <= 3 && (best < 0 || shares[j] < shares[best]))
            {
                best = j;
            }
        }

        return best;
    }

    private void ValuesAt(int j, out T a, out T b, out T c)
    {
        T[][] rows = this.rows;
        a = rows[0][j];
        b = a;
        c = a;
        int count = 1;
        for (int r = 1; r < rows.Length; r++)
        {
            T v = rows[r][j];
            if (!v.Equals(a) && !v.Equals(b) && !v.Equals(c))
            {
                if (count == 1)
                {
                    b = v;
                    c = v;
                }
                else
                {
                    c = v;
                }

                count++;
            }
        }
    }

#if NET
    /// <summary>
    /// The anchored search: a vector of starts at a time, in a loop that holds nothing but the comparisons (the
    /// constants are its arguments, and it makes no call, so everything stays in registers); the full comparison
    /// is made here, outside it, at each start the anchors let through.
    /// </summary>
    private int Scan(ReadOnlySpan<T> input, int from, int last)
    {
        ref T s = ref MemoryMarshal.GetReference(input);
        int i = from;
        if (this.packable && System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            int lastVector = last - 32 + 1;
            while (i <= lastVector)
            {
                i = this.FindPacked(ref Unsafe.As<T, ushort>(ref s), i, lastVector, out uint mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    if (this.Verify(input, start))
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += 32;
            }
        }

        if (Vector256.IsHardwareAccelerated)
        {
            int lastVector = last - Vector256<T>.Count + 1;
            while (i <= lastVector)
            {
                i = this.Find256(ref s, i, lastVector, out uint mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    if (this.Verify(input, start))
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += Vector256<T>.Count;
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            int lastVector = last - Vector128<T>.Count + 1;
            while (i <= lastVector)
            {
                i = this.Find128(ref s, i, lastVector, out uint mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    if (this.Verify(input, start))
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += Vector128<T>.Count;
            }
        }

        for (; i <= last; i++)
        {
            if (this.Test(ref Unsafe.Add(ref s, i)) && this.Verify(input, i))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The packed masked scan over UTF-16: thirty-two units an iteration (see <see cref="packable"/>).</summary>
    private int FindPacked(ref ushort s, int i, int lastVector, out uint mask)
    {
        Vector256<byte> u1 = Vector256.Create((byte)(Unit(this.a1) | Unit(this.m1)));
        Vector256<byte> u2 = Vector256.Create((byte)(Unit(this.a2) | Unit(this.m2)));
        Vector256<byte> u3 = Vector256.Create((byte)(Unit(this.a3) | Unit(this.m3)));
        Vector256<byte> m1 = Vector256.Create((byte)Unit(this.m1));
        Vector256<byte> m2 = Vector256.Create((byte)Unit(this.m2));
        Vector256<byte> m3 = Vector256.Create((byte)Unit(this.m3));
        return this.mode == Mode.TwoFolded
            ? FindTwoMaskedPacked(ref s, i, lastVector, this.o1, this.o2, u1, u2, m1, m2, out mask)
            : FindThreeMaskedPacked(ref s, i, lastVector, this.o1, this.o2, this.o3, u1, u2, u3, m1, m2, m3, out mask);
    }

    private static int FindTwoMaskedPacked(ref ushort s, int i, int lastVector, int o1, int o2, Vector256<byte> u1, Vector256<byte> u2, Vector256<byte> m1, Vector256<byte> m2, out uint mask)
    {
        while (i <= lastVector)
        {
            ref ushort cur = ref Unsafe.Add(ref s, i);
            Vector256<byte> x1 = Pack(Vector256.LoadUnsafe(ref cur, (nuint)o1), Vector256.LoadUnsafe(ref cur, (nuint)(o1 + 16)));
            Vector256<byte> x2 = Pack(Vector256.LoadUnsafe(ref cur, (nuint)o2), Vector256.LoadUnsafe(ref cur, (nuint)(o2 + 16)));
            Vector256<byte> eq = Vector256.Equals(x1 | m1, u1) & Vector256.Equals(x2 | m2, u2);
            if (eq != Vector256<byte>.Zero)
            {
                mask = Unpack(eq.ExtractMostSignificantBits());
                return i;
            }

            i += 32;
        }

        mask = 0;
        return i;
    }

    private static int FindThreeMaskedPacked(ref ushort s, int i, int lastVector, int o1, int o2, int o3, Vector256<byte> u1, Vector256<byte> u2, Vector256<byte> u3, Vector256<byte> m1, Vector256<byte> m2, Vector256<byte> m3, out uint mask)
    {
        while (i <= lastVector)
        {
            ref ushort cur = ref Unsafe.Add(ref s, i);
            Vector256<byte> x1 = Pack(Vector256.LoadUnsafe(ref cur, (nuint)o1), Vector256.LoadUnsafe(ref cur, (nuint)(o1 + 16)));
            Vector256<byte> x2 = Pack(Vector256.LoadUnsafe(ref cur, (nuint)o2), Vector256.LoadUnsafe(ref cur, (nuint)(o2 + 16)));
            Vector256<byte> x3 = Pack(Vector256.LoadUnsafe(ref cur, (nuint)o3), Vector256.LoadUnsafe(ref cur, (nuint)(o3 + 16)));
            Vector256<byte> eq = Vector256.Equals(x1 | m1, u1) & Vector256.Equals(x2 | m2, u2) & Vector256.Equals(x3 | m3, u3);
            if (eq != Vector256<byte>.Zero)
            {
                mask = Unpack(eq.ExtractMostSignificantBits());
                return i;
            }

            i += 32;
        }

        mask = 0;
        return i;
    }

    /// <summary>Two vectors of units as one of bytes: the pack works a 128-bit lane at a time, so bytes 8 to 15 hold the second vector's first eight units and 16 to 23 the first's last eight.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<byte> Pack(Vector256<ushort> a, Vector256<ushort> b) => System.Runtime.Intrinsics.X86.Avx2.PackUnsignedSaturate(a.AsInt16(), b.AsInt16());

    /// <summary>The lanes of a packed comparison in the order of the units.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Unpack(uint m) => (m & 0xFF0000FFu) | ((m & 0x0000FF00u) << 8) | ((m & 0x00FF0000u) >> 8);

    /// <summary>The pair scan over UTF-8: the two adjacent anchor bytes as one sixteen-bit value, compared at every start (see <see cref="Mode.Pair"/>).</summary>
    private int ScanPair(ReadOnlySpan<T> input, int from, int last)
    {
        ref byte s = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(input));
        int o = Math.Min(this.o1, this.o2);
        ushort pair = (ushort)(Unit(this.rows[0][o]) | (Unit(this.rows[0][o + 1]) << 8));
        int i = from;
        if (Vector256.IsHardwareAccelerated)
        {
            int lastVector = last - 32 + 1;
            Vector256<ushort> p = Vector256.Create(pair);
            while (i <= lastVector)
            {
                i = PairSearch.Find256(ref s, i, lastVector, o, p, p, p, p, p, p, out uint mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    if (this.Verify(input, start))
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += 32;
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            int lastVector = last - 16 + 1;
            Vector128<ushort> p = Vector128.Create(pair);
            while (i <= lastVector)
            {
                i = PairSearch.Find128(ref s, i, lastVector, o, p, p, p, p, p, p, out uint mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    if (this.Verify(input, start))
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += 16;
            }
        }

        for (; i <= last; i++)
        {
            if (this.Verify(input, i))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether the anchors agree at a start.</summary>
    private bool Test(ref T cur)
    {
        T x1 = Unsafe.Add(ref cur, this.o1);
        T x2 = Unsafe.Add(ref cur, this.o2);
        bool two = (x1.Equals(this.a1) || x1.Equals(this.b1) || x1.Equals(this.c1)) && (x2.Equals(this.a2) || x2.Equals(this.b2) || x2.Equals(this.c2));
        if (!two || this.mode == Mode.Two || this.mode == Mode.TwoFolded)
        {
            return two;
        }

        T x3 = Unsafe.Add(ref cur, this.o3);
        return x3.Equals(this.a3) || x3.Equals(this.b3) || x3.Equals(this.c3);
    }

    /// <summary>The first vector of starts at or after a position where the anchors agree somewhere, with the lanes where they do; or a position past the last vector with no lanes.</summary>
    private int Find256(ref T s, int i, int lastVector, out uint mask)
    {
        switch (this.mode)
        {
            case Mode.Two:
                return FindTwo256(ref s, i, lastVector, this.o1, this.o2, Vector256.Create(this.a1), Vector256.Create(this.a2), out mask);
            case Mode.Three:
                return FindThree256(ref s, i, lastVector, this.o1, this.o2, this.o3, Vector256.Create(this.a1), Vector256.Create(this.a2), Vector256.Create(this.a3), out mask);
            case Mode.TwoFolded:
                return this.masked
                    ? FindTwoMasked256(ref s, i, lastVector, this.o1, this.o2, Vector256.Create(Or(this.a1, this.m1)), Vector256.Create(Or(this.a2, this.m2)), Vector256.Create(this.m1), Vector256.Create(this.m2), out mask)
                    : FindTwoFolded256(ref s, i, lastVector, this.o1, this.o2, Vector256.Create(this.a1), Vector256.Create(this.b1), Vector256.Create(this.c1), Vector256.Create(this.a2), Vector256.Create(this.b2), Vector256.Create(this.c2), out mask);
            default:
                return this.masked
                    ? FindThreeMasked256(ref s, i, lastVector, this.o1, this.o2, this.o3, Vector256.Create(Or(this.a1, this.m1)), Vector256.Create(Or(this.a2, this.m2)), Vector256.Create(Or(this.a3, this.m3)), Vector256.Create(this.m1), Vector256.Create(this.m2), Vector256.Create(this.m3), out mask)
                    : FindThreeFolded256(ref s, i, lastVector, this.o1, this.o2, this.o3, Vector256.Create(this.a1), Vector256.Create(this.b1), Vector256.Create(this.c1), Vector256.Create(this.a2), Vector256.Create(this.b2), Vector256.Create(this.c2), Vector256.Create(this.a3), Vector256.Create(this.b3), Vector256.Create(this.c3), out mask);
        }
    }

    /// <summary>The first vector of starts at or after a position where the anchors agree somewhere, with the lanes where they do; or a position past the last vector with no lanes.</summary>
    private int Find128(ref T s, int i, int lastVector, out uint mask)
    {
        switch (this.mode)
        {
            case Mode.Two:
                return FindTwo128(ref s, i, lastVector, this.o1, this.o2, Vector128.Create(this.a1), Vector128.Create(this.a2), out mask);
            case Mode.Three:
                return FindThree128(ref s, i, lastVector, this.o1, this.o2, this.o3, Vector128.Create(this.a1), Vector128.Create(this.a2), Vector128.Create(this.a3), out mask);
            case Mode.TwoFolded:
                return this.masked
                    ? FindTwoMasked128(ref s, i, lastVector, this.o1, this.o2, Vector128.Create(Or(this.a1, this.m1)), Vector128.Create(Or(this.a2, this.m2)), Vector128.Create(this.m1), Vector128.Create(this.m2), out mask)
                    : FindTwoFolded128(ref s, i, lastVector, this.o1, this.o2, Vector128.Create(this.a1), Vector128.Create(this.b1), Vector128.Create(this.c1), Vector128.Create(this.a2), Vector128.Create(this.b2), Vector128.Create(this.c2), out mask);
            default:
                return this.masked
                    ? FindThreeMasked128(ref s, i, lastVector, this.o1, this.o2, this.o3, Vector128.Create(Or(this.a1, this.m1)), Vector128.Create(Or(this.a2, this.m2)), Vector128.Create(Or(this.a3, this.m3)), Vector128.Create(this.m1), Vector128.Create(this.m2), Vector128.Create(this.m3), out mask)
                    : FindThreeFolded128(ref s, i, lastVector, this.o1, this.o2, this.o3, Vector128.Create(this.a1), Vector128.Create(this.b1), Vector128.Create(this.c1), Vector128.Create(this.a2), Vector128.Create(this.b2), Vector128.Create(this.c2), Vector128.Create(this.a3), Vector128.Create(this.b3), Vector128.Create(this.c3), out mask);
        }
    }

    private static int FindTwo256(ref T s, int i, int lastVector, int o1, int o2, Vector256<T> v1, Vector256<T> v2, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> eq = Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o1), v1) & Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o2), v2);
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindThree256(ref T s, int i, int lastVector, int o1, int o2, int o3, Vector256<T> v1, Vector256<T> v2, Vector256<T> v3, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> eq = Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o1), v1) & Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o2), v2) & Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o3), v3);
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindTwoMasked256(ref T s, int i, int lastVector, int o1, int o2, Vector256<T> u1, Vector256<T> u2, Vector256<T> m1, Vector256<T> m2, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> eq = Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o1) | m1, u1) & Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o2) | m2, u2);
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindThreeMasked256(ref T s, int i, int lastVector, int o1, int o2, int o3, Vector256<T> u1, Vector256<T> u2, Vector256<T> u3, Vector256<T> m1, Vector256<T> m2, Vector256<T> m3, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> eq = Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o1) | m1, u1) & Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o2) | m2, u2) & Vector256.Equals(Vector256.LoadUnsafe(ref cur, (nuint)o3) | m3, u3);
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindTwoFolded256(ref T s, int i, int lastVector, int o1, int o2, Vector256<T> a1, Vector256<T> b1, Vector256<T> c1, Vector256<T> a2, Vector256<T> b2, Vector256<T> c2, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> x1 = Vector256.LoadUnsafe(ref cur, (nuint)o1);
            Vector256<T> x2 = Vector256.LoadUnsafe(ref cur, (nuint)o2);
            Vector256<T> eq = (Vector256.Equals(x1, a1) | Vector256.Equals(x1, b1) | Vector256.Equals(x1, c1)) & (Vector256.Equals(x2, a2) | Vector256.Equals(x2, b2) | Vector256.Equals(x2, c2));
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindThreeFolded256(ref T s, int i, int lastVector, int o1, int o2, int o3, Vector256<T> a1, Vector256<T> b1, Vector256<T> c1, Vector256<T> a2, Vector256<T> b2, Vector256<T> c2, Vector256<T> a3, Vector256<T> b3, Vector256<T> c3, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> x1 = Vector256.LoadUnsafe(ref cur, (nuint)o1);
            Vector256<T> x2 = Vector256.LoadUnsafe(ref cur, (nuint)o2);
            Vector256<T> x3 = Vector256.LoadUnsafe(ref cur, (nuint)o3);
            Vector256<T> eq = (Vector256.Equals(x1, a1) | Vector256.Equals(x1, b1) | Vector256.Equals(x1, c1)) & (Vector256.Equals(x2, a2) | Vector256.Equals(x2, b2) | Vector256.Equals(x2, c2)) & (Vector256.Equals(x3, a3) | Vector256.Equals(x3, b3) | Vector256.Equals(x3, c3));
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindTwo128(ref T s, int i, int lastVector, int o1, int o2, Vector128<T> v1, Vector128<T> v2, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> eq = Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o1), v1) & Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o2), v2);
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindThree128(ref T s, int i, int lastVector, int o1, int o2, int o3, Vector128<T> v1, Vector128<T> v2, Vector128<T> v3, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> eq = Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o1), v1) & Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o2), v2) & Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o3), v3);
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindTwoMasked128(ref T s, int i, int lastVector, int o1, int o2, Vector128<T> u1, Vector128<T> u2, Vector128<T> m1, Vector128<T> m2, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> eq = Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o1) | m1, u1) & Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o2) | m2, u2);
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindThreeMasked128(ref T s, int i, int lastVector, int o1, int o2, int o3, Vector128<T> u1, Vector128<T> u2, Vector128<T> u3, Vector128<T> m1, Vector128<T> m2, Vector128<T> m3, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> eq = Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o1) | m1, u1) & Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o2) | m2, u2) & Vector128.Equals(Vector128.LoadUnsafe(ref cur, (nuint)o3) | m3, u3);
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindTwoFolded128(ref T s, int i, int lastVector, int o1, int o2, Vector128<T> a1, Vector128<T> b1, Vector128<T> c1, Vector128<T> a2, Vector128<T> b2, Vector128<T> c2, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> x1 = Vector128.LoadUnsafe(ref cur, (nuint)o1);
            Vector128<T> x2 = Vector128.LoadUnsafe(ref cur, (nuint)o2);
            Vector128<T> eq = (Vector128.Equals(x1, a1) | Vector128.Equals(x1, b1) | Vector128.Equals(x1, c1)) & (Vector128.Equals(x2, a2) | Vector128.Equals(x2, b2) | Vector128.Equals(x2, c2));
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindThreeFolded128(ref T s, int i, int lastVector, int o1, int o2, int o3, Vector128<T> a1, Vector128<T> b1, Vector128<T> c1, Vector128<T> a2, Vector128<T> b2, Vector128<T> c2, Vector128<T> a3, Vector128<T> b3, Vector128<T> c3, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> x1 = Vector128.LoadUnsafe(ref cur, (nuint)o1);
            Vector128<T> x2 = Vector128.LoadUnsafe(ref cur, (nuint)o2);
            Vector128<T> x3 = Vector128.LoadUnsafe(ref cur, (nuint)o3);
            Vector128<T> eq = (Vector128.Equals(x1, a1) | Vector128.Equals(x1, b1) | Vector128.Equals(x1, c1)) & (Vector128.Equals(x2, a2) | Vector128.Equals(x2, b2) | Vector128.Equals(x2, c2)) & (Vector128.Equals(x3, a3) | Vector128.Equals(x3, b3) | Vector128.Equals(x3, c3));
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }
#endif
}

#if NET
/// <summary>
/// The search over UTF-8 for any of up to six pairs of adjacent bytes, each compared as one sixteen-bit value:
/// the text is loaded at both alignments, so every start is a lane of one load or the other. A pair of bytes is
/// a far rarer thing than either byte (in a CJK text every byte is one of a few dozen, a pair one of thousands),
/// so the full comparisons are few.
/// </summary>
internal static class PairSearch
{
    /// <summary>The most pairs a search takes.</summary>
    public const int Most = 6;

    /// <summary>The first vector of thirty-two starts at or after a position where some pair is, with the lanes where it is; or a position past the last vector with no lanes.</summary>
    /// <param name="s">The text.</param>
    /// <param name="i">The position.</param>
    /// <param name="lastVector">The last position a vector of starts may begin at.</param>
    /// <param name="o">The offset of the pair from a start.</param>
    /// <param name="p0">A pair, as the first byte and the second shifted up by eight.</param>
    /// <param name="p1">A pair, or the first again.</param>
    /// <param name="p2">A pair, or the first again.</param>
    /// <param name="p3">A pair, or the first again.</param>
    /// <param name="p4">A pair, or the first again.</param>
    /// <param name="p5">A pair, or the first again.</param>
    /// <param name="mask">The lanes.</param>
    /// <returns>The position.</returns>
    public static int Find256(ref byte s, int i, int lastVector, int o, Vector256<ushort> p0, Vector256<ushort> p1, Vector256<ushort> p2, Vector256<ushort> p3, Vector256<ushort> p4, Vector256<ushort> p5, out uint mask)
    {
        while (i <= lastVector)
        {
            ref byte cur = ref Unsafe.Add(ref s, i + o);
            Vector256<ushort> even = Vector256.LoadUnsafe(ref cur).AsUInt16();
            Vector256<ushort> odd = Vector256.LoadUnsafe(ref cur, 1).AsUInt16();
            Vector256<ushort> e = Vector256.Equals(even, p0) | Vector256.Equals(even, p1) | Vector256.Equals(even, p2) | Vector256.Equals(even, p3) | Vector256.Equals(even, p4) | Vector256.Equals(even, p5);
            Vector256<ushort> d = Vector256.Equals(odd, p0) | Vector256.Equals(odd, p1) | Vector256.Equals(odd, p2) | Vector256.Equals(odd, p3) | Vector256.Equals(odd, p4) | Vector256.Equals(odd, p5);
            if ((e | d) != Vector256<ushort>.Zero)
            {
                mask = Spread(e.ExtractMostSignificantBits()) | (Spread(d.ExtractMostSignificantBits()) << 1);
                return i;
            }

            i += 32;
        }

        mask = 0;
        return i;
    }

    /// <summary>As <see cref="Find256"/>, over sixteen starts.</summary>
    /// <param name="s">The text.</param>
    /// <param name="i">The position.</param>
    /// <param name="lastVector">The last position a vector of starts may begin at.</param>
    /// <param name="o">The offset of the pair from a start.</param>
    /// <param name="p0">A pair.</param>
    /// <param name="p1">A pair, or the first again.</param>
    /// <param name="p2">A pair, or the first again.</param>
    /// <param name="p3">A pair, or the first again.</param>
    /// <param name="p4">A pair, or the first again.</param>
    /// <param name="p5">A pair, or the first again.</param>
    /// <param name="mask">The lanes.</param>
    /// <returns>The position.</returns>
    public static int Find128(ref byte s, int i, int lastVector, int o, Vector128<ushort> p0, Vector128<ushort> p1, Vector128<ushort> p2, Vector128<ushort> p3, Vector128<ushort> p4, Vector128<ushort> p5, out uint mask)
    {
        while (i <= lastVector)
        {
            ref byte cur = ref Unsafe.Add(ref s, i + o);
            Vector128<ushort> even = Vector128.LoadUnsafe(ref cur).AsUInt16();
            Vector128<ushort> odd = Vector128.LoadUnsafe(ref cur, 1).AsUInt16();
            Vector128<ushort> e = Vector128.Equals(even, p0) | Vector128.Equals(even, p1) | Vector128.Equals(even, p2) | Vector128.Equals(even, p3) | Vector128.Equals(even, p4) | Vector128.Equals(even, p5);
            Vector128<ushort> d = Vector128.Equals(odd, p0) | Vector128.Equals(odd, p1) | Vector128.Equals(odd, p2) | Vector128.Equals(odd, p3) | Vector128.Equals(odd, p4) | Vector128.Equals(odd, p5);
            if ((e | d) != Vector128<ushort>.Zero)
            {
                mask = Spread(e.ExtractMostSignificantBits()) | (Spread(d.ExtractMostSignificantBits()) << 1);
                return i;
            }

            i += 16;
        }

        mask = 0;
        return i;
    }

    /// <summary>The bits of a lane mask moved to every other position: lane k to bit 2k.</summary>
    /// <param name="m">The lane mask, of sixteen bits at most.</param>
    /// <returns>The spread mask.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Spread(uint m)
    {
        m = (m | (m << 8)) & 0x00FF00FFu;
        m = (m | (m << 4)) & 0x0F0F0F0Fu;
        m = (m | (m << 2)) & 0x33333333u;
        return (m | (m << 1)) & 0x55555555u;
    }
}
#endif

/// <summary>
/// Several literals to search for at once, each matched exactly or in its forms. One position (or two) that every
/// literal has is chosen, and the units of the text there are compared with each distinct unit the literals
/// allow there over a vector of starts, as the span search for any of several values does; a letter's two cases
/// are one comparison of the unit with their differing bit set. The earliest start where the units agree is
/// compared against each literal. The positions are those that make the work least by a cost model: the
/// comparisons at every vector, against the full comparisons at the starts the units let through. For an
/// alternation of literals, the pairs of first characters of a sequence, and the forms a case-insensitive
/// search leaves out.
/// </summary>
/// <typeparam name="T">The unit: byte for UTF-8, ushort for UTF-16.</typeparam>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class MultiNeedle<T>
    where T : unmanaged, IEquatable<T>
{
    /// <summary>The cost of a start the units let through, in the units of a vector operation: the search's return and restart, and the full comparison.</summary>
    private const double StartCost = 30;

    private readonly Needle<T>[] needles;
    private readonly bool exact;
    private readonly T[] a1;
    private readonly T[] a2;
    private readonly T[] values1;
    private readonly T[] masks1;
    private readonly T[] values2;
    private readonly T[] masks2;
    private readonly int o1;
    private readonly int o2;
    private readonly bool two;
    private readonly int minLength;
#if NET
    private readonly bool pair;
    private readonly ushort[] pairs = [];
    private readonly SearchValues<byte>? anyBytes;
    private readonly SearchValues<char>? anyChars;
#endif

    /// <summary>Initializes a new instance of the <see cref="MultiNeedle{T}"/> class.</summary>
    /// <param name="needles">The literals.</param>
    /// <param name="leadOnly">Whether to search by the first position alone whatever the cost model says: for the forms a case-insensitive search leaves out (the long s, the Kelvin sign), whose lead bytes are rare in any text the pattern's own script is written in, where the table of shares cannot know that.</param>
    public MultiNeedle(Needle<T>[] needles, bool leadOnly = false)
    {
        this.needles = needles;
        int k = needles.Length;
        this.exact = true;
        this.minLength = int.MaxValue;
        foreach (Needle<T> needle in needles)
        {
            this.minLength = Math.Min(this.minLength, needle.Length);
            this.exact &= needle.Exact;
        }

        // At each position every literal has: the distinct units there (a letter's cases as one unit with their
        // differing bit, and the bit) and their share of text together.
        int positions = this.minLength;
        T[][] values = new T[positions][];
        T[][] masks = new T[positions][];
        double[] share = new double[positions];
        Span<T> forms = stackalloc T[Needle<T>.MaxRows];
        for (int d = 0; d < positions; d++)
        {
            List<T> vs = [];
            List<T> ms = [];
            foreach (Needle<T> needle in needles)
            {
                int count = needle.UnitsAt(d, forms);
                if (count == 2 && Needle<T>.TryMask(forms[0], forms[1], out T mask))
                {
                    Add(vs, ms, Or(forms[0], mask), mask);
                    share[d] += (255 - UnitRarity.Of(forms[0]) + 255 - UnitRarity.Of(forms[1])) / 1000.0;
                    continue;
                }

                for (int f = 0; f < count; f++)
                {
                    Add(vs, ms, forms[f], default);
                    share[d] += (255 - UnitRarity.Of(forms[f])) / 1000.0;
                }
            }

            values[d] = [.. vs];
            masks[d] = [.. ms];
        }

        // The cheapest choice: one position (a load and a comparison per distinct unit, less one for the first),
        // or two (both, and the and), per unit of the vector, plus the starts let through at their cost.
        int units = typeof(T) == typeof(byte) ? 32 : 16;
        int best1 = 0;
        for (int d = 1; d < positions; d++)
        {
            if (share[d] < share[best1])
            {
                best1 = d;
            }
        }

        double cost = ((2.0 * values[best1].Length) / units) + (share[best1] * StartCost);
        int bestA = best1;
        int bestB = -1;
        if (leadOnly)
        {
            bestA = 0;
            cost = double.NegativeInfinity;
        }

        for (int d = 0; d < positions; d++)
        {
            for (int e = d + 1; e < positions; e++)
            {
                double c = ((3.0 + (2.0 * values[d].Length) + (2.0 * values[e].Length)) / units) + (share[d] * share[e] * StartCost);
                if (c < cost)
                {
                    cost = c;
                    bestA = d;
                    bestB = e;
                }
            }
        }

#if NET
        // Over UTF-8, two adjacent positions can be compared as one sixteen-bit value, which admits only the
        // literals' own pairs of bytes rather than every pairing of a unit of one position with a unit of the
        // other: the starts let through fall by the number of literals. For literals matched exactly.
        if (typeof(T) == typeof(byte) && this.exact)
        {
            for (int d = 0; d + 1 < positions; d++)
            {
                List<ushort> pairValues = [];
                foreach (Needle<T> needle in needles)
                {
                    ushort v = (ushort)(Unit(needle.UnitAt(d)) | (Unit(needle.UnitAt(d + 1)) << 8));
                    if (!pairValues.Contains(v))
                    {
                        pairValues.Add(v);
                    }
                }

                if (pairValues.Count > PairSearch.Most)
                {
                    continue;
                }

                double c = ((4.0 + (4.0 * pairValues.Count)) / units) + (share[d] * share[d + 1] / pairValues.Count * StartCost);
                if (c < cost)
                {
                    cost = c;
                    bestA = d;
                    bestB = d + 1;
                    this.pair = true;
                    this.pairs = [.. pairValues];
                }
            }
        }
#endif

        this.Cost = cost;
        this.o1 = bestA;
        this.two = bestB >= 0;
        if (bestB < 0)
        {
            // One position searched: the next rarest is checked at each start before the full comparison, which
            // turns away most of the starts a common first unit lets through (the H of Holmes before every He).
            bestB = bestA;
            for (int d = 0; d < positions; d++)
            {
                if (d != bestA && (bestB == bestA || share[d] < share[bestB]))
                {
                    bestB = d;
                }
            }
        }

        this.o2 = bestB;
        this.values1 = values[this.o1];
        this.masks1 = masks[this.o1];
        this.values2 = values[this.o2];
        this.masks2 = masks[this.o2];
        this.a1 = new T[k];
        this.a2 = new T[k];
        for (int i = 0; i < k; i++)
        {
            this.a1[i] = needles[i].UnitAt(this.o1);
            this.a2[i] = needles[i].UnitAt(this.o2);
        }

#if NET
        if (!this.two && this.exact)
        {
            // One position, exact: the span search for any of several values is the best there is.
            if (typeof(T) == typeof(byte))
            {
                this.anyBytes = SearchValues.Create(MemoryMarshal.Cast<T, byte>(this.values1));
            }
            else
            {
                this.anyChars = SearchValues.Create(MemoryMarshal.Cast<T, char>(this.values1));
            }
        }
#endif
    }

    /// <summary>Gets the cost of the search by the model, per unit of text, in the units of a vector operation (see <see cref="StartCost"/>).</summary>
    public double Cost { get; }

    /// <summary>Gets a value indicating whether the search compares two positions at every vector rather than searching one by the span search.</summary>
    public bool Two => this.two;

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
#if NET
        if (this.pair)
        {
            return this.ScanPair(input, from, last, out length);
        }

        if (!this.two && this.exact)
        {
            // The units at the position, searched for by the span search; each hit is a start to compare at.
            int o = this.o1;
            int at = from + o;
            int end = last + o;
            while (at <= end)
            {
                // Up to three values go to the span search's own overloads, whose start is cheaper than the search
                // for a set of values; the restarts are what a common unit costs.
                ReadOnlySpan<T> rest = input.Slice(at, end - at + 1);
                T[] v = this.values1;
                int j = v.Length switch
                {
                    1 => rest.IndexOf(v[0]),
                    2 => rest.IndexOfAny(v[0], v[1]),
                    3 => rest.IndexOfAny(v[0], v[1], v[2]),
                    _ => typeof(T) == typeof(byte) ? MemoryMarshal.Cast<T, byte>(rest).IndexOfAny(this.anyBytes!) : MemoryMarshal.Cast<T, char>(rest).IndexOfAny(this.anyChars!),
                };
                if (j < 0)
                {
                    return -1;
                }

                int start = at + j - o;
                length = this.VerifyAny(input, start);
                if (length >= 0)
                {
                    return start;
                }

                at += j + 1;
            }

            return -1;
        }

        if (this.values1.Length <= Straight && this.values2.Length <= Straight)
        {
            return this.ScanTwo(input, from, last, out length);
        }

        ref T s = ref MemoryMarshal.GetReference(input);
        int o1 = this.o1;
        int o2 = this.o2;
        if (Vector256.IsHardwareAccelerated)
        {
            int lastVector = last - Vector256<T>.Count + 1;
            while (i <= lastVector)
            {
                ref T cur = ref Unsafe.Add(ref s, i);
                Vector256<T> x1 = Vector256.LoadUnsafe(ref cur, (nuint)o1);
                Vector256<T> any = Vector256<T>.Zero;
                for (int j = 0; j < this.values1.Length; j++)
                {
                    any |= Vector256.Equals(x1 | Vector256.Create(this.masks1[j]), Vector256.Create(this.values1[j]));
                }

                Vector256<T> x2 = Vector256.LoadUnsafe(ref cur, (nuint)o2);
                Vector256<T> second = Vector256<T>.Zero;
                for (int j = 0; j < this.values2.Length; j++)
                {
                    second |= Vector256.Equals(x2 | Vector256.Create(this.masks2[j]), Vector256.Create(this.values2[j]));
                }

                any &= second;
                if (any != Vector256<T>.Zero)
                {
                    uint mask = any.ExtractMostSignificantBits();
                    do
                    {
                        int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                        length = this.VerifyAny(input, start);
                        if (length >= 0)
                        {
                            return start;
                        }

                        mask &= mask - 1;
                    }
                    while (mask != 0);
                }

                i += Vector256<T>.Count;
            }
        }
#endif

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

#if NET
    /// <summary>The most distinct units at a position the straight-line two-position scan takes; more go to the loop over the arrays.</summary>
    private const int Straight = 6;

    /// <summary>
    /// The two-position scan with up to six distinct units at each position: a loop of nothing but the
    /// comparisons, with the constants as its arguments (a position with fewer units repeats its first), and the
    /// full comparisons outside it at each start the units let through.
    /// </summary>
    private int ScanTwo(ReadOnlySpan<T> input, int from, int last, out int length)
    {
        length = -1;
        ref T s = ref MemoryMarshal.GetReference(input);
        T[] v1 = this.values1;
        T[] m1 = this.masks1;
        T[] v2 = this.values2;
        T[] m2 = this.masks2;
        bool masked = !this.exact;
        int i = from;
        if (Vector256.IsHardwareAccelerated)
        {
            int lastVector = last - Vector256<T>.Count + 1;
            while (i <= lastVector)
            {
                uint mask;
                i = masked
                    ? FindAnyMasked256(ref s, i, lastVector, this.o1, this.o2, Vector256.Create(v1[0]), Vector256.Create(At(v1, 1)), Vector256.Create(At(v1, 2)), Vector256.Create(At(v1, 3)), Vector256.Create(At(v1, 4)), Vector256.Create(At(v1, 5)), Vector256.Create(m1[0]), Vector256.Create(At(m1, 1)), Vector256.Create(At(m1, 2)), Vector256.Create(At(m1, 3)), Vector256.Create(At(m1, 4)), Vector256.Create(At(m1, 5)), Vector256.Create(v2[0]), Vector256.Create(At(v2, 1)), Vector256.Create(At(v2, 2)), Vector256.Create(At(v2, 3)), Vector256.Create(At(v2, 4)), Vector256.Create(At(v2, 5)), Vector256.Create(m2[0]), Vector256.Create(At(m2, 1)), Vector256.Create(At(m2, 2)), Vector256.Create(At(m2, 3)), Vector256.Create(At(m2, 4)), Vector256.Create(At(m2, 5)), out mask)
                    : FindAny256(ref s, i, lastVector, this.o1, this.o2, Vector256.Create(v1[0]), Vector256.Create(At(v1, 1)), Vector256.Create(At(v1, 2)), Vector256.Create(At(v1, 3)), Vector256.Create(At(v1, 4)), Vector256.Create(At(v1, 5)), Vector256.Create(v2[0]), Vector256.Create(At(v2, 1)), Vector256.Create(At(v2, 2)), Vector256.Create(At(v2, 3)), Vector256.Create(At(v2, 4)), Vector256.Create(At(v2, 5)), out mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    length = this.VerifyAny(input, start);
                    if (length >= 0)
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += Vector256<T>.Count;
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            int lastVector = last - Vector128<T>.Count + 1;
            while (i <= lastVector)
            {
                uint mask;
                i = masked
                    ? FindAnyMasked128(ref s, i, lastVector, this.o1, this.o2, Vector128.Create(v1[0]), Vector128.Create(At(v1, 1)), Vector128.Create(At(v1, 2)), Vector128.Create(At(v1, 3)), Vector128.Create(At(v1, 4)), Vector128.Create(At(v1, 5)), Vector128.Create(m1[0]), Vector128.Create(At(m1, 1)), Vector128.Create(At(m1, 2)), Vector128.Create(At(m1, 3)), Vector128.Create(At(m1, 4)), Vector128.Create(At(m1, 5)), Vector128.Create(v2[0]), Vector128.Create(At(v2, 1)), Vector128.Create(At(v2, 2)), Vector128.Create(At(v2, 3)), Vector128.Create(At(v2, 4)), Vector128.Create(At(v2, 5)), Vector128.Create(m2[0]), Vector128.Create(At(m2, 1)), Vector128.Create(At(m2, 2)), Vector128.Create(At(m2, 3)), Vector128.Create(At(m2, 4)), Vector128.Create(At(m2, 5)), out mask)
                    : FindAny128(ref s, i, lastVector, this.o1, this.o2, Vector128.Create(v1[0]), Vector128.Create(At(v1, 1)), Vector128.Create(At(v1, 2)), Vector128.Create(At(v1, 3)), Vector128.Create(At(v1, 4)), Vector128.Create(At(v1, 5)), Vector128.Create(v2[0]), Vector128.Create(At(v2, 1)), Vector128.Create(At(v2, 2)), Vector128.Create(At(v2, 3)), Vector128.Create(At(v2, 4)), Vector128.Create(At(v2, 5)), out mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    length = this.VerifyAny(input, start);
                    if (length >= 0)
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += Vector128<T>.Count;
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

        static T At(T[] values, int k) => k < values.Length ? values[k] : values[0];
    }

    private static int FindAny256(ref T s, int i, int lastVector, int o1, int o2, Vector256<T> p0, Vector256<T> p1, Vector256<T> p2, Vector256<T> p3, Vector256<T> p4, Vector256<T> p5, Vector256<T> q0, Vector256<T> q1, Vector256<T> q2, Vector256<T> q3, Vector256<T> q4, Vector256<T> q5, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> x1 = Vector256.LoadUnsafe(ref cur, (nuint)o1);
            Vector256<T> x2 = Vector256.LoadUnsafe(ref cur, (nuint)o2);
            Vector256<T> eq = (Vector256.Equals(x1, p0) | Vector256.Equals(x1, p1) | Vector256.Equals(x1, p2) | Vector256.Equals(x1, p3) | Vector256.Equals(x1, p4) | Vector256.Equals(x1, p5)) & (Vector256.Equals(x2, q0) | Vector256.Equals(x2, q1) | Vector256.Equals(x2, q2) | Vector256.Equals(x2, q3) | Vector256.Equals(x2, q4) | Vector256.Equals(x2, q5));
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindAny128(ref T s, int i, int lastVector, int o1, int o2, Vector128<T> p0, Vector128<T> p1, Vector128<T> p2, Vector128<T> p3, Vector128<T> p4, Vector128<T> p5, Vector128<T> q0, Vector128<T> q1, Vector128<T> q2, Vector128<T> q3, Vector128<T> q4, Vector128<T> q5, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> x1 = Vector128.LoadUnsafe(ref cur, (nuint)o1);
            Vector128<T> x2 = Vector128.LoadUnsafe(ref cur, (nuint)o2);
            Vector128<T> eq = (Vector128.Equals(x1, p0) | Vector128.Equals(x1, p1) | Vector128.Equals(x1, p2) | Vector128.Equals(x1, p3) | Vector128.Equals(x1, p4) | Vector128.Equals(x1, p5)) & (Vector128.Equals(x2, q0) | Vector128.Equals(x2, q1) | Vector128.Equals(x2, q2) | Vector128.Equals(x2, q3) | Vector128.Equals(x2, q4) | Vector128.Equals(x2, q5));
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindAnyMasked256(ref T s, int i, int lastVector, int o1, int o2, Vector256<T> p0, Vector256<T> p1, Vector256<T> p2, Vector256<T> p3, Vector256<T> p4, Vector256<T> p5, Vector256<T> mp0, Vector256<T> mp1, Vector256<T> mp2, Vector256<T> mp3, Vector256<T> mp4, Vector256<T> mp5, Vector256<T> q0, Vector256<T> q1, Vector256<T> q2, Vector256<T> q3, Vector256<T> q4, Vector256<T> q5, Vector256<T> mq0, Vector256<T> mq1, Vector256<T> mq2, Vector256<T> mq3, Vector256<T> mq4, Vector256<T> mq5, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector256<T> x1 = Vector256.LoadUnsafe(ref cur, (nuint)o1);
            Vector256<T> x2 = Vector256.LoadUnsafe(ref cur, (nuint)o2);
            Vector256<T> eq = (Vector256.Equals(x1 | mp0, p0) | Vector256.Equals(x1 | mp1, p1) | Vector256.Equals(x1 | mp2, p2) | Vector256.Equals(x1 | mp3, p3) | Vector256.Equals(x1 | mp4, p4) | Vector256.Equals(x1 | mp5, p5)) & (Vector256.Equals(x2 | mq0, q0) | Vector256.Equals(x2 | mq1, q1) | Vector256.Equals(x2 | mq2, q2) | Vector256.Equals(x2 | mq3, q3) | Vector256.Equals(x2 | mq4, q4) | Vector256.Equals(x2 | mq5, q5));
            if (eq != Vector256<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector256<T>.Count;
        }

        mask = 0;
        return i;
    }

    private static int FindAnyMasked128(ref T s, int i, int lastVector, int o1, int o2, Vector128<T> p0, Vector128<T> p1, Vector128<T> p2, Vector128<T> p3, Vector128<T> p4, Vector128<T> p5, Vector128<T> mp0, Vector128<T> mp1, Vector128<T> mp2, Vector128<T> mp3, Vector128<T> mp4, Vector128<T> mp5, Vector128<T> q0, Vector128<T> q1, Vector128<T> q2, Vector128<T> q3, Vector128<T> q4, Vector128<T> q5, Vector128<T> mq0, Vector128<T> mq1, Vector128<T> mq2, Vector128<T> mq3, Vector128<T> mq4, Vector128<T> mq5, out uint mask)
    {
        while (i <= lastVector)
        {
            ref T cur = ref Unsafe.Add(ref s, i);
            Vector128<T> x1 = Vector128.LoadUnsafe(ref cur, (nuint)o1);
            Vector128<T> x2 = Vector128.LoadUnsafe(ref cur, (nuint)o2);
            Vector128<T> eq = (Vector128.Equals(x1 | mp0, p0) | Vector128.Equals(x1 | mp1, p1) | Vector128.Equals(x1 | mp2, p2) | Vector128.Equals(x1 | mp3, p3) | Vector128.Equals(x1 | mp4, p4) | Vector128.Equals(x1 | mp5, p5)) & (Vector128.Equals(x2 | mq0, q0) | Vector128.Equals(x2 | mq1, q1) | Vector128.Equals(x2 | mq2, q2) | Vector128.Equals(x2 | mq3, q3) | Vector128.Equals(x2 | mq4, q4) | Vector128.Equals(x2 | mq5, q5));
            if (eq != Vector128<T>.Zero)
            {
                mask = eq.ExtractMostSignificantBits();
                return i;
            }

            i += Vector128<T>.Count;
        }

        mask = 0;
        return i;
    }

    /// <summary>The scan for any of the literals' pairs of adjacent bytes (see <see cref="PairSearch"/>).</summary>
    private int ScanPair(ReadOnlySpan<T> input, int from, int last, out int length)
    {
        length = -1;
        ref byte s = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(input));
        ushort[] pairs = this.pairs;
        int o = this.o1;
        int i = from;
        if (Vector256.IsHardwareAccelerated)
        {
            int lastVector = last - 32 + 1;
            while (i <= lastVector)
            {
                i = PairSearch.Find256(ref s, i, lastVector, o, Vector256.Create(pairs[0]), Vector256.Create(At(pairs, 1)), Vector256.Create(At(pairs, 2)), Vector256.Create(At(pairs, 3)), Vector256.Create(At(pairs, 4)), Vector256.Create(At(pairs, 5)), out uint mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    length = this.VerifyAny(input, start);
                    if (length >= 0)
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += 32;
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            int lastVector = last - 16 + 1;
            while (i <= lastVector)
            {
                i = PairSearch.Find128(ref s, i, lastVector, o, Vector128.Create(pairs[0]), Vector128.Create(At(pairs, 1)), Vector128.Create(At(pairs, 2)), Vector128.Create(At(pairs, 3)), Vector128.Create(At(pairs, 4)), Vector128.Create(At(pairs, 5)), out uint mask);
                if (mask == 0)
                {
                    break;
                }

                do
                {
                    int start = i + System.Numerics.BitOperations.TrailingZeroCount(mask);
                    length = this.VerifyAny(input, start);
                    if (length >= 0)
                    {
                        return start;
                    }

                    mask &= mask - 1;
                }
                while (mask != 0);

                i += 16;
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

        static ushort At(ushort[] values, int k) => k < values.Length ? values[k] : values[0];
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Unit(T u) => typeof(T) == typeof(byte) ? Unsafe.As<T, byte>(ref u) : Unsafe.As<T, ushort>(ref u);

    private static T Or(T u, T mask)
    {
        int v = Unit(u) | Unit(mask);
        if (typeof(T) == typeof(byte))
        {
            byte b = (byte)v;
            return Unsafe.As<byte, T>(ref b);
        }

        ushort c = (ushort)v;
        return Unsafe.As<ushort, T>(ref c);
    }

    private static void Add(List<T> values, List<T> masks, T value, T mask)
    {
        for (int k = 0; k < values.Count; k++)
        {
            if (values[k].Equals(value) && masks[k].Equals(mask))
            {
                return;
            }
        }

        values.Add(value);
        masks.Add(mask);
    }

    /// <summary>The length of the first literal, in the pattern's order, that is at a start, or -1.</summary>
    private int VerifyAny(ReadOnlySpan<T> input, int start)
    {
        Needle<T>[] needles = this.needles;
        if (!this.exact)
        {
            foreach (Needle<T> needle in needles)
            {
                if (input.Length - start >= needle.Length && needle.Verify(input, start))
                {
                    return needle.Length;
                }
            }

            return -1;
        }

        T x1 = input[start + this.o1];
        T x2 = input[start + this.o2];
        T[] a1 = this.a1;
        T[] a2 = this.a2;
        for (int j = 0; j < needles.Length; j++)
        {
            if (x1.Equals(a1[j]) && x2.Equals(a2[j]))
            {
                Needle<T> needle = needles[j];
                if (input.Length - start >= needle.Length && needle.Verify(input, start))
                {
                    return needle.Length;
                }
            }
        }

        return -1;
    }
}

/// <summary>How rare a unit of text is: 255 less its share of text in parts per thousand.</summary>
internal static class UnitRarity
{
    private static readonly byte[] Bytes = MakeBytes();

    /// <summary>The rarity of a byte of UTF-8.</summary>
    /// <param name="b">The byte.</param>
    /// <returns>The rarity.</returns>
    public static int Of(byte b) => Bytes[b];

    /// <summary>The rarity of a UTF-16 code unit.</summary>
    /// <param name="c">The unit.</param>
    /// <returns>The rarity.</returns>
    public static int Of(ushort c)
    {
        if (c < 0x80)
        {
            return Bytes[c];
        }

        // Within its own script every letter is common: the letters of an alphabet share the text between a few
        // dozen values. A CJK character is one of thousands. A high surrogate is one of a few values in use.
        if (c < 0x250 || (c >= 0x370 && c < 0x700) || (c >= 0x900 && c < 0xE80) || (c >= 0x3040 && c < 0x3100) || (c >= 0x1100 && c < 0x1200))
        {
            return 240;
        }

        if (c >= 0xD800 && c < 0xDC00)
        {
            return 200;
        }

        if (c >= 0xDC00 && c < 0xE000)
        {
            return 245;
        }

        return 251;
    }

    /// <summary>The rarity of a unit.</summary>
    /// <typeparam name="T">The unit type, byte or ushort.</typeparam>
    /// <param name="unit">The unit.</param>
    /// <returns>The rarity.</returns>
    public static int Of<T>(T unit)
        where T : unmanaged
    {
        return typeof(T) == typeof(byte) ? Of(Unsafe.As<T, byte>(ref unit)) : Of(Unsafe.As<T, ushort>(ref unit));
    }

    private static byte[] MakeBytes()
    {
        // Higher is rarer: 255 less the byte's share of text in parts per thousand, by the frequency of English
        // letters, with capitals at a thirtieth of their small letters, and by what UTF-8 makes of the others:
        // two- and three-byte lead bytes are the commonest bytes of their scripts, continuation bytes spread over
        // sixty-four values, four-byte leads are rare.
        byte[] r = new byte[256];
        for (int b = 0; b < 256; b++)
        {
            r[b] = 254;
        }

        (char Letter, int PerMille)[] letters =
        [
            ('e', 102), ('t', 73), ('a', 66), ('o', 60), ('i', 56), ('n', 54), ('s', 50), ('h', 49), ('r', 48),
            ('d', 34), ('l', 32), ('c', 22), ('u', 22), ('m', 19), ('w', 19), ('f', 18), ('g', 16), ('y', 16),
            ('p', 15), ('b', 12), ('v', 8), ('k', 6), ('j', 2), ('x', 2), ('q', 1), ('z', 1),
        ];
        foreach ((char letter, int perMille) in letters)
        {
            r[letter] = (byte)(255 - perMille);
            r[char.ToUpperInvariant(letter)] = (byte)(255 - Math.Max(1, perMille / 30));
        }

        r[' '] = 255 - 170;
        r['\n'] = 255 - 20;
        r['\r'] = 255 - 20;
        r['.'] = 255 - 7;
        r[','] = 255 - 8;
        for (int b = 0xC0; b < 0xE0; b++)
        {
            r[b] = 255 - 200;
        }

        for (int b = 0xE0; b < 0xF0; b++)
        {
            r[b] = 255 - 150;
        }

        for (int b = 0x80; b < 0xC0; b++)
        {
            r[b] = 255 - 15;
        }

        return r;
    }
}

/// <summary>Builds needles for each encoding.</summary>
internal static class Needles
{
    /// <summary>A UTF-8 needle for a literal matched exactly.</summary>
    /// <param name="text">The literal, which is not empty.</param>
    /// <returns>The needle.</returns>
    public static Needle<byte> Utf8(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>A UTF-16 needle for a literal matched exactly.</summary>
    /// <param name="text">The literal, which is not empty.</param>
    /// <returns>The needle.</returns>
    public static Needle<ushort> Utf16(string text) => new(MemoryMarshal.Cast<char, ushort>(text.AsSpan()).ToArray());

    /// <summary>A search for any of several literals, each matched exactly.</summary>
    /// <typeparam name="T">The unit: byte for UTF-8, ushort for UTF-16.</typeparam>
    /// <param name="texts">The literals, none empty.</param>
    /// <param name="utf8">Whether the encoding is UTF-8 (otherwise UTF-16).</param>
    /// <param name="leadOnly">Whether to search by the first position alone (see <see cref="MultiNeedle{T}"/>).</param>
    /// <returns>The search.</returns>
    public static MultiNeedle<T> Multi<T>(string[] texts, bool utf8, bool leadOnly = false)
        where T : unmanaged, IEquatable<T>
    {
        Needle<T>[] needles = new Needle<T>[texts.Length];
        for (int k = 0; k < texts.Length; k++)
        {
            needles[k] = new Needle<T>(utf8
                ? MemoryMarshal.Cast<byte, T>(System.Text.Encoding.UTF8.GetBytes(texts[k])).ToArray()
                : MemoryMarshal.Cast<char, T>(texts[k].AsSpan()).ToArray());
        }

        return new MultiNeedle<T>(needles, leadOnly);
    }

    /// <summary>
    /// A needle for a literal whose characters each have one or more forms, over one encoding. A form whose
    /// encoding is not the length of the character's first form cannot be searched for by anchors at fixed
    /// positions: it is left out and returned as an extra, for the caller to look for apart.
    /// </summary>
    /// <param name="forms">For each character, its forms as code points, the first being the pattern's own.</param>
    /// <param name="utf8">Whether the encoding is UTF-8 (otherwise UTF-16).</param>
    /// <param name="extras">The forms left out, as code points.</param>
    /// <param name="window">The most units a match can take, counting the forms left out.</param>
    /// <returns>The needle, which may be unable to search (see <see cref="Needle{T}.CanSearch"/>).</returns>
    public static Needle<T> Forms<T>(int[][] forms, bool utf8, out int[] extras, out int window)
        where T : unmanaged, IEquatable<T>
    {
        List<int> left = [];
        int rowCount = 1;
        foreach (int[] character in forms)
        {
            rowCount = Math.Max(rowCount, character.Length);
        }

        List<T>[] rows = new List<T>[rowCount];
        for (int r = 0; r < rowCount; r++)
        {
            rows[r] = [];
        }

        int[] starts = new int[forms.Length + 1];
        window = 0;
        for (int k = 0; k < forms.Length; k++)
        {
            starts[k] = rows[0].Count;
            int[] character = forms[k];
            int width = Encode<T>(character[0], utf8).Length;
            int widest = width;
            List<T[]> kept = [];
            foreach (int form in character)
            {
                T[] encoded = Encode<T>(form, utf8);
                widest = Math.Max(widest, encoded.Length);
                if (encoded.Length == width)
                {
                    kept.Add(encoded);
                }
                else
                {
                    left.Add(form);
                }
            }

            window += widest;
            for (int r = 0; r < rowCount; r++)
            {
                rows[r].AddRange(kept[Math.Min(r, kept.Count - 1)]);
            }
        }

        starts[forms.Length] = rows[0].Count;
        extras = [.. left];
        T[][] built = new T[rowCount][];
        for (int r = 0; r < rowCount; r++)
        {
            built[r] = [.. rows[r]];
        }

        // Rows that repeat one another (every character has fewer forms than the most) add nothing.
        int distinct = 1;
        for (int r = 1; r < rowCount; r++)
        {
            bool repeat = false;
            for (int q = 0; q < distinct; q++)
            {
                repeat |= built[q].AsSpan().SequenceEqual(built[r]);
            }

            if (!repeat)
            {
                built[distinct++] = built[r];
            }
        }

        return new Needle<T>(built.AsSpan(0, distinct).ToArray(), starts);
    }

    private static T[] Encode<T>(int codePoint, bool utf8)
        where T : unmanaged
    {
        System.Text.StringBuilder sb = new();
        Compiler.AppendRune(sb, codePoint);
        string text = sb.ToString();
        return utf8
            ? MemoryMarshal.Cast<byte, T>(System.Text.Encoding.UTF8.GetBytes(text)).ToArray()
            : MemoryMarshal.Cast<char, T>(text.AsSpan()).ToArray();
    }
}
