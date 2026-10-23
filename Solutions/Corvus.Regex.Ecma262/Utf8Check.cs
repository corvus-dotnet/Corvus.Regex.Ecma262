// <copyright file="Utf8Check.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// The check of a UTF-8 text that the members taking one make before they read it: bytes that are not UTF-8 are refused,
/// never matched, read in part or read as U+FFFD. The members whose names end in <c>Unchecked</c> skip it.
/// </summary>
internal static class Utf8Check
{
    /// <summary>Throws when a text is not UTF-8.</summary>
    /// <param name="utf8">The text.</param>
    /// <param name="paramName">The name of the parameter that holds it.</param>
    /// <exception cref="ArgumentException">The text is not UTF-8.</exception>
    public static void Check(ReadOnlySpan<byte> utf8, string paramName)
    {
#if NET
        if (System.Text.Unicode.Utf8.IsValid(utf8))
        {
            return;
        }
#endif
        int at = FirstInvalid(utf8);
        if (at >= 0)
        {
            throw new ArgumentException($"The text is not UTF-8: the bytes at offset {at} are not a well-formed UTF-8 sequence.", paramName);
        }
    }

    /// <summary>
    /// The offset of the first byte that does not begin a well-formed sequence (Table 3-7 of the Unicode Standard: no
    /// overlong form, no surrogate, nothing beyond U+10FFFF, no sequence cut short), or -1 when the text is UTF-8.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <returns>The offset, or -1.</returns>
    internal static int FirstInvalid(ReadOnlySpan<byte> s)
    {
        int i = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            if (b < 0x80)
            {
                i++;
                continue;
            }

            // The number of continuation bytes, and the range of the first of them (the rest are 80-BF).
            int n;
            byte lo = 0x80, hi = 0xBF;
            if (b >= 0xC2 && b <= 0xDF)
            {
                n = 1;
            }
            else if (b == 0xE0)
            {
                n = 2;
                lo = 0xA0;
            }
            else if ((b >= 0xE1 && b <= 0xEC) || b == 0xEE || b == 0xEF)
            {
                n = 2;
            }
            else if (b == 0xED)
            {
                n = 2;
                hi = 0x9F;
            }
            else if (b == 0xF0)
            {
                n = 3;
                lo = 0x90;
            }
            else if (b >= 0xF1 && b <= 0xF3)
            {
                n = 3;
            }
            else if (b == 0xF4)
            {
                n = 3;
                hi = 0x8F;
            }
            else
            {
                return i;
            }

            if (i + n >= s.Length || s[i + 1] < lo || s[i + 1] > hi)
            {
                return i;
            }

            for (int k = 2; k <= n; k++)
            {
                if ((s[i + k] & 0xC0) != 0x80)
                {
                    return i;
                }
            }

            i += n + 1;
        }

        return -1;
    }
}