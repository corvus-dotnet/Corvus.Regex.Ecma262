// <copyright file="EcmaRegexUnicode.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The sets of code points of ECMA-262 patterns and the operations on them. The sets of the Unicode properties and the
/// case folding of each Unicode version are <see cref="UnicodeTables"/>.
/// </summary>
/// <remarks>
/// A set of code points is an <c>int[]</c> of inclusive ranges, sorted, that neither overlap nor touch. Nothing here
/// runs while a string is matched.
/// </remarks>
internal static class EcmaRegexUnicode
{
    /// <summary>The last code point.</summary>
    public const int Max = 0x10FFFF;

    /// <summary>The steps the operations on sets have taken on this thread since <see cref="TakeSteps"/> last read them.</summary>
    [ThreadStatic]
    private static long steps;

    /// <summary>
    /// The steps the operations on sets have taken on this thread since the last call. The parser counts them
    /// against its limit of steps, where a step stands for the time it takes to read one
    /// character of a pattern and make its node. So a range that is sorted is half a step, a member of a class of
    /// case variants that is looked up is half a step, and a range that is only copied is a sixteenth.
    /// </summary>
    /// <returns>The steps.</returns>
    public static long TakeSteps()
    {
        long taken = steps;
        steps = 0;
        return taken;
    }

    /// <summary>Counts steps that an operation on the sets of <see cref="UnicodeTables"/> has taken on this thread.</summary>
    /// <param name="taken">The steps.</param>
    public static void Spend(long taken) => steps += taken;

    /// <summary>The set of the first <paramref name="count"/> numbers of <paramref name="pairs"/>, read as inclusive ranges in any order.</summary>
    /// <param name="pairs">The ranges.</param>
    /// <param name="count">How many numbers of <paramref name="pairs"/> to read.</param>
    /// <returns>The set.</returns>
    public static int[] SetOf(int[] pairs, int count)
    {
        int n = count / 2;
        steps += 1 + (n / 2);
        long[] keys = new long[n];
        for (int i = 0; i < n; i++)
        {
            keys[i] = ((long)pairs[2 * i] << 32) | (uint)pairs[(2 * i) + 1];
        }

        Array.Sort(keys);
        int[] merged = new int[count];
        int m = 0;
        foreach (long key in keys)
        {
            int lo = (int)(key >> 32);
            int hi = (int)key;
            if (m > 0 && lo <= merged[m - 1] + 1)
            {
                if (hi > merged[m - 1])
                {
                    merged[m - 1] = hi;
                }

                continue;
            }

            merged[m++] = lo;
            merged[m++] = hi;
        }

        if (m != count)
        {
            Array.Resize(ref merged, m);
        }

        return merged;
    }

    /// <summary>Every code point up to <paramref name="max"/> that is not in the set.</summary>
    /// <param name="set">The set.</param>
    /// <param name="max">The last code point of the universe.</param>
    /// <returns>The complement.</returns>
    public static int[] Complement(int[] set, int max = Max)
    {
        steps += 1 + (set.Length / 32);
        int[] result = new int[set.Length + 2];
        int m = 0;
        int next = 0;
        for (int i = 0; i < set.Length && set[i] <= max; i += 2)
        {
            if (set[i] > next)
            {
                result[m++] = next;
                result[m++] = set[i] - 1;
            }

            next = set[i + 1] + 1;
        }

        if (next <= max)
        {
            result[m++] = next;
            result[m++] = max;
        }

        Array.Resize(ref result, m);
        return result;
    }

    /// <summary>Every code point that is in either set.</summary>
    /// <param name="a">The first set.</param>
    /// <param name="b">The second set.</param>
    /// <returns>The union.</returns>
    public static int[] Union(int[] a, int[] b)
    {
        if (a.Length == 0)
        {
            return b;
        }

        if (b.Length == 0)
        {
            return a;
        }

        int[] pairs = new int[a.Length + b.Length];
        Array.Copy(a, pairs, a.Length);
        Array.Copy(b, 0, pairs, a.Length, b.Length);
        return SetOf(pairs, pairs.Length);
    }

    /// <summary>The code points in both sets.</summary>
    /// <param name="a">The first set.</param>
    /// <param name="b">The second set.</param>
    /// <returns>The intersection.</returns>
    public static int[] Intersect(int[] a, int[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return [];
        }

        steps += 1 + ((a.Length + b.Length) / 32);
        int[] result = new int[a.Length + b.Length];
        int m = 0;
        int i = 0;
        int j = 0;
        while (i < a.Length && j < b.Length)
        {
            int lo = Math.Max(a[i], b[j]);
            int hi = Math.Min(a[i + 1], b[j + 1]);
            if (lo <= hi)
            {
                result[m++] = lo;
                result[m++] = hi;
            }

            if (a[i + 1] < b[j + 1])
            {
                i += 2;
            }
            else
            {
                j += 2;
            }
        }

        Array.Resize(ref result, m);
        return result;
    }

    /// <summary>The code points of the first set that are not in the second.</summary>
    /// <param name="a">The first set.</param>
    /// <param name="b">The second set.</param>
    /// <param name="max">The last code point of the grammar.</param>
    /// <returns>The difference.</returns>
    public static int[] Difference(int[] a, int[] b, int max = Max) => b.Length == 0 ? a : Intersect(a, Complement(b, max));

    /// <summary>The code points of the set that lie in the range.</summary>
    /// <param name="set">The set.</param>
    /// <param name="lo">The first code point of the range.</param>
    /// <param name="hi">The last code point of the range.</param>
    /// <returns>The part of the set in the range.</returns>
    public static int[] Slice(int[] set, int lo, int hi)
    {
        steps += 1 + (set.Length / 32);
        int[] result = new int[set.Length];
        int m = 0;
        for (int i = 0; i < set.Length; i += 2)
        {
            int a = Math.Max(set[i], lo);
            int b = Math.Min(set[i + 1], hi);
            if (a <= b)
            {
                result[m++] = a;
                result[m++] = b;
            }
        }

        Array.Resize(ref result, m);
        return result;
    }

    /// <summary>Whether the set holds the code point.</summary>
    /// <param name="set">The set.</param>
    /// <param name="c">The code point.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool Contains(int[] set, int c)
    {
        int lo = 0;
        int hi = set.Length / 2;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (c > set[(2 * mid) + 1])
            {
                lo = mid + 1;
            }
            else if (c < set[2 * mid])
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

    /// <summary>Whether the two sets share a code point.</summary>
    /// <param name="a">The first set.</param>
    /// <param name="b">The second set.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    public static bool Intersects(int[] a, int[] b)
    {
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

    /// <summary>Whether the two sets hold the same code points.</summary>
    /// <param name="a">The first set.</param>
    /// <param name="b">The second set.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    public static bool SetEquals(int[] a, int[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
