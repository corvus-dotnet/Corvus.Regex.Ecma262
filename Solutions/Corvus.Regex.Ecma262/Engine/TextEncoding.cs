// <copyright file="TextEncoding.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// A port of units.rs of the Rust ECMA-262 engine of Corvus.JsonSchema (itself units.go, and what the Go source
// takes from the unicode/utf8 package), generalised over the two encodings the library matches natively.

using System;
using System.Runtime.CompilerServices;

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>
/// Reads the text a match runs over, in one of the two encodings. The matcher is specialised over a struct that
/// implements this interface, so no virtual call and no branch on the encoding is made in its loop.
/// </summary>
/// <typeparam name="TUnit">The unit of the text: <see cref="byte"/> for UTF-8, <see cref="char"/> for UTF-16.</typeparam>
/// <remarks>
/// A position is an offset into the text in its own units. ECMA-262 matches a pattern read without the u flag a
/// UTF-16 code unit at a time, so each encoding can also read the text that way. Over UTF-16 that is the natural
/// reading. Over UTF-8 a character of four bytes is two code units, a high surrogate and a low one, and the position
/// between them is written as the offset of the character's third byte, which is a continuation byte and so no
/// position at all when reading by code point.
/// </remarks>
internal interface ITextEncoding<TUnit>
    where TUnit : unmanaged, IEquatable<TUnit>
{
    /// <summary>The unit at a position, as a number.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <returns>The unit.</returns>
    int Unit(ReadOnlySpan<TUnit> input, int pos);

    /// <summary>The code point at a position and its width in units. Bytes that are not UTF-8 decode to U+FFFD one at a time, and a lone surrogate of UTF-16 to itself.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    int Decode(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>The code point that ends at a position and its width in units.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is more than 0.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    int DecodeBefore(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>The UTF-16 code unit at a position and its width in units, for a pattern matched by code unit.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code unit.</returns>
    int UnitAt(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>The UTF-16 code unit that ends at a position and its width in units.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is more than 0.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code unit.</returns>
    int UnitBefore(ReadOnlySpan<TUnit> input, int pos, out int width);

    /// <summary>Whether the character at a position is one of the four ECMA-262 line terminators.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    bool IsLineTerminatorAt(ReadOnlySpan<TUnit> input, int pos);

    /// <summary>Whether the character that ends at a position is one of the four ECMA-262 line terminators.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is more than 0.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    bool IsLineTerminatorBefore(ReadOnlySpan<TUnit> input, int pos);

    /// <summary>Whether the literal run of an instruction is at a position, and its width in units when it is.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position.</param>
    /// <param name="program">The program.</param>
    /// <param name="lit">The number of the literal.</param>
    /// <param name="width">The width.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    bool LiteralAt(ReadOnlySpan<TUnit> input, int pos, Program program, int lit, out int width);

    /// <summary>The position of the first occurrence of the program's prefix at or after a position, or -1.</summary>
    /// <param name="input">The text.</param>
    /// <param name="from">The position.</param>
    /// <param name="program">The program.</param>
    /// <returns>The position, or -1.</returns>
    int IndexOfPrefix(ReadOnlySpan<TUnit> input, int from, Program program);

    /// <summary>The next position after a position that a match can start at: the next unit that is not a continuation byte.</summary>
    /// <param name="input">The text.</param>
    /// <param name="pos">The position, which is less than the length.</param>
    /// <returns>The position.</returns>
    int NextStart(ReadOnlySpan<TUnit> input, int pos);
}

/// <summary>Reads UTF-8 text.</summary>
internal readonly struct Utf8Encoding : ITextEncoding<byte>
{
    /// <summary>What a byte that is not UTF-8 decodes to, one byte at a time.</summary>
    public const int RuneError = 0xFFFD;

    /// <summary>Whether a unit below 128 is an ECMA-262 word character.</summary>
    /// <param name="b">The unit.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWordUnit(int b)
    {
        return (uint)(b - '0') <= 9 || (uint)((b | 0x20) - 'a') <= 25 || b == '_';
    }

    /// <summary>The code point at the start of a span and its width in bytes: U+FFFD and 1 for bytes that are not UTF-8, and U+FFFD and 0 for no bytes.</summary>
    /// <param name="p">The span.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    public static int DecodeRune(ReadOnlySpan<byte> p, out int width)
    {
        if (p.Length == 0)
        {
            width = 0;
            return RuneError;
        }

        int p0 = p[0];
        if (p0 < 0x80)
        {
            width = 1;
            return p0;
        }

        if (p0 >= 0xC2 && p0 <= 0xDF)
        {
            if (p.Length >= 2 && IsContinuation(p[1]))
            {
                width = 2;
                return ((p0 & 0x1F) << 6) | (p[1] & 0x3F);
            }
        }
        else if (p0 >= 0xE0 && p0 <= 0xEF)
        {
            if (p.Length >= 3 && IsContinuation(p[2]))
            {
                int lo = p0 == 0xE0 ? 0xA0 : 0x80;
                int hi = p0 == 0xED ? 0x9F : 0xBF;
                if (p[1] >= lo && p[1] <= hi)
                {
                    width = 3;
                    return ((p0 & 0x0F) << 12) | ((p[1] & 0x3F) << 6) | (p[2] & 0x3F);
                }
            }
        }
        else if (p0 >= 0xF0 && p0 <= 0xF4)
        {
            if (p.Length >= 4 && IsContinuation(p[2]) && IsContinuation(p[3]))
            {
                int lo = p0 == 0xF0 ? 0x90 : 0x80;
                int hi = p0 == 0xF4 ? 0x8F : 0xBF;
                if (p[1] >= lo && p[1] <= hi)
                {
                    width = 4;
                    return ((p0 & 0x07) << 18) | ((p[1] & 0x3F) << 12) | ((p[2] & 0x3F) << 6) | (p[3] & 0x3F);
                }
            }
        }

        width = 1;
        return RuneError;
    }

    /// <summary>The code point at the end of a span and its width in bytes.</summary>
    /// <param name="p">The span.</param>
    /// <param name="width">The width.</param>
    /// <returns>The code point.</returns>
    public static int DecodeLastRune(ReadOnlySpan<byte> p, out int width)
    {
        int end = p.Length;
        if (end == 0)
        {
            width = 0;
            return RuneError;
        }

        int last = p[end - 1];
        if (last < 0x80)
        {
            width = 1;
            return last;
        }

        // A character is four bytes at most, so its first byte is one of the last four.
        int lim = Math.Max(end - 4, 0);
        int start = end - 1;
        while (start > lim)
        {
            start--;
            if (!IsContinuation(p[start]))
            {
                break;
            }
        }

        int r = DecodeRune(p.Slice(start, end - start), out int size);
        if (start + size != end)
        {
            width = 1;
            return RuneError;
        }

        width = size;
        return r;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Unit(ReadOnlySpan<byte> input, int pos) => input[pos];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ReadOnlySpan<byte> input, int pos, out int width) => DecodeRune(input.Slice(pos), out width);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBefore(ReadOnlySpan<byte> input, int pos, out int width) => DecodeLastRune(input.Slice(0, pos), out width);

    /// <inheritdoc/>
    public int UnitAt(ReadOnlySpan<byte> input, int pos, out int width)
    {
        int c = input[pos];
        if (c >= 0xF0)
        {
            int r = DecodeRune(input.Slice(pos), out int w);
            if (w == 4)
            {
                width = 2;
                return HighSurrogate(r);
            }

            width = 1;
            return RuneError;
        }

        if (c >= 0x80 && c < 0xC0 && pos >= 2 && input[pos - 2] >= 0xF0)
        {
            // Between the halves of the character that starts two bytes back.
            int r = DecodeRune(input.Slice(pos - 2), out int w);
            if (w == 4)
            {
                width = 2;
                return LowSurrogate(r);
            }
        }

        return DecodeRune(input.Slice(pos), out width);
    }

    /// <inheritdoc/>
    public int UnitBefore(ReadOnlySpan<byte> input, int pos, out int width)
    {
        int r = DecodeLastRune(input.Slice(0, pos), out int w);
        if (w == 4)
        {
            width = 2;
            return LowSurrogate(r);
        }

        if (w == 1 && r == RuneError && pos >= 2 && input.Length - pos >= 2 && input[pos - 2] >= 0xF0)
        {
            // Between the halves of the character that starts two bytes back.
            int r2 = DecodeRune(input.Slice(pos - 2), out int w2);
            if (w2 == 4)
            {
                width = 2;
                return HighSurrogate(r2);
            }
        }

        width = w;
        return r;
    }

    /// <inheritdoc/>
    public bool IsLineTerminatorAt(ReadOnlySpan<byte> input, int pos)
    {
        int c = input[pos];
        if (c == '\n' || c == '\r')
        {
            return true;
        }

        // U+2028 and U+2029 are E2 80 A8 and E2 80 A9.
        return c == 0xE2 && pos + 2 < input.Length && input[pos + 1] == 0x80 && (input[pos + 2] == 0xA8 || input[pos + 2] == 0xA9);
    }

    /// <inheritdoc/>
    public bool IsLineTerminatorBefore(ReadOnlySpan<byte> input, int pos)
    {
        int c = input[pos - 1];
        if (c == '\n' || c == '\r')
        {
            return true;
        }

        return (c == 0xA8 || c == 0xA9) && pos >= 3 && input[pos - 3] == 0xE2 && input[pos - 2] == 0x80;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool LiteralAt(ReadOnlySpan<byte> input, int pos, Program program, int lit, out int width)
    {
        byte[] bytes = program.Utf8Literals[lit];
        width = bytes.Length;
        return input.Length - pos >= bytes.Length && input.Slice(pos, bytes.Length).SequenceEqual(bytes);
    }

    /// <inheritdoc/>
    public int IndexOfPrefix(ReadOnlySpan<byte> input, int from, Program program)
    {
        int i = input.Slice(from).IndexOf(program.Utf8Prefix);
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    public int NextStart(ReadOnlySpan<byte> input, int pos)
    {
        pos++;
        while (pos < input.Length && IsContinuation(input[pos]))
        {
            pos++;
        }

        return pos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsContinuation(int b) => (b & 0xC0) == 0x80;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HighSurrogate(int c) => 0xD800 + ((c - 0x10000) >> 10);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LowSurrogate(int c) => 0xDC00 + ((c - 0x10000) & 0x3FF);
}

/// <summary>Reads UTF-16 text.</summary>
internal readonly struct Utf16Encoding : ITextEncoding<char>
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Unit(ReadOnlySpan<char> input, int pos) => input[pos];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ReadOnlySpan<char> input, int pos, out int width)
    {
        char c = input[pos];
        if (char.IsHighSurrogate(c) && pos + 1 < input.Length && char.IsLowSurrogate(input[pos + 1]))
        {
            width = 2;
            return char.ConvertToUtf32(c, input[pos + 1]);
        }

        width = 1;
        return c;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBefore(ReadOnlySpan<char> input, int pos, out int width)
    {
        char c = input[pos - 1];
        if (char.IsLowSurrogate(c) && pos >= 2 && char.IsHighSurrogate(input[pos - 2]))
        {
            width = 2;
            return char.ConvertToUtf32(input[pos - 2], c);
        }

        width = 1;
        return c;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int UnitAt(ReadOnlySpan<char> input, int pos, out int width)
    {
        width = 1;
        return input[pos];
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int UnitBefore(ReadOnlySpan<char> input, int pos, out int width)
    {
        width = 1;
        return input[pos - 1];
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLineTerminatorAt(ReadOnlySpan<char> input, int pos)
    {
        char c = input[pos];
        return c == '\n' || c == '\r' || c == '\u2028' || c == '\u2029';
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsLineTerminatorBefore(ReadOnlySpan<char> input, int pos)
    {
        char c = input[pos - 1];
        return c == '\n' || c == '\r' || c == '\u2028' || c == '\u2029';
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool LiteralAt(ReadOnlySpan<char> input, int pos, Program program, int lit, out int width)
    {
        string chars = program.Utf16Literals[lit];
        width = chars.Length;
        return input.Length - pos >= chars.Length && input.Slice(pos, chars.Length).SequenceEqual(chars.AsSpan());
    }

    /// <inheritdoc/>
    public int IndexOfPrefix(ReadOnlySpan<char> input, int from, Program program)
    {
        int i = input.Slice(from).IndexOf(program.Utf16Prefix.AsSpan());
        return i < 0 ? -1 : from + i;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int NextStart(ReadOnlySpan<char> input, int pos) => pos + 1;
}
